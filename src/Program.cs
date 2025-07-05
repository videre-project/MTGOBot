/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using Bot;

//
// Parse CLI arguments to control the bot's startup behavior.
// --norestart: Prevents the bot from restarting automatically.
// --pollidle: Enables polling for idle status instead of using event-driven updates.
// --ignore-status-check: Ignores the status check when starting the bot, useful for
//
bool norestart = false;
bool pollIdle = true;
foreach (var arg in Environment.GetCommandLineArgs())
{
  if (arg.Equals("--norestart", StringComparison.OrdinalIgnoreCase))
  {
    norestart = true;
    break;
  }
  if (arg.Equals("--pollidle", StringComparison.OrdinalIgnoreCase))
  {
    pollIdle = true;
    break;
  }
}

// Starts the MTGO Bot in a new subprocess.
var bot = new Runner("BotClient", async () =>
{
  // Main entry point for the MTGO Bot.
  using (var client = new BotClient(!norestart, pollIdle))
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
