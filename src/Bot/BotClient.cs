/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading.Tasks;

using MTGOSDK.API;
using MTGOSDK.Core;
using MTGOSDK.Core.Security;

using Database;


namespace Bot;

public class BotClient : IDisposable
{
  public Client Client { get; private set; }

  public BotClient()
  {
    this.Client = new Client(
      RemoteClient.HasStarted
        ? new ClientOptions()
        : new ClientOptions
          {
            CreateProcess = true,
            AcceptEULAPrompt = true
          }
    );

    if (!Client.IsConnected)
    {
      Client.LogOn(
        username: DotEnv.Get("USERNAME"),
        password: DotEnv.Get("PASSWORD")
      ).Wait();
    }
  }

  public async Task StartEventQueue()
  {
    await EventQueue.InitializeQueue();

    // Start for loop that waits every 5 minutes before starting the next batch.
    while (true)
    {
      await EventQueue.ProcessQueue();
      await Task.Delay(TimeSpan.FromMinutes(5));
    }
  }

  public void Dispose() => Client.Dispose();
}
