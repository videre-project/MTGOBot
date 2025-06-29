/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Tournaments;

using Database.Schemas;
using Database.Types;


namespace Database;

public struct EventComposite(Tournament tournament)
{
  public EventEntry                 @event    = new EventEntry(tournament);
  public ICollection<PlayerEntry>   players   = PlayerEntry.FromEvent(tournament);
  public ICollection<StandingEntry> standings = StandingEntry.FromEvent(tournament);
  public ICollection<MatchEntry>    matches   => standings.SelectMany(s => s.Matches).ToList();
  // public ICollection<DeckEntry>     decklists = DeckEntry.FromEvent(tournament).Result;
  // public ICollection<DeckEntry>     decklists = default;
  public ICollection<DeckEntry>     decklists =
    // FIXME: Preliminary events are not yet published.
    EventEntry.GetEventType(tournament) != EventType.Preliminary
      ? DeckEntry.FromEvent(tournament).Result
      : new List<DeckEntry>();

  public string DisplayName => tournament.ToString();

  public EventComposite(int eventId)
      : this(EventManager.GetEvent(eventId) as Tournament
        ?? throw new ArgumentException($"Event {eventId} is not a tournament."))
  { }

  public override string ToString() =>
    string.Join("\n", new string[] {
      tournament.ToString(),
      $"  Players:   {players.Count}",
      $"  Standings: {standings.Count}",
      $"  Matches:   {matches.Count}",
      $"  Decklists: {decklists.Count}",
    });
}
