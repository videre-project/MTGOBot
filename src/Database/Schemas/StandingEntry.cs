/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.API.Users;

using Database.Types;


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
    IList<MatchEntry> matches = new List<MatchEntry>();
    foreach(var match in standing.PreviousMatches)
    {
      matches.Add(new MatchEntry(eventId, match, standing.Player));
    }
    return matches;
  }

  /// <summary>
  /// Formats the tournament record based on the played matches (or byes).
  /// </summary>
  private string GetRecord(IEnumerable<MatchEntry> matches) =>
    string.Format("{0}-{1}-{2}",
        matches.Count(m => m.Result == ResultType.win),
        matches.Count(m => m.Result == ResultType.loss),
        matches.Count(m => m.Result == ResultType.draw));

  /// <summary>
  /// Extracts standing records from a Tournament.
  /// </summary>
  /// <param name="tournament">The Tournament instance to extract standings from.</param>
  /// <returns>A collection of StandingEntry records.</returns>
  public static ICollection<StandingEntry> FromEvent(Tournament tournament)
  {
    int eventId = tournament.Id;
    ICollection<StandingEntry> standings = new List<StandingEntry>();
    foreach(var standing in tournament.Standings)
    {
      standings.Add(new StandingEntry(eventId, standing));
    }

    return standings;
  }

  public override string ToString() =>
    string.Format(
      "({0}, {1}, '{2}', '{3}', {4}, {5}, {6}, {7})",
      EventId, Rank, Player, Record, Points, OMWP, GWP, OGWP
    );
}
