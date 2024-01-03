/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace Bot;

public class Runner
{
  private Action? lambda = null;

  /// <summary>
  /// Starts the runner subprocess.
  /// </summary>
  public void Start() => lambda?.Invoke();

  public Runner(string name, Func<Task> factory)
    : this(name, delegate { factory.Invoke().Wait(); })
  { }

  public Runner(string name, Action factory)
  {
    // Run the runner factory if '--runner <name>' is passed as a CLI argument.
    var cargs = Environment.GetCommandLineArgs();
    if(cargs.SkipWhile((s, i) => s != "--runner" || cargs[i + 1] != name).Any())
    {
      lambda = new Action(() =>
      {
        factory.Invoke();
        Environment.Exit(0);
      });
    }
    // Otherwise, start a new thread to manage the runner.
    else if (!cargs.Contains("--runner"))
    {
      lambda = new Action(() =>
      {
        new Thread(() => StartRunner(name)) { Name = $"Runner-{name}" }.Start();
      });
    }
  }

  /// <summary>
  /// The maximum number of times to restart the runner subprocess.
  /// </summary>
  /// <remarks>
  /// If this is set to <c>null</c>, the runner will only restart based on the
  /// minimum runtime.
  /// </remarks>
  public int? MaxRetries { get; set; } = 3;

  /// <summary>
  /// The minimum amount of time to run the runner subprocess before restarting.
  /// </summary>
  /// <remarks>
  /// This is useful for preventing the runner from restarting too quickly when
  /// the bot is crashing immediately after starting.
  /// <para/>
  /// If this is set to <c>null</c>, the runner will only restart based on the
  /// number of retries.
  /// </remarks>
  public TimeSpan? MinRuntime { get; set; } = TimeSpan.FromMinutes(5);

  /// <summary>
  /// Starts and manages the lifecycle of the runner subprocess.
  /// </summary>
  private void StartRunner(string name)
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
          Arguments = $"--runner {name}",
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

      // Handle early exit conditions to determine whether to restart the bot.
      if (MinRuntime != null)
        exitEarly = bot.ExitTime.Subtract(bot.StartTime) < MinRuntime;
      // Otherwise, increment only based on the number of retries.
      else if (MaxRetries != null)
        tries += 1;
    }
    // Restart the bot on exit unless it has exited early after exceeding the
    // maximum number of retries (if specified), otherwise exit the runner.
    while((tries = exitEarly ? tries + 1 : 0) < (MaxRetries ?? 1));
    Environment.Exit(-1);
  }
}
