/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using Bot;


// Starts the MTGO Bot in a new subprocess.
var bot = new Runner("BotClient", async () =>
{
  // Main entry point for the MTGO Bot.
  using (var client = new BotClient(
    restart: true,
    pollIdle: false,
    ignoreStatusCheck: true))
  {
    Console.WriteLine("Finished loading.");
    await client.StartEventQueue();
  }
});
bot.Start();

// Subscribe to the runner exit event to exit the program cleanly.
bot.OnExitRequested += () => Environment.Exit(0);

// If provided a console, block the main thread until a key is pressed.
if (!Console.IsInputRedirected && Console.KeyAvailable)
{
  Console.WriteLine("Press any key to exit.");
  Console.ReadKey();
}
