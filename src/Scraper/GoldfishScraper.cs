/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

using MTGOSDK.Core.Logging;


namespace Scraper;

public class GoldfishScraper
{
  private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
  {
    AllowAutoRedirect = true
  });

  static GoldfishScraper()
  {
    _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/110.0.0.0 Safari/537.36");
    _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
    _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.mtggoldfish.com/tournament_searches/new");
  }

  private static async Task<IDocument> GetDocumentAsync(string url)
  {
    var html = await _httpClient.GetStringAsync(url);
    var config = Configuration.Default;
    var context = BrowsingContext.New(config);
    return await context.OpenAsync(req => req.Content(html));
  }

  public static async IAsyncEnumerable<(int Id, string Name)> GetEventsAsync(string? name, string? format, DateTime startDate, DateTime endDate)
  {
    var dateRange = $"{startDate:MM/dd/yyyy} - {endDate:MM/dd/yyyy}";

    var query = new Dictionary<string, string>();
    if (!string.IsNullOrEmpty(name))
      query.Add("tournament_search[name]", name);
    if (!string.IsNullOrEmpty(format))
      query.Add("tournament_search[format]", format.ToLower());
    
    query.Add("tournament_search[date_range]", dateRange);
    query.Add("commit", "Search");

    using var content = new FormUrlEncodedContent(query);
    var queryString = await content.ReadAsStringAsync();
    var baseUrl = "https://www.mtggoldfish.com/tournament_searches/create";
    var url = $"{baseUrl}?{queryString}";
    
    int page = 1;
    while (true)
    {
      IDocument doc;
      try
      {
        doc = await GetDocumentAsync($"{url}&page={page++}");
      }
      catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.BadRequest)
      {
        // MTGGoldfish returns 400 if the page number is out of range.
        break;
      }

      var table = doc.QuerySelector("table.table.table-striped");
      if (table == null) break;

      var rows = table.QuerySelectorAll("tr > td:nth-child(2) > a");
      if (!rows.Any()) break;

      foreach (var node in rows)
      {
        var href = node.GetAttribute("href");
        if (href == null) continue;

        var id = int.Parse(href.Split('/').Last());
        var eventName = node.TextContent.Trim();
        yield return (id, eventName);
      }
    }
  }

  public static async Task<Dictionary<string, int>> GetEventArchetypesAsync(string url)
  {
    var archetypes = new Dictionary<string, int>();
    var doc = await GetDocumentAsync(url);
    
    var table = doc.QuerySelector("table.table.table-striped.table-sm");
    if (table == null) return archetypes;

    var rows = table.QuerySelectorAll("tr > td:first-child > a");
    foreach (var node in rows)
    {
      var href = node.GetAttribute("href");
      if (href == null) continue;

      var id = int.Parse(href.Split('/').Last());
      var name = node.TextContent.Trim();
      archetypes[name] = id;
    }

    return archetypes;
  }

  public static async Task<Dictionary<string, (int Id, string Name, string Archetype, int? ArchetypeId)>> GetPlayerArchetypesAsync(string url)
  {
    var playerArchetypes = new Dictionary<string, (int Id, string Name, string Archetype, int? ArchetypeId)>();
    var archetypes = await GetEventArchetypesAsync(url);

    int pageNum = 1;
    while (true)
    {
      var pagedUrl = pageNum == 1 ? url : $"{url}?page={pageNum}";
      var doc = await GetDocumentAsync(pagedUrl);
      var table = doc.QuerySelector("table.table-tournament");
      if (table == null) break;

      var rows = table.QuerySelectorAll("tr").Where(r => r.GetAttribute("style") != "display: none;");
      bool foundData = false;
      foreach (var row in rows)
      {
        var links = row.QuerySelectorAll("td > a").ToList();
        if (links.Count < 3) continue;

        var name = links[0].TextContent.Trim();
        var player = links[1].TextContent.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(player)) continue;

        foundData = true;
        // Extract deck ID: href="javascript:expand_deck(7626769)"
        var id_uri = links[2].GetAttribute("href");
        var match = Regex.Match(id_uri ?? "", @"\((\d+)\)");
        int deckId = match.Success ? int.Parse(match.Groups[1].Value) : 0;

        // Archetype extraction logic:
        // 1. If name is in archetypes, use it
        // 2. If not, try to find a fallback by removing the first word
        // 3. If fallback is in archetypes, use it
        // 4. If not, use the original name
        string? fallback = name.Split(' ').Length > 1 ? string.Join(" ", name.Split(' ').Skip(1)) : null;
        int? archetypeId = archetypes.ContainsKey(name) ? archetypes[name] : (fallback != null && archetypes.ContainsKey(fallback) ? archetypes[fallback] : (int?)null);
        string? archetype = archetypes.ContainsKey(name) ? name : (fallback != null && archetypes.ContainsKey(fallback) ? fallback : null);

        playerArchetypes[player] = (deckId, name, archetype ?? name, archetypeId);
      }

      if (!foundData) break;
      pageNum++;
    }

    // For unmatched decks, fetch individual deck pages
    foreach (var player in playerArchetypes.Keys.ToList())
    {
      var data = playerArchetypes[player];
      if (data.Id > 0 && data.ArchetypeId == null)
      {
        var deckUrl = $"https://www.mtggoldfish.com/deck/{data.Id}";
        var deckDoc = await GetDocumentAsync(deckUrl);
        var info = deckDoc.QuerySelector("p.deck-container-information");
        var archLink = info?.QuerySelectorAll("a").FirstOrDefault(l => l.GetAttribute("href").Contains("/archetype/"));
        
        if (archLink != null)
        {
          string archName = archLink.TextContent.Trim();
          int? archId = (archName != null && archetypes.TryGetValue(archName, out int id)) ? id : (int?)null;
          playerArchetypes[player] = (data.Id, data.Name, archName ?? "", archId);
        }
      }
    }

    return playerArchetypes;
  }

  /// <summary>
  /// Updates archetype entries for events in the database.
  /// </summary>
  public static async Task UpdateArchetypesAsync()
  {
    var unlabeledEvents = await Database.EventRepository.GetUnlabeledEventsAsync(7);
    foreach (var @event in unlabeledEvents)
    {
      try
      {
        var decks = await Database.EventRepository.GetDecksByEventAsync(@event.Id);
        if (decks.Count() < 4) continue;

        Log.Information("Searching MTGGoldfish for event {Name} ({Date})", @event.Name, @event.Date.ToString("yyyy-MM-dd"));
        
        // Construct the source MTGO URL to match against (mimics GetMTGOUrl)
        var sourceUrl = $"https://www.mtgo.com/decklist/{@event.Name.ToLower().Replace(" ", "-")}-{@event.Date:yyyy-MM-dd}{@event.Id}";

        // Use the name prefix for searching (the part before the first " - ")
        string namePrefix = @event.Name.Split(" - ")[0];
        
        // Search for candidate matches on MTGGoldfish
        var startDate = @event.Date.AddDays(-1);
        var endDate = @event.Date.AddDays(2);
        
        string? matchedUrl = null;
        await foreach (var candidate in GetEventsAsync(namePrefix, @event.Format.ToString(), startDate, endDate))
        {
          // Optimization: Align with jobs.ts and FilterEventList logic.
          // Goldfish often formats tournament names as "Prefix #EventID" or "Prefix - Suffix #EventID".
          bool containsId = candidate.Name.Contains(@event.Id.ToString());
          bool startsWithPrefix = candidate.Name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase);

          // If the Goldfish title has a different ID, it's definitely not our event.
          // MTGGoldfish IDs are prefixed with '#' in the tournament name list.
          if (candidate.Name.Contains("#") && !containsId)
          {
            continue;
          }

          // If it doesn't even contain the prefix, it's likely a false positive from the search.
          if (!startsWithPrefix && !candidate.Name.Contains(namePrefix, StringComparison.OrdinalIgnoreCase))
          {
            continue;
          }

          var url = $"https://www.mtggoldfish.com/tournament/{candidate.Id}";
          var doc = await GetDocumentAsync(url);
          var match = doc.QuerySelectorAll("div > p > a").FirstOrDefault(a => a.GetAttribute("href").Contains("mtgo.com/decklist/"))?.GetAttribute("href");
          
          if (match == sourceUrl || match?.EndsWith(@event.Id.ToString()) == true)
          {
            matchedUrl = url;
            break;
          }
        }

        if (matchedUrl != null)
        {
          Log.Information("Found MTGGoldfish match: {Url}", matchedUrl);
          var archetypeMap = await GetPlayerArchetypesAsync(matchedUrl);
          
          var archetypeEntries = new List<Database.Schemas.ArchetypeEntry>();
          foreach (var deck in decks)
          {
            if (archetypeMap.TryGetValue(deck.Player, out var archData))
            {
              archetypeEntries.Add(new Database.Schemas.ArchetypeEntry(
                archData.Id,
                deck.Id,
                archData.Name,
                archData.Archetype,
                archData.ArchetypeId
              ));
            }
          }

          if (archetypeEntries.Any())
          {
            await Database.EventRepository.AddArchetypeEntries(archetypeEntries);
            Log.Information("Updated {Count} archetypes for event {EventId}", archetypeEntries.Count, @event.Id);
          }
          else
          {
            Log.Information("No archetypes found for event {EventId}", @event.Id);
          }
        }
        else
        {
          Log.Information("No MTGGoldfish match found for event {EventId}", @event.Id);
        }
      }
      catch (Exception ex)
      {
        Log.Error("Error updating archetypes for event {EventId}: {Message}", @event.Id, ex.Message);
      }
    }
  }
}
