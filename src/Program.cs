/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using Microsoft.Extensions.Logging;
using MTGOSDK.Core.Logging;

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
  }
  if (arg.Equals("--pollidle", StringComparison.OrdinalIgnoreCase))
  {
    pollIdle = true;
  }
}

// Initialize the logging system.
ILoggerFactory factory = LoggerFactory.Create(builder =>
{
  builder.AddSimpleConsole(options =>
  {
    options.IncludeScopes = false;
    options.SingleLine = true;
    options.TimestampFormat = "[HH:mm:ss] ";
  });
  builder.SetMinimumLevel(LogLevel.Debug);
});
LoggerBase.SetFactoryInstance(factory);

// Starts the MTGO Bot in a new subprocess.
var bot = new Runner("BotClient", async () =>
{
  // Main entry point for the MTGO Bot.
  using (var client = new BotClient(!norestart, pollIdle, loggerFactory: factory))
  {
    Log.Information("Finished loading.");
    await client.StartEventQueue();
  }
  
  // If norestart is set, exit with code 99 to signal clean shutdown
  if (norestart)
  {
    Environment.Exit(99);
  }
});
bot.Start();

// Subscribe to the runner exit event to exit the program cleanly.
bot.OnExitRequested += () => Environment.Exit(0);

// If provided a console, block the main thread until a key is pressed.
if (!Console.IsInputRedirected && Console.KeyAvailable)
{
  Log.Information("Press any key to exit.");
  Console.ReadKey();
}
