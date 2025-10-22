/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Play.Tournaments;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using Database.Types;
using Utils;


namespace Database.Schemas;

public struct StandingEntry
{
  public int    EventId { get; set; }
  public int    Rank    { get; set; }
  public string Player  { get; set; }
  public string Record  { get; set; }
  public int    Points  { get; set; }
  public float  OMWP    { get; set; }
  public float  GWP     { get; set; }
  public float  OGWP    { get; set; }

  public ICollection<MatchEntry> Matches { get; private set; }

  public StandingEntry(int eventId, StandingRecord standing)
  {
    this.EventId = eventId;
    this.Rank    = standing.Rank;
    this.Player  = standing.Player.Name;
    this.Points  = standing.Points;
    this.OMWP    = Float(standing.OpponentMatchWinPercentage);
    this.GWP     = Float(standing.GameWinPercentage);
    this.OGWP    = Float(standing.OpponentGameWinPercentage);

    this.Matches = GetMatches(eventId, standing);
    this.Record  = GetRecord(this.Matches);
  }

  /// <summary>
  /// Converts a string percentage to a float.
  /// </summary>
  private static float Float(string value) => float.Parse(value.TrimEnd('%'));

  /// <summary>
  /// Extracts MatchStandingRecord entries from a StandingRecord.
  /// </summary>
  /// <param name="standing">The instance to extract matches from.</param>
  /// <returns>A list of MatchEntry records.</returns>
  private static IList<MatchEntry> GetMatches(int eventId, StandingRecord standing)
  {
    return Retry(() =>
    {
      IList<MatchEntry> matches = new List<MatchEntry>();
      foreach (var match in standing.PreviousMatches)
      {
        var matchEntry = Retry(() => new MatchEntry(eventId, match, standing.Player),
            retries: 5, raise: true);
        matches.Add(matchEntry);
      }
      return matches;
    }, retries: 5, raise: true)!;
  }

  /// <summary>
  /// Formats the tournament record based on the played matches (or byes).
  /// </summary>
  private string GetRecord(IEnumerable<MatchEntry> matches) =>
    string.Format("{0}-{1}-{2}",
        matches.Count(m => m.Result == ResultType.win),
        matches.Count(m => m.Result == ResultType.loss),
        matches.Count(m => m.Result == ResultType.draw));

  // New: override Equals and GetHashCode for collection comparison
  public override bool Equals(object? obj)
  {
    if (obj is not StandingEntry other) return false;
    return EventId == other.EventId && Player == other.Player && Rank == other.Rank;
  }
  public override int GetHashCode() => HashCode.Combine(EventId, Player, Rank);

  /// <summary>
  /// Extracts standing records from a Tournament, optionally resuming from an existing collection.
  /// </summary>
  /// <param name="tournament">The Tournament instance to extract standings from.</param>
  /// <param name="existing">Optional: already-processed standings to persist state.</param>
  /// <returns>A collection of StandingEntry records.</returns>
  public static ICollection<StandingEntry> FromEvent(
    Tournament tournament,
    ICollection<StandingEntry>? existing = null)
  {
    int eventId = tournament.Id;
    ICollection<StandingEntry> standings = existing ?? [];

    int count = tournament.Standings.Count;
    
    using (var progress = new ProgressBar(count, "Processing standings"))
    {
      foreach (var standing in tournament.Standings)
      {
        // Skip any standings that has already been processed
        if (standings.Any(s => s.Rank == standing.Rank))
        {
          progress.Update();
          continue;
        }

        DateTime startTime = DateTime.Now;
        var standingEntry = new StandingEntry(eventId, standing);
        standings.Add(standingEntry);
        
        // Update progress with last processing time
        TimeSpan processingTime = DateTime.Now - startTime;
        progress.Update(suffix: $"(last: {processingTime.TotalSeconds:F1}s)");

        // If it took longer than 5 minutes, throw a timeout exception
        if (processingTime > TimeSpan.FromMinutes(5))
          throw new TimeoutException("Processing standings took too long.");
      }
    } // progress.Dispose() called here, prints final newline

    // Verify standings after processing
    ValidateStandings(standings);
  
    return standings;
  }

  public static void ValidateStandings(ICollection<StandingEntry> standings)
  {
    if (standings.Count == 0)
      throw new InvalidOperationException("No standings found to verify.");

    // Verify that all standings have unique ranks
    var ranks = standings.Select(s => s.Rank).Distinct().ToList();
    if (ranks.Count != standings.Count)
      throw new InvalidOperationException("Duplicate ranks found in standings.");

    // Verify that all players are unique
    var players = standings.Select(s => s.Player).Distinct().ToList();
    if (players.Count != standings.Count)
      throw new InvalidOperationException("Duplicate players found in standings.");

    // Do the points match the number of matches won?
    // 3 points for a win, 1 point for a draw, 0 points for a loss
    foreach (var standing in standings)
    {
      int wins = standing.Matches.Count(m => m.Result == ResultType.win);
      int draws = standing.Matches.Count(m => m.Result == ResultType.draw);
      int expectedPoints = (wins * 3) + draws;

      if (standing.Points != expectedPoints)
        throw new InvalidOperationException(
          $"Points mismatch for {standing.Player}: expected {expectedPoints}, got {standing.Points}.");
    }

    // For matches without a bye, we should have at least one game.
    //
    // A good way to check how many should be to cross-reference the game games
    // from the two players' who were paired against each other that round.
    //
    // We'll create a dictionary of player names to their matches to simplify
    // the lookup.
    var playerMatches = standings
      .SelectMany(s => s.Matches)
      .GroupBy(m => m.Player)
      .ToDictionary(g => g.Key, g => g.ToList());

    // Group matches by Id and verify that results are consistent for both.
    var matchGroups = standings
      .SelectMany(s => s.Matches)
      .GroupBy(m => m.Id)
      .ToList();

    foreach (var group in matchGroups)
    {
      // There should be only one entry if the match is a bye.
      if (group.Count() == 1 || group.Key == -1)
      {
        var match = group.First();
        if (match.IsBye)
          continue; // Bye matches are valid with no games.
        else if (group.Key == -1)
          throw new InvalidOperationException(
            $"Round {match.Round} for {match.Player} has an invalid match ID (-1).");

        // If it's not a bye, we should have at least one game.
        if (match.Games.Length == 0)
          throw new InvalidOperationException(
            $"Match {match.Id} for {match.Player} has no games recorded.");
      }
      else if (group.Count() > 2)
      {
        throw new InvalidOperationException(
          $"Match {group.Key} has more than two entries: {group.Count()} found.");
      }

      // Make sure the game results are consistent between both players.
      // That means a win for one player should be a loss for the other,
      // and a draw should be consistent for both.
      var firstMatch = group.First();
      var secondMatch = group.Skip(1).FirstOrDefault();
      if (!secondMatch.Equals(default(MatchEntry)))
      {
        if (firstMatch.Result == ResultType.win && secondMatch.Result != ResultType.loss ||
            firstMatch.Result == ResultType.loss && secondMatch.Result != ResultType.win ||
            firstMatch.Result == ResultType.draw && secondMatch.Result != ResultType.draw)
        {
          throw new InvalidOperationException(
            $"Inconsistent results for match {firstMatch.Id} between {firstMatch.Player} and {secondMatch.Player}.");
        }
      }
    }
  }

  public override string ToString() =>
    string.Format(
      "({0}, {1}, '{2}', '{3}', {4}, {5}, {6}, {7})",
      EventId, Rank, Player, Record, Points, OMWP, GWP, OGWP
    );
}
