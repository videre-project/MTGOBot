/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.API.Users;

using Database.Types;
using Scraper;


namespace Database.Schemas;

public struct DeckEntry
{
  public int                Id        { get; set; }
  public int                EventId   { get; set; }
  public string             Player    { get; set; }
  public CardQuantityPair[] MainBoard { get; set; }
  public CardQuantityPair[] SideBoard { get; set; }

  public DeckEntry(int eventId, string playerName, JToken json)
  {
    this.Id        = GetDeckId(json);
    this.EventId   = eventId;
    this.Player    = playerName;
    this.MainBoard = GetBoard(json, "main");
    this.SideBoard = GetBoard(json, "sideboard");
  }

  [Obsolete("Use the constructor that takes a player name string.")]
  public DeckEntry(int eventId, User player, JToken json)
    : this(eventId, player.Name, json)
  { }

  /// <summary>
  /// Retrieves the deck ID from the given JSON object.
  /// </summary>
  private static int GetDeckId(JToken json) =>
    json["decktournamentid"].ToObject<int>();

  /// <summary>
  /// Retrieves a list of cards from the given board type.
  /// </summary>
  private static CardQuantityPair[] GetBoard(JToken json, string key)
  {
    var cards = new List<CardQuantityPair>();
    foreach (var card in json[$"{key}_deck"])
    {
      cards.Add(new CardQuantityPair(card));
    }
    return cards.ToArray();
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
      "({0}, {1}, '{2}', {3}, {4})",
      Id, EventId, Player, MainBoard.FormatArray(), SideBoard.FormatArray()
    );
}
