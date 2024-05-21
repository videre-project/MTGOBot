/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using MTGOSDK.API;
using MTGOSDK.Core;
using MTGOSDK.Core.Reflection;
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

  public BotClient(bool restart = false, bool ignoreStatusCheck = false) : base(
    factory: async () =>
    {
      // Wait until the main MTGO server is online.
      bool online = ignoreStatusCheck;
      while (!online)
      {
        try
        {
          online = await ServerStatus.IsOnline();
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
    // Initialize the client instance.
    Console.WriteLine($"Connecting to MTGO v{Client.Version}...");
    this.Client = new Client(
      !restart && RemoteClient.HasStarted
        ? new ClientOptions()
        : new ClientOptions
          {
            CreateProcess = true,
            StartMinimized = true,
            DestroyOnExit = true,
            AcceptEULAPrompt = true
          }
    );

    // If not connected, attempt to log in.
    DotEnv.LoadFile();
    if (!Client.IsConnected)
    {
      Client.LogOn(
        username: DotEnv.Get("USERNAME"),
        password: DotEnv.Get("PASSWORD")
      ).Wait();
    }

    // Teardown the bot when the MTGO client disconnects.
    // This will trigger a restart of the client when using a runner.
    Client.IsConnectedChanged += delegate(object? sender)
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
    var queue = new EventQueue();
    // Start loop that waits every 5 minutes before starting the next batch.
    while (DateTime.UtcNow < ResetTime)
    {
      if (await queue.ProcessQueue() || Uptime < TimeSpan.FromMinutes(5))
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
          BaseAddress = new Uri("http://localhost:3000"),
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
      // Clear any small object caches to prevent memory leaks on the client.
      Client.ClearCaches();
      await Task.Delay(TimeSpan.FromMinutes(5));
    }
  }

  public void Dispose() => Client.Dispose();
}
