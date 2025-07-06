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

public struct EventComposite
{
  private Tournament m_tournament = null!;

  public EventEntry                 @event;
  public ICollection<PlayerEntry>   players   = new List<PlayerEntry>();
  public ICollection<StandingEntry> standings =  new List<StandingEntry>();
  public ICollection<MatchEntry>    matches   => standings.SelectMany(s => s.Matches).ToList();
  public ICollection<DeckEntry>     decklists =  new List<DeckEntry>();

  public string DisplayName => m_tournament.ToString();

  public EventComposite(Tournament tournament)
  {
    this.m_tournament = tournament;
  }

  public void BuildCollection(Tournament? tournament = null)
  {
    if (tournament == null) tournament = m_tournament;

    this.@event = new EventEntry(tournament);
    this.players = PlayerEntry.FromEvent(tournament, this.players);
    this.standings = StandingEntry.FromEvent(tournament, this.standings);

    // Get decklists from local server (proxies the Daybreak census API)
    this.decklists =
      EventEntry.GetEventType(tournament) != EventType.Preliminary
        ? DeckEntry.FromEvent(tournament).Result
        : new List<DeckEntry>();
  }

  public EventComposite(int eventId)
      : this(EventManager.GetEvent(eventId) as Tournament
        ?? throw new ArgumentException($"Event {eventId} is not a tournament."))
  { }

  public override string ToString() =>
    string.Join("\n", new string[] {
      m_tournament.ToString(),
      $"  Players:   {players.Count}",
      $"  Standings: {standings.Count}",
      $"  Matches:   {matches.Count}",
      $"  Decklists: {decklists.Count}",
    });
}
