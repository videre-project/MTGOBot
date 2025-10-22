/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace Utils;

/// <summary>
/// A tqdm-like progress bar for console applications.
/// Works with redirected output (e.g., through pnpm).
/// </summary>
public class ProgressBar : IDisposable
{
  private readonly int _total;
  private readonly DateTime _startTime;
  private readonly string _description;
  private readonly bool _useSameLine;
  private int _current = 0;
  private int _lastPercent = -1;

  public ProgressBar(int total, string description = "Processing")
  {
    _total = total;
    _description = description;
    _startTime = DateTime.Now;
    
    // Only use same-line updates if output is NOT redirected and we can get window info
    _useSameLine = !Console.IsOutputRedirected && CanUseCursorPosition();
  }

  private static bool CanUseCursorPosition()
  {
    try
    {
      var _ = Console.CursorTop;
      var __ = Console.WindowWidth;
      return true;
    }
    catch
    {
      return false;
    }
  }

  /// <summary>
  /// Update the progress bar (like tqdm's update()).
  /// </summary>
  public void Update(int n = 1, string? suffix = null)
  {
    _current += n;
    Render(suffix);
  }

  /// <summary>
  /// Set the current position (like tqdm's n property).
  /// </summary>
  public void SetProgress(int current, string? suffix = null)
  {
    _current = current;
    Render(suffix);
  }

  /// <summary>
  /// Render the progress bar.
  /// </summary>
  private void Render(string? suffix = null)
  {
    double percentage = _total > 0 ? (double)_current / _total * 100 : 0;
    TimeSpan elapsed = DateTime.Now - _startTime;
    
    // Calculate rate and ETA
    double rate = _current > 0 ? _current / elapsed.TotalSeconds : 0;
    TimeSpan eta = rate > 0 && _current < _total
      ? TimeSpan.FromSeconds((_total - _current) / rate)
      : TimeSpan.Zero;

    // Build progress bar
    int barWidth = 30;
    int filled = _total > 0 ? (int)(percentage / 100 * barWidth) : 0;
    string bar = new string('█', filled) + new string('░', barWidth - filled);

    // Format time
    string elapsedStr = FormatTime(elapsed);
    string etaStr = _current < _total ? FormatTime(eta) : "00:00";

    // Build the line
    string line = $"{_description}: {percentage,5:F1}%|{bar}| {_current}/{_total} " +
                  $"[{elapsedStr}<{etaStr}, {rate:F2}it/s]";
    
    if (!string.IsNullOrEmpty(suffix))
    {
      line += $" {suffix}";
    }

    if (_useSameLine)
    {
      // Use carriage return to overwrite same line
      Console.Write($"\r{line}");
      
      // Pad with spaces to clear any remaining characters
      try
      {
        int padding = Math.Max(0, Console.WindowWidth - line.Length - 2);
        if (padding > 0 && padding < 100)
        {
          Console.Write(new string(' ', padding));
        }
      }
      catch { }
      
      Console.Out.Flush();
    }
    else
    {
      // For redirected output, only print at certain intervals
      int currentPercent = (int)percentage;
      if (currentPercent != _lastPercent && 
          (currentPercent % 10 == 0 || _current == _total || _current == 1))
      {
        Console.WriteLine(line);
        _lastPercent = currentPercent;
      }
    }
  }

  /// <summary>
  /// Format time duration like tqdm (MM:SS or HH:MM:SS).
  /// </summary>
  private static string FormatTime(TimeSpan time)
  {
    if (time.TotalHours >= 1)
      return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
    else
      return $"{time.Minutes:D2}:{time.Seconds:D2}";
  }

  /// <summary>
  /// Complete the progress bar and move to next line.
  /// </summary>
  public void Close()
  {
    if (_current < _total)
      _current = _total;
    
    Render();
    
    // Only print newline if we were using same-line updates
    if (_useSameLine)
    {
      Console.WriteLine();
    }
  }

  /// <summary>
  /// Dispose - same as Close().
  /// </summary>
  public void Dispose()
  {
    Close();
    GC.SuppressFinalize(this);
  }
}
