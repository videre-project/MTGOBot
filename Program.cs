/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using Bot;


using (var client = new BotClient(restart: true))
{
  Console.WriteLine("Finished loading.");
  await client.StartEventQueue();
}
