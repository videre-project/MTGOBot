/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.API.Play.Tournaments;

using Database.Types;


namespace Database.Schemas;

public struct EventEntry
{
  public int        Id      { get; set; }
  public string     Name    { get; set; }
  public DateTime   Date    { get; set; }
  public FormatType Format  { get; set; }
  public EventType  Kind    { get; set; }
  public int        Rounds  { get; set; }
  public int        Players { get; set; }

  public EventEntry(Tournament tournament)
  {
    this.Id      = tournament.Id;
    this.Name    = tournament.Description;
    this.Date    = tournament.StartTime;
    this.Format  = GetFormatType(tournament);
    this.Kind    = GetEventType(tournament);
    this.Rounds  = tournament.TotalRounds;
    this.Players = tournament.TotalPlayers;

    // Validate that we have reasonable values
    if (this.Id == 0 || this.Players == 0)
    {
      throw new ArgumentException(
        $"Tournament {tournament.Id} has invalid data: Id={this.Id}, Players={this.Players}, Name={this.Name}");
    }
  }

  /// <summary>
  /// Retrieves the FormatType from the provided tournament's description.
  /// </summary>
  public static FormatType GetFormatType(Tournament tournament)
  {
    if (tournament.Format.Name == "Contraption" &&
        tournament.ToString().Contains("Premodern"))
      return FormatType.Premodern;

    foreach (var format in Enum.GetValues(typeof(FormatType)))
    {
      if (tournament.ToString().Contains(format.ToString()))
        return (FormatType)format;
    }

    throw new ArgumentException(
        $"Provided tournament {tournament.Id} has an invalid format.");
  }

  /// <summary>
  /// Retrieves the EventType from the provided tournament's description.
  /// </summary>
  public static EventType GetEventType(Tournament tournament)
  {
    foreach (var kind in Enum.GetValues(typeof(EventType)))
    {
      if (tournament.ToString().Contains(kind.ToString()))
        return (EventType)kind;

      // Special case for Last Chance Qualifiers
      if (tournament.ToString().Contains("Last Chance"))
        return EventType.Qualifier;
    }

    throw new ArgumentException(
        $"Provided tournament {tournament.Id} has an invalid event type.");
  }

  public override string ToString() =>
    string.Format(
      "({0}, {1}, '{2}', '{3}', '{4}', {5}, {6})",
      Id, Name?.Escape() != null ? $"'{Name.Escape()}'" : "NULL", Date, Format, Kind, Rounds, Players
    );
}
