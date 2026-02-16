/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace Bot;

public class Runner
{
  // <summary>
  // The factory method for starting the runner subprocess.
  // </summary>
  private Action? lambda = null;

  // <summary>
  // A list of names of running runner subprocesses.
  // </summary>
  private static ConcurrentDictionary<string, Runner> RunnerThreads = new();

  /// <summary>
  /// Event fired when a clean exit has been requested by the runner subprocess.
  /// </summary>
  public event Action? OnExitRequested;

  /// <summary>
  /// Indicates if a clean exit has been requested by the runner subprocess.
  /// </summary>
  public static volatile bool ExitRequested = false;

  /// <summary>
  /// Starts the runner subprocess.
  /// </summary>
  public void Start()
  {
    // Handle any AggregateExceptions thrown by the lambda to ensure
    // that the runner subprocess doesn't swallow the exception.
    try { lambda?.Invoke(); }
    catch (AggregateException e) { throw e.InnerException ?? e; }
  }

  public Runner(string name, Func<Task> factory)
    : this(name, delegate { (factory.Invoke()).GetAwaiter().GetResult(); })
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
        ExitRequested = true;
        OnExitRequested?.Invoke();
      });
    }
    // Otherwise, start a new thread to manage the runner.
    else if (!cargs.Contains("--runner") && RunnerThreads.TryAdd(name, this))
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
      var mainModule = Process.GetCurrentProcess().MainModule.FileName;
      var arguments = $"--runner {name}";

      //
      // If we are running via the dotnet host (e.g. dotnet.exe MTGOBot.dll), 
      // we need to include the assembly path in the arguments for the subprocess.
      //
      if (mainModule.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
          mainModule.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase))
      {
        var entryAssembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrEmpty(entryAssembly))
        {
          arguments = $"\"{entryAssembly}\" {arguments}";
        }
      }

      var restartDelay = TimeSpan.Zero;
      using var bot = new Process()
      {
        StartInfo = new ProcessStartInfo
        {
          FileName = mainModule,
          Arguments = arguments,
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
        },
        EnableRaisingEvents = true,
      };

      bot.ErrorDataReceived += (s, e) => Console.Error.WriteLine(e.Data);
      bot.OutputDataReceived += (s, e) =>
      {
        if (e.Data?.StartsWith("[Runner:Wait:") == true)
        {
          var waitTime = e.Data.Split(':')[2].TrimEnd(']');
          if (double.TryParse(waitTime, out double ms))
          {
            restartDelay = TimeSpan.FromMilliseconds(ms);
          }
        }
        else if (!string.IsNullOrEmpty(e.Data))
        {
          Console.WriteLine(e.Data);
        }
      };

      // Start the bot subprocess.
      bot.Start();
      bot.BeginOutputReadLine();
      bot.BeginErrorReadLine();
      bot.WaitForExit();

      // Force a garbage collection to clear any memory used during process management
      GC.Collect();
      GC.WaitForPendingFinalizers();
      GC.Collect();

      // If the bot requested a wait, sleep before restarting.
      if (restartDelay > TimeSpan.Zero)
      {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Runner waiting for {restartDelay.TotalMinutes:F1} minutes...");
        Thread.Sleep(restartDelay);
        tries = 0;
        exitEarly = false;
      }

      // If the exit code is 99, this is an intentional clean shutdown.
      if (bot.ExitCode == 99) break;

      // For exit code 0 (normal/reset) or any error code, evaluate restart logic
      // Handle early exit conditions to determine whether to restart the bot.
      if (MinRuntime != null)
      {
        exitEarly = bot.ExitTime.Subtract(bot.StartTime) < MinRuntime;
      }
      // Otherwise, increment only based on the number of retries.
      else if (MaxRetries != null)
      {
        tries += 1;
      }
    }
    // Restart the bot on exit unless it has exited early after exceeding the
    // maximum number of retries (if specified), otherwise exit the runner.
    while((tries = exitEarly ? tries + 1 : 0) < (MaxRetries ?? 1));
  }
}
