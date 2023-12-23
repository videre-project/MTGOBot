/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.API;
using MTGOSDK.Core.Security;

using Bot;


DotEnv.LoadFile();
Console.WriteLine($"Connecting to MTGO v{Client.Version}...");
using (var client = new BotClient())
{
  Console.WriteLine("Finished loading.");
  await client.StartEventQueue();
  Console.WriteLine("Closing MTGO client...");
}
