/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using MTGOSDK.API.Play.Tournaments;

using Database.Types;
using Scraper;
using static Database.Sql;


namespace Database.Schemas;

public struct DeckEntry
{
  public int                Id        { get; set; }
  public int                EventId   { get; set; }
  public string             Player    { get; set; }
  public CardQuantityPair[] MainBoard { get; set; }
  public CardQuantityPair[] SideBoard { get; set; }

  public DeckEntry(
    int id,
    int eventId,
    string playerName,
    CardQuantityPair[] mainBoard,
    CardQuantityPair[] sideBoard)
  {
    this.Id        = id;
    this.EventId   = eventId;
    this.Player    = playerName;
    this.MainBoard = mainBoard;
    this.SideBoard = sideBoard;
  }

  public DeckEntry(int eventId, string playerName, JToken json)
    : this(
      GetDeckId(json),
      eventId,
      playerName,
      GetBoard(json, "main"),
      GetBoard(json, "sideboard"))
  { }

  private static int GetDeckId(JToken json)
  {
    int? deckTournamentId = json["decktournamentid"]?.ToObject<int?>();
    if (deckTournamentId.HasValue)
      return deckTournamentId.Value;

    int? leagueDeckId = GetLeagueDeckId(json);
    if (leagueDeckId.HasValue)
      return leagueDeckId.Value;

    throw new ArgumentException(
      "Deck JSON does not contain decktournamentid or leaguedeckid."
    );
  }

  private static CardQuantityPair[] GetBoard(JToken json, string key)
  {
    var cards = new List<CardQuantityPair>();
    foreach (var card in json[$"{key}_deck"] ?? new JArray())
      cards.Add(new CardQuantityPair(card));
    return cards.ToArray();
  }

  private static int? GetLeagueDeckId(JToken deck)
  {
    foreach (var board in new[] { "main_deck", "sideboard_deck" })
    {
      foreach (var card in deck[board] ?? new JArray())
      {
        int? value = card["leaguedeckid"]?.ToObject<int?>();
        if (value.HasValue) return value.Value;
      }
    }
    return null;
  }

  /// <summary>
  /// Extracts deck entries from a Tournament.
  /// </summary>
  /// <param name="tournament">The instance to extract decks from.</param>
  /// <returns>A collection of deck entries.</returns>
  /// <exception cref="Exception">Thrown if the API call fails.</exception>
  public static async Task<ICollection<DeckEntry>> FromEvent(Tournament tournament)
  {
    // Create a whitelist of player IDs from the tournament's reported players.
    var playerIds = new List<int>();
    foreach (var player in tournament.Players)
      playerIds.Add(player.Id);

    // Extract the decklists posted on the DayBreak Census API.
    var decks = new List<DeckEntry>();
    var json = await MTGODecklistScraper.GetDecklists(
      tournament.Id,
      tournament.Description,
      tournament.StartTime
    );

    if (json == null)
    {
      throw new Exception($"Failed to fetch decklists for event {tournament.Id}");
    }

    foreach (var deck in json)
    {
      // Skip decks that aren't from players that participated in the event.
      int playerId = deck["loginid"].ToObject<int>();
      if (!playerIds.Contains(playerId))
        continue;

      string playerName = deck["player"].ToObject<string>();
      var deckEntry = new DeckEntry(tournament.Id, playerName, deck);
      decks.Add(deckEntry);
    }

    return decks;
  }

  public override string ToString() =>
    string.Format(
      "({0}, {1}, {2}, {3}, {4})",
      Id, EventId, Literal(Player), MainBoard.FormatArray(), SideBoard.FormatArray()
    );
}
