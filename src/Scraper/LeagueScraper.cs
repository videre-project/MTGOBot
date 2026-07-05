/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using MTGOSDK.Core.Logging;

using Database;
using Database.Schemas;
using Database.Types;


namespace Scraper;

public class LeagueScraper
{
  private static readonly FormatType[] Formats =
  [
    FormatType.Standard,
    FormatType.Modern,
    FormatType.Pioneer,
    FormatType.Vintage,
    FormatType.Legacy,
    FormatType.Pauper
  ];

  private static DateTime _lastUpdate = DateTime.MinValue;

  public static async Task UpdateAsync(int days = 14)
  {
    if (DateTime.UtcNow - _lastUpdate < TimeSpan.FromHours(6))
    {
      Log.Debug("Skipping League update; last run was at {LastRun}.", _lastUpdate);
      return;
    }

    _lastUpdate = DateTime.UtcNow;
    var endDate = DateTime.UtcNow.Date.AddDays(1);
    var startDate = DateTime.UtcNow.Date.AddDays(-days);
    int imported = 0;
    int discovered = 0;
    int skippedExisting = 0;

    foreach (var format in Formats)
    {
      int formatSkippedExisting = 0;
      try
      {
        await foreach (var candidate in GoldfishScraper.GetEventsAsync("League", format.ToString(), startDate, endDate))
        {
          if (!TryParseOfficialLeague(candidate.Name, out var parsedFormat, out var date) || parsedFormat != format)
            continue;

          var existing = await EventRepository.GetLeagueImportStatus(format, date);
          if (existing != null && existing.DeckCount > 0)
          {
            skippedExisting++;
            formatSkippedExisting++;
            continue;
          }

          discovered++;
          try
          {
            if (await ImportAsync(candidate.Id, candidate.Name, format, date))
              imported++;
          }
          catch (Exception ex)
          {
            Log.Error("Failed to import League {Name} ({GoldfishId}): {Message}",
              candidate.Name, candidate.Id, ex.Message);
          }

          await Task.Delay(TimeSpan.FromSeconds(1));
        }
      }
      catch (Exception ex)
      {
        Log.Error("Failed to search MTGGoldfish {Format} Leagues: {Message}",
          format, ex.Message);
      }

      if (formatSkippedExisting > 0)
      {
        Log.Debug("Skipped {Count} already-imported {Format} League publication(s).",
          formatSkippedExisting, format);
      }
    }

    Log.Information("League update complete: discovered {Discovered}, imported {Imported}, skipped existing {SkippedExisting}.",
      discovered, imported, skippedExisting);
  }

  private static bool TryParseOfficialLeague(string name, out FormatType format, out DateTime date)
  {
    foreach (var candidateFormat in Formats)
    {
      var match = Regex.Match(
        name,
        $"^{Regex.Escape(candidateFormat.ToString())} League (?<date>20\\d{{2}}-\\d{{2}}-\\d{{2}})(?: \\(\\d+\\))?$",
        RegexOptions.IgnoreCase
      );

      if (match.Success && DateTime.TryParse(match.Groups["date"].Value, out date))
      {
        format = candidateFormat;
        date = date.Date;
        return true;
      }
    }

    format = default;
    date = default;
    return false;
  }

  private static async Task<bool> ImportAsync(
    int goldfishTournamentId,
    string name,
    FormatType format,
    DateTime date)
  {
    var goldfishData = await GoldfishScraper.GetLeagueDataAsync(goldfishTournamentId);
    if (goldfishData.RowsByPlayer.Count == 0)
    {
      Log.Warning("MTGGoldfish League tournament {GoldfishId} had no deck rows.", goldfishTournamentId);
      return false;
    }

    JObject? mtgoData = goldfishData.MtgoUrl != null
      ? await MTGODecklistScraper.GetDecklistDataFromUrl(goldfishData.MtgoUrl)
      : null;
    var mtgoDecks = mtgoData?["decklists"] as JArray;

    // No embedded decklists usually means MTGO.com has not populated this
    // League page yet. Leave it unimported so a later polling pass can retry.
    if (mtgoDecks == null || mtgoDecks.Count == 0)
    {
      Log.Warning("MTGO.com League page for {Name} did not expose embedded decklists at {Url}.",
        name, goldfishData.MtgoUrl ?? "(missing MTGO URL)");
      return false;
    }

    if (mtgoDecks.Count < goldfishData.RowsByPlayer.Count)
    {
      Log.Warning(
        "MTGO.com League page for {Name} exposed fewer decklists than MTGGoldfish ({MtgoCount} vs {GoldfishCount}) at {Url}; leaving it unimported for retry.",
        name,
        mtgoDecks.Count,
        goldfishData.RowsByPlayer.Count,
        goldfishData.MtgoUrl ?? "(missing MTGO URL)");
      return false;
    }

    int eventId = GetLeagueEventId(mtgoData, goldfishTournamentId);
    if (await EventRepository.EventExists(eventId))
    {
      Log.Debug("League {Name} from MTGO.com/MTGGoldfish tournament {GoldfishId} is already imported as event {EventId}.",
        name, goldfishTournamentId, eventId);
      return false;
    }

    var players = new Dictionary<string, PlayerEntry>(StringComparer.OrdinalIgnoreCase);
    var standings = new Dictionary<string, StandingEntry>(StringComparer.OrdinalIgnoreCase);
    var decks = new List<DeckEntry>();
    var archetypes = new List<ArchetypeEntry>();

    foreach (var deck in mtgoDecks)
    {
      string? player = deck["player"]?.ToObject<string>()?.Trim();
      if (string.IsNullOrWhiteSpace(player))
        continue;

      var deckEntry = new DeckEntry(eventId, player, deck);
      if (deckEntry.MainBoard.Length == 0) continue;

      players[player] = new PlayerEntry(player);
      decks.Add(deckEntry);

      var wins = deck["wins"];
      int? winCount = wins?["wins"]?.ToObject<int?>();
      int? lossCount = wins?["losses"]?.ToObject<int?>();
      string record = winCount.HasValue && lossCount.HasValue
        ? $"{winCount.Value}-{lossCount.Value}-0"
        : (goldfishData.RowsByPlayer.TryGetValue(player, out var row) ? row.Record : "5-0-0");
      standings[player] = new StandingEntry(eventId, player, record);

      if (goldfishData.RowsByPlayer.TryGetValue(player, out var archRow))
      {
        archetypes.Add(new ArchetypeEntry(
          archRow.GoldfishDeckId,
          deckEntry.Id,
          archRow.Label,
          archRow.Archetype,
          archRow.ArchetypeId
        ));
      }
    }

    if (decks.Count == 0)
    {
      Log.Warning("League {Name} had no importable decklists.", name);
      return false;
    }

    int rounds = standings.Values
      .Select(s => s.Record.Split('-').Select(int.Parse).Sum())
      .DefaultIfEmpty(5)
      .Max();

    await EventRepository.AddEventPayload(
      new EventEntry(
        eventId,
        $"{format} League",
        date,
        format,
        EventType.League,
        Math.Max(5, rounds),
        Math.Max(4, players.Count)
      ),
      players.Values,
      standings.Values,
      [],
      decks,
      archetypes
    );

    Log.Information("Imported League {Name} from MTGGoldfish tournament {GoldfishId} as event {EventId}.",
      name, goldfishTournamentId, eventId);
    return true;
  }

  private static int GetLeagueEventId(JObject? mtgoData, int goldfishTournamentId)
  {
    string? siteName = mtgoData?["site_name"]?.ToObject<string>();
    if (!string.IsNullOrWhiteSpace(siteName))
      return StableNegativeId($"event:mtgo-league:{siteName.Trim()}");

    string? playEventId = mtgoData?["playeventid"]?.ToObject<string>();
    string? publishDate = mtgoData?["publish_date"]?.ToObject<string>();
    if (!string.IsNullOrWhiteSpace(playEventId) && !string.IsNullOrWhiteSpace(publishDate))
      return StableNegativeId($"event:mtgo-league:{playEventId.Trim()}:{publishDate.Trim()}");

    return StableNegativeId($"event:mtggoldfish-league:{goldfishTournamentId}");
  }

  private static int StableNegativeId(string value)
  {
    uint hash = 2166136261;
    foreach (char c in value)
    {
      hash ^= c;
      hash *= 16777619;
    }

    int id = -1 - (int)(hash % 2147483646);
    return id;
  }
}
