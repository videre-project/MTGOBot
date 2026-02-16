/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using MTGOSDK.API;
using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Memory;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Security;


namespace Bot;

public class BotClient : DLRWrapper<Client>, IDisposable
{
  /// <summary>
  /// The current uptime of the bot client.
  /// </summary>
  public static TimeSpan Uptime =>
    DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime();

  /// <summary>
  /// The next reset time to restart the MTGO client and event queue.
  /// </summary>
  public static DateTime ResetTime = GetResetTime();

  private static DateTime GetResetTime(int numResets = 12)
  {
    // By default, we reset at 1:30 AM UTC and every 2 hours thereafter.
    var resetTime = DateTime.UtcNow.Date.AddHours(1).AddMinutes(30);

    TimeSpan interval = (resetTime.AddDays(1) - resetTime) / numResets;
    DateTime[] times = new DateTime[numResets + 1];
    for (int i = 0; i <= numResets; i++)
    {
      times[i] = resetTime + (interval * i);
    }

    var nextReset = times.FirstOrDefault(t => t > DateTime.UtcNow);
    return nextReset != default(DateTime) 
        ? nextReset 
        : times.First() + TimeSpan.FromDays(1);
  }

  /// <summary>
  /// The instance of the MTGO client handle.
  /// </summary>
  public Client Client { get; private set; }
  
  public bool PollIdle { get; set; }

  private readonly ILoggerFactory _loggerFactory;

  public BotClient(
      bool restart = false,
      bool pollIdle = true,
      bool ignoreStatusCheck = false,
      ILoggerFactory? loggerFactory = null) : base(
    factory: async () =>
    {
      // Wait until the main MTGO server is online.
      bool online = ignoreStatusCheck;
      while (!online)
      {
        try
        {
          online = await Client.IsOnline();
        }
        catch (Exception)
        {
          //
          // If an exception is thrown, we have found an issue with the API.
          // For now, we'll assume the servers are online and test through login.
          //
          break;
        }
        if (!online)
        {
          Log.Information("MTGO servers are currently offline. Waiting...");
          await Task.Delay(TimeSpan.FromMinutes(30));
          restart |= true; // Restart after downtime.
        }
      }
    })
  {
    DotEnv.LoadFile();

    this._loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    this.Client = null!;
    StartClient();

    this.PollIdle = pollIdle;
  }

  public void StartClient(bool restart = false)
  {
    bool isColdStart = true;
    if (this.Client != null)
    {
      isColdStart = false;
      restart = true;

      // Clear any connection events
      Client.IsConnectedChanged.Clear();
      Client.Dispose();

      Log.Information("Restarting MTGO client...");
      Task.Run(async delegate
      {
        if (!await WaitUntil(() => !RemoteClient.KillProcess(), 10))
        {
          throw new SetupFailureException("Unable to restart MTGO.");
        }

        await RemoteClient.StartProcess();
      }).Wait();
    }

    // Initialize the client instance.
    Log.Information("Connecting to MTGO v{Version}...", Client.Version);
    this.Client = new Client(
      !restart && RemoteClient.HasStarted
        ? new ClientOptions()
        : new ClientOptions
        {
          CreateProcess = true,
          StartMinimized = true,
          CloseOnExit = true,
          AcceptEULAPrompt = isColdStart
        },
      loggerFactory: _loggerFactory
    );

    // If not connected, attempt to log in.
    if (!Client.IsConnected)
    {
      Client.LogOn(
        username: DotEnv.Get("USERNAME"),
        password: DotEnv.Get("PASSWORD")
      ).Wait();
    }

    // Teardown the bot when the MTGO client disconnects.
    // This will trigger a restart of the client when using a runner.
    Client.IsConnectedChanged += delegate (object? sender)
    {
      Log.Information("The MTGO client has been disconnected. Stopping...");
      Client.Dispose();
      Environment.Exit(-1);
    };
  }

  /// <summary>
  /// Blocks the current thread processing the event queue.
  /// </summary>
  public async Task StartEventQueue()
  {
    // Start loop that waits every 5 minutes before starting the next batch.
    while (DateTime.UtcNow < ResetTime)
    {
      // Suppress GC events while the event queue is running.
      using var gcCtx = GCTimer.SuppressGC();

      // Process the event queue. This will return true if there are events
      var queue = new EventQueue();
      bool hasEventsProcessed = await queue.ProcessQueue(this);
      if (!PollIdle) break;

      // Clear any small object caches to prevent memory leaks on the client.
      Client.ClearCaches();

      gcCtx.Dispose();
      GC.Collect();
      GC.WaitForPendingFinalizers();

      // We've processed all finished events but there are upcoming events still.
      // Here, we'll wait until the next event ends or reset time is reached.
      TimeSpan waitTime = TimeSpan.Zero;
      if (queue.Queue.IsEmpty && !queue.UpcomingQueue.IsEmpty)
      {
        var nextEvent = queue.UpcomingQueue.MinBy(e => e.EndTime);
        var now = DateTime.Now;
        var resetTimeLocal = ResetTime.ToLocalTime();

        // If the next event has already ended, skip the wait and retry immediately
        if (nextEvent.EndTime <= now)
        {
          Log.Information(
            "Next event already ended at {EndTime}, retrying immediately...",
            nextEvent.EndTime
          );
          waitTime = TimeSpan.FromSeconds(1);
        }
        // If the next event ends before reset time, wait until then.
        else if (nextEvent.EndTime < resetTimeLocal)
        {
          waitTime = nextEvent.EndTime - now;
          if (waitTime < TimeSpan.Zero) waitTime = TimeSpan.Zero;
          Log.Information(
            "Waiting until next event ends at {EndTime} ({WaitTime} minutes)...",
            nextEvent.EndTime,
            waitTime.TotalMinutes.ToString("F1")
          );
        }
        else
        {
          waitTime = resetTimeLocal - now;
          if (waitTime < TimeSpan.Zero) waitTime = TimeSpan.Zero;
          Log.Information(
            "Next event ends after reset time at {EndTime}, waiting until reset time at {ResetTime} ({WaitTime} minutes)...",
            nextEvent.EndTime,
            resetTimeLocal,
            waitTime.TotalMinutes.ToString("F1")
          );
        }
      }
      // If the upcoming event queue is empty, wait until reset time
      else if (queue.UpcomingQueue.IsEmpty)
      {
        waitTime = ResetTime - DateTime.UtcNow;
        if (waitTime < TimeSpan.Zero) waitTime = TimeSpan.Zero;
        Log.Information(
          "No upcoming events, waiting until reset time at {ResetTime} ({WaitTime} minutes)...",
          ResetTime,
          waitTime.TotalMinutes.ToString("F1")
        );
      }
      // There are events in the current queue to process still.
      // Wait a while to retry so MTGO has a change to perform GC.
      else
      {
        Log.Information("Events still in queue, waiting before retrying...");
        waitTime = TimeSpan.FromMinutes(5);
      }

      if (hasEventsProcessed || Uptime < TimeSpan.FromMinutes(5))
      {
        //
        // Check every 10 minutes until archetypes are updated for all processed
        // events. This can take between 20-30 minutes for all external sources
        // to be updated.
        //
        await Scraper.GoldfishScraper.UpdateArchetypesAsync();
      }

      // If we are waiting for a significant amount of time, we should dispose
      if (waitTime > TimeSpan.FromMinutes(1))
      {
        Log.Information("Disposing of MTGO client to free resources...");
        Client.IsConnectedChanged.Clear();
        Client?.Dispose();
        this.Client = null!;

        // Signal the runner to wait for the specified time before restarting.
        Console.WriteLine($"[Runner:Wait:{waitTime.TotalMilliseconds:F0}]");
        break;
      }
      else
      {
        // Clear any small object caches to prevent memory leaks on the client.
        Client.ClearCaches();

        gcCtx.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();
      }

      // Wait for the next event or reset time.
      if (waitTime > TimeSpan.Zero)
      {
        Log.Information("Waiting {WaitTime} minutes before retrying...", waitTime.TotalMinutes);
        await Task.Delay(waitTime);
      }

      // If we disposed of the client, we need to restart the bot.
      if (this.Client == null)
      {
        Log.Information("Restarting bot to process next batch of events...");
        break;
      }

    }

    // If we exited because reset time was reached, exit cleanly for restart
    if (DateTime.UtcNow >= ResetTime)
    {
      Log.Information("Reset time reached. Restarting bot...");
    }
  }

  public void Dispose() => Client?.Dispose();
}
