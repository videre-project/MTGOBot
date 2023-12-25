/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Linq;
using System.Diagnostics;

using Bot;


// Starts the MTGO Bot runner in a new subprocess.
var cargs = Environment.GetCommandLineArgs();
if(!(cargs.Contains("--no-restart") || cargs.Contains("--subprocess")))
{
  int tries = 0;
  bool exitEarly = false;
  do
  {
    using var bot = new Process()
    {
      StartInfo = new ProcessStartInfo
      {
        FileName = Process.GetCurrentProcess().MainModule.FileName,
        Arguments = "--subprocess",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
      },
      EnableRaisingEvents = true,
    };
    bot.ErrorDataReceived += (s, e) => Console.Error.WriteLine(e.Data);
    bot.OutputDataReceived += (s, e) => Console.WriteLine(e.Data);

    // Start the bot subprocess.
    bot.Start();
    bot.BeginOutputReadLine();
    bot.BeginErrorReadLine();
    bot.WaitForExit();

    exitEarly = (bot.ExitTime - bot.StartTime).TotalMinutes <= 5;
  }
  // Restart the bot on exit unless it has exited early on the last 3 attempts.
  while((tries = exitEarly ? tries + 1 : 0) <= 3);
  Environment.Exit(-1);
}

// Main entry point for the MTGO Bot.
using (var client = new BotClient(restart: true))
{
  Console.WriteLine("Finished loading.");
  await client.StartEventQueue();
}
