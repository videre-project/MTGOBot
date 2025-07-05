/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.API.Users;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using Database.Types;


namespace Database.Schemas;

public struct MatchEntry
{
  public int?          Id       { get; set; }
  public int           EventId  { get; set; }
  public int           Round    { get; set; }
  public string        Player   { get; set; }
  public string?       Opponent { get; set; }
  public string        Record   { get; set; }
  public ResultType    Result   { get; set; }
  public bool          IsBye    { get; set; }
  public GameResult[]  Games    { get; set; }

  public MatchEntry(int eventId, MatchStandingRecord match, User player)
  {
    this.Id       = match.Id;
    this.EventId  = eventId;
    this.Round    = match.Round;
    this.Player   = player.Name;
    this.Opponent = GetOpponent(match.Players, player) ?? string.Empty;
    this.Result   = GetResult(match, player);
    this.IsBye    = match.HasBye;

    this.Games    = GetGames(match, player);
    this.Record   = GetRecord(this.IsBye, this.Games);
  }

  /// <summary>
  /// Retrieves the opponent's name from the given list of players.
  /// </summary>
  private static string? GetOpponent(IEnumerable<User> players, User player) =>
    ((IEnumerable<User>)players).FirstOrDefault(p => p.Id != player.Id)?.Name;

  /// <summary>
  /// Retrieves the result of the match for the given player.
  /// </summary>
  private static ResultType GetResult(MatchStandingRecord match, User player) =>
    match.HasBye
      ? ResultType.win
      : match.WinningPlayerIds.Contains(player.Id)
        ? ResultType.win
        : match.LosingPlayerIds.Contains(player.Id)
          ? ResultType.loss
          : ResultType.draw;

  /// <summary>
  /// Extracts GameStandingRecord entries from a MatchStandingRecord.
  /// </summary>
  private static GameResult[] GetGames(MatchStandingRecord match, User player)
  {
    var games = new List<GameResult>();
    foreach (var game in match.GameStandingRecords)
    {
      var gameResult = Retry(() => new GameResult(game, player),
          retries: 5, raise: true);
      games.Add(gameResult);
    }
    return games.ToArray();
  }

  /// <summary>
  /// Formats the match record based on the played games (or bye).
  /// </summary>
  private static string GetRecord(bool isBye, IEnumerable<GameResult> games) =>
    isBye
      ? "2-0-0"
      : string.Format("{0}-{1}-{2}",
          games.Count(g => g.Result == ResultType.win),
          games.Count(g => g.Result == ResultType.loss),
          games.Count(g => g.Result == ResultType.draw));

  public override string ToString() =>
    string.Format(
      "({0}, {1}, {2}, '{3}', '{4}', '{5}', '{6}', {7}, {8})",
      Id, EventId, Round, Player, Opponent, Record, Result, IsBye,
      Games.FormatArray()
    ).Replace(@", '', ", @", NULL, ");
}
