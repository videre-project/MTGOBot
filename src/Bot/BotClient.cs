/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using MTGOSDK.API;
using MTGOSDK.Core.Exceptions;
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
    var resetTime = DateTime.UtcNow.Date.AddHours(1).AddMinutes(30);

    TimeSpan interval = (resetTime.AddDays(1) - resetTime) / numResets;
    DateTime[] times = new DateTime[numResets + 1];
    for (int i = 0; i <= numResets; i++)
    {
      times[i] = resetTime + (interval * i);
    }

    return times.FirstOrDefault(t => t > DateTime.UtcNow);
  }

  /// <summary>
  /// The instance of the MTGO client handle.
  /// </summary>
  public Client Client { get; private set; }
  
  public bool PollIdle { get; set; }

  public BotClient(
      bool restart = false,
      bool pollIdle = true,
      bool ignoreStatusCheck = false) : base(
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
          Console.WriteLine("MTGO servers are currently offline. Waiting...");
          await Task.Delay(TimeSpan.FromMinutes(30));
          restart |= true; // Restart after downtime.
        }
      }
    })
  {
    DotEnv.LoadFile();

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

      Console.WriteLine("Restarting MTGO client...");
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
    Console.WriteLine($"Connecting to MTGO v{Client.Version}...");
    this.Client = new Client(
      !restart && RemoteClient.HasStarted
        ? new ClientOptions()
        : new ClientOptions
        {
          CreateProcess = true,
          StartMinimized = true,
          CloseOnExit = true,
          AcceptEULAPrompt = isColdStart
        }
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
      Console.WriteLine("The MTGO client has been disconnected. Stopping...");
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
    var queue = new EventQueue();
    while (DateTime.UtcNow < ResetTime)
    {
      // Suppress GC events while the event queue is running.
      using var gcCtx = GCTimer.SuppressGC();

      // Process the event queue. This will return true if there are events
      if (await queue.ProcessQueue(this) || Uptime < TimeSpan.FromMinutes(5))
      {
        //
        // Check every 10 minutes until archetypes are updated for all processed
        // events. This can take between 20-30 minutes for all external sources
        // to be updated.
        //
        // This will send a request to the local server to handle updating the
        // archetype entries for the processed events.
        //
        using (var client = new HttpClient()
        {
          BaseAddress = new Uri("http://localhost:3001"),
          Timeout = TimeSpan.FromMinutes(5)
        })
        {
          HttpResponseMessage res = null!;
          int retries = 7;
          while (retries-- > 0)
          {
            res = await client.PostAsync("/events/update-archetypes", null);
            if (res.IsSuccessStatusCode) break;
            await Task.Delay(TimeSpan.FromMinutes(10));
          }
        }
      }
      if (!PollIdle) break;

      // Clear any small object caches to prevent memory leaks on the client.
      Client.ClearCaches();
      await Task.Delay(TimeSpan.FromMinutes(5));
    }
  }

  public void Dispose() => Client.Dispose();
}
