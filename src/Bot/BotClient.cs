/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Linq;
using System.Threading.Tasks;

using MTGOSDK.API;
using MTGOSDK.Core;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Security;

using Database;


namespace Bot;

public class BotClient : DLRWrapper<Client>, IDisposable
{
  /// <summary>
  /// The next reset time to restart the MTGO client and event queue.
  /// </summary>
  private static DateTime ResetTime = GetResetTime();

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

  public BotClient(bool restart = false) : base(
    factory: async delegate
    {
      // Wait until the main MTGO server is online.
      while (!await ServerStatus.IsOnline())
      {
        restart |= true; // Restart after downtime.
        await Task.Delay(TimeSpan.FromMinutes(30));
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
      await queue.ProcessQueue();
      await Task.Delay(TimeSpan.FromMinutes(5));
      // Clear any small object caches to prevent memory leaks on the client.
      Client.ClearCaches();
    }
  }

  public void Dispose() => Client.Dispose();
}
