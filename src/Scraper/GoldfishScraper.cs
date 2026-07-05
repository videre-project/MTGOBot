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
  public record LeagueDeckRow(
    int GoldfishDeckId,
    string Label,
    string Player,
    string Record,
    string Archetype,
    int? ArchetypeId);

  public record LeagueData(
    string? MtgoUrl,
    Dictionary<string, LeagueDeckRow> RowsByPlayer);

  private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
  {
    AllowAutoRedirect = true
  })
  {
    Timeout = TimeSpan.FromSeconds(30)
  };

  static GoldfishScraper()
  {
    _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/110.0.0.0 Safari/537.36");
    _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
    _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.mtggoldfish.com/tournament_searches/new");
  }

  private static async Task<IDocument> CreateDocumentAsync(string html)
  {
    var config = Configuration.Default;
    var context = BrowsingContext.New(config);
    return await context.OpenAsync(req => req.Content(html));
  }

  private static async Task<string> GetStringAsync(string url, Action<HttpRequestMessage>? configureRequest = null)
  {
    int retryCount = 0;
    while (retryCount < 3)
    {
      try
      {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        configureRequest?.Invoke(request);

        using var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (content.Trim().Equals("Throttled", StringComparison.OrdinalIgnoreCase) ||
            response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
          Log.Warning("MTGGoldfish throttled the request. Waiting 30s before retrying...");
          await Task.Delay(TimeSpan.FromSeconds(30));
          retryCount++;
          continue;
        }

        response.EnsureSuccessStatusCode();
        return content;
      }
      catch (Exception e) when (e is TaskCanceledException || e is HttpRequestException)
      {
        Log.Warning("Request to {Url} failed or timed out, retrying... (attempt {RetryCount}): {Message}", url, retryCount + 1, e.Message);
        retryCount++;
        await Task.Delay(TimeSpan.FromSeconds(5 * retryCount));
      }
    }

    throw new InvalidOperationException($"Request to {url} failed after 3 retries");
  }

  private static async Task<IDocument> GetDocumentAsync(string url)
  {
    var html = await GetStringAsync(url);
    return await CreateDocumentAsync(html);
  }

  private static async Task<IDocument> GetDeckWidgetDocumentAsync(int deckId)
  {
    var widgetUrl = $"https://www.mtggoldfish.com/widgets/deck/js?deckId={deckId}";
    var response = await GetStringAsync(widgetUrl, request =>
    {
      request.Headers.TryAddWithoutValidation("Accept", "text/javascript, application/javascript, */*; q=0.01");
      request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
    });

    // The widget endpoint returns JavaScript that injects the rendered deck HTML.
    var match = Regex.Match(
      response,
      @"elem\.innerHTML\s*=\s*""(?<html>(?:\\.|[^""\\])*)"";",
      RegexOptions.Singleline
    );

    if (!match.Success)
      throw new InvalidOperationException($"Could not extract deck widget HTML for deck {deckId}");

    var html = Regex.Unescape(match.Groups["html"].Value);
    return await CreateDocumentAsync(html);
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

      // Rate limiting: wait between requests to avoid overwhelming the server
      await Task.Delay(TimeSpan.FromSeconds(2));
    }
  }

  public static async Task<Dictionary<string, int>> GetEventArchetypesAsync(string url)
  {
    var archetypes = new Dictionary<string, int>();
    var doc = await GetDocumentAsync(url);

    var table = doc.QuerySelectorAll("table")
      .FirstOrDefault(t =>
      {
        var headers = t.QuerySelectorAll("th")
          .Select(h => h.TextContent.Trim())
          .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return headers.Contains("Deck")
          && headers.Contains("Percentage")
          && headers.Contains("Total");
      });

    if (table == null) return archetypes;

    var rows = table.QuerySelectorAll("tr > td:first-child > a[href*='/archetype/']");
    foreach (var node in rows)
    {
      var href = node.GetAttribute("href");
      if (href == null) continue;

      var match = Regex.Match(href, @"/archetype/(\d+)");
      if (!match.Success) continue;

      var id = int.Parse(match.Groups[1].Value);
      var name = node.TextContent.Trim();
      archetypes[name] = id;
    }

    return archetypes;
  }

  public static async Task<Dictionary<string, (int Id, string Name, string Archetype, int? ArchetypeId)>> GetPlayerArchetypesAsync(string url)
  {
    Log.Information("Starting GetPlayerArchetypesAsync for {Url}", url);
    var playerArchetypes = new Dictionary<string, (int Id, string Name, string Archetype, int? ArchetypeId)>();
    var archetypes = await GetEventArchetypesAsync(url);
    Log.Information("Got {ArchetypeCount} archetypes", archetypes.Count);

    int pageNum = 1;
    int maxPages = 10; // Default fallback
    bool hasMaxPages = false;

    while (pageNum <= maxPages)
    {
      Log.Information("Fetching page {Page} of {MaxPages}", pageNum, maxPages);
      var pagedUrl = pageNum == 1 ? url : $"{url}?page={pageNum}";
      var doc = await GetDocumentAsync(pagedUrl);

      // Select the main tournament table, which usually contains 'Place' in the first header
      var table = doc.QuerySelectorAll("table.table-tournament")
                     .FirstOrDefault(t => t.QuerySelector("th")?.TextContent.Contains("Place", StringComparison.OrdinalIgnoreCase) == true);

      Log.Debug("Found table at page {Page}: {TableExists}", pageNum, table != null);
      if (table == null)
      {
        Log.Information("No more pages found at page {Page}", pageNum);
        break;
      }

      var rows = table.QuerySelectorAll("tr").Where(r => r.GetAttribute("style") != "display: none;");
      var rowList = rows.ToList();
      Log.Debug("Found {RowCount} rows on page {Page}", rowList.Count, pageNum);

      // Stop if we found too few rows (likely an empty or placeholder page)
      if (rowList.Count < 2)
      {
        Log.Information("Page {Page} has only {Count} rows, stopping pagination", pageNum, rowList.Count);
        break;
      }

      // Try to find pagination info to get actual max pages
      if (!hasMaxPages)
      {
        var pagination = doc.QuerySelector(".pagination, .paginate, div.pagination, .pagination-controls, .paging, .tournament-pagination-info");
        if (pagination != null)
        {
          var paginationText = pagination.TextContent;
          var match = System.Text.RegularExpressions.Regex.Match(paginationText, @"(\d+)\s+pages?|page\s+(\d+)\s+of\s+(\d+)|(\d+)/(\d+)|(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
          if (match.Success)
          {
            // Try to extract max page from various formats
            var total = match.Groups[6].Success ? int.Parse(match.Groups[6].Value) :
                        match.Groups[3].Success ? int.Parse(match.Groups[3].Value) :
                        match.Groups[5].Success ? int.Parse(match.Groups[5].Value) :
                        maxPages;

            maxPages = total;
            hasMaxPages = true;
            Log.Information("Detected {MaxPages} pages from pagination", maxPages);
          }
        }
        else
        {
          // No pagination element found - assume single page tournament
          Log.Information("No pagination element found, assuming single page tournament");
          hasMaxPages = true;
          maxPages = 1;
        }
      }

      int processedCount = 0;
      int initialPlayerCount = playerArchetypes.Count;
      foreach (var row in rowList)
      {
        var links = row.QuerySelectorAll("td > a").ToList();
        if (links.Count < 3) continue;

        var name = links[0].TextContent.Trim();
        var player = links[1].TextContent.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(player)) continue;

        // Extract deck ID: href="javascript:expand_deck(7626769)"
        var id_uri = links[2].GetAttribute("href");
        var match = Regex.Match(id_uri ?? "", @"\((\d+)\)");
        int deckId = match.Success && int.TryParse(match.Groups[1].Value, out var parsedId) ? parsedId : 0;

        // Archetype extraction logic:
        // 1. If name is in archetypes, use it
        // 2. If not, try to find a fallback by removing the first word
        // 3. If fallback is in archetypes, use it
        // 4. If not, use the original name
        string? fallback = name.Split(' ').Length > 1 ? string.Join(" ", name.Split(' ').Skip(1)) : null;
        int? archetypeId = archetypes.ContainsKey(name) ? archetypes[name] : (fallback != null && archetypes.ContainsKey(fallback) ? archetypes[fallback] : (int?)null);
        string? archetype = archetypes.ContainsKey(name) ? name : (fallback != null && archetypes.ContainsKey(fallback) ? fallback : null);

        playerArchetypes[player] = (deckId, name, archetype ?? name, archetypeId);
        processedCount++;
        Log.Debug("Extracted player: {Player}, name: {Name}, archetype: {Archetype}", player, name, archetype ?? name);
      }

      Log.Information("Processed {Count} players on page {Page}", processedCount, pageNum);

      if (processedCount == 0)
      {
        Log.Information("No data found on page {Page}, stopping pagination", pageNum);
        break;
      }

      if (pageNum > 1 && playerArchetypes.Count == initialPlayerCount)
      {
        Log.Information("No new players found on page {Page}, stopping pagination", pageNum);
        break;
      }

      Log.Information("Processed page {Page}, total players: {Count}", pageNum, playerArchetypes.Count);
      pageNum++;

      // Rate limiting: wait between paginated requests
      await Task.Delay(TimeSpan.FromSeconds(2));
    }

    // For unmatched decks, fetch individual deck pages.
    Log.Information("Fetching individual deck pages for {UnmatchedCount} unmatched players", playerArchetypes.Count(p => p.Value.ArchetypeId == null));
    foreach (var player in playerArchetypes.Keys.ToList())
    {
      var data = playerArchetypes[player];
      if (data.Id > 0 && data.ArchetypeId == null)
      {
        try
        {
          Log.Debug("Fetching deck page for {Player} (deck {DeckId})", player, data.Id);
          var deckDoc = await GetDeckWidgetDocumentAsync(data.Id);
          var info = deckDoc.QuerySelector("p.deck-container-information");
          var archLink = info?.QuerySelectorAll("a").FirstOrDefault(l => l.GetAttribute("href").Contains("/archetype/"));

          if (archLink != null)
          {
            string archName = archLink.TextContent.Trim();
            int? archId = (archName != null && archetypes.TryGetValue(archName, out int id)) ? id : (int?)null;
            playerArchetypes[player] = (data.Id, data.Name, archName ?? "", archId);
            Log.Debug("Found archetype {Archetype} for {Player}", archName, player);
          }
        }
        catch (Exception ex)
        {
          Log.Warning("Failed to fetch deck {DeckId} for player {Player}: {Message}", data.Id, player, ex.Message);
        }
      }

      // Rate limiting: wait between individual deck fetches.
      await Task.Delay(TimeSpan.FromSeconds(2));
    }

    return playerArchetypes;
  }

  public static async Task<LeagueData> GetLeagueDataAsync(int tournamentId)
  {
    string goldfishTournamentUrl = $"https://www.mtggoldfish.com/tournament/{tournamentId}";
    var doc = await GetDocumentAsync(goldfishTournamentUrl);
    var mtgoUrl = doc.QuerySelectorAll("a")
      .Select(a => a.GetAttribute("href"))
      .FirstOrDefault(href => href?.Contains("mtgo.com/decklist/", StringComparison.OrdinalIgnoreCase) == true);

    var archetypes = new Dictionary<string, int>();
    var breakdown = doc.QuerySelectorAll("table")
      .FirstOrDefault(t =>
      {
        var headers = t.QuerySelectorAll("th")
          .Select(h => h.TextContent.Trim())
          .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return headers.Contains("Deck")
          && headers.Contains("Percentage")
          && headers.Contains("Total");
      });

    if (breakdown != null)
    {
      foreach (var node in breakdown.QuerySelectorAll("tr > td:first-child > a[href*='/archetype/']"))
      {
        var href = node.GetAttribute("href");
        var match = Regex.Match(href ?? "", @"/archetype/(\d+)");
        if (match.Success)
          archetypes[node.TextContent.Trim()] = int.Parse(match.Groups[1].Value);
      }
    }

    var table = doc.QuerySelectorAll("table.table-tournament")
      .FirstOrDefault(t => t.QuerySelector("th")?.TextContent.Contains("Place", StringComparison.OrdinalIgnoreCase) == true);
    var rowsByPlayer = new Dictionary<string, LeagueDeckRow>(StringComparer.OrdinalIgnoreCase);

    if (table == null) return new LeagueData(mtgoUrl, rowsByPlayer);

    foreach (var row in table.QuerySelectorAll("tr").Where(r => r.GetAttribute("style") != "display: none;"))
    {
      var cells = row.QuerySelectorAll("td").ToList();
      if (cells.Count < 3) continue;

      var deckLink = cells[1].QuerySelector("a[href*='/deck/']");
      var playerLink = cells[2].QuerySelector("a[href*='/player/']");
      if (deckLink == null || playerLink == null) continue;

      var deckHref = deckLink.GetAttribute("href") ?? "";
      var idMatch = Regex.Match(deckHref, @"/deck/(?<id>\d+)");
      if (!idMatch.Success) continue;

      int deckId = int.Parse(idMatch.Groups["id"].Value);
      string label = deckLink.TextContent.Trim();
      string player = playerLink.TextContent.Trim();
      string record = NormalizeRecord(cells[0].TextContent);
      string? fallback = label.Split(' ').Length > 1 ? string.Join(" ", label.Split(' ').Skip(1)) : null;
      int? archetypeId = archetypes.ContainsKey(label)
        ? archetypes[label]
        : (fallback != null && archetypes.ContainsKey(fallback) ? archetypes[fallback] : (int?)null);
      string archetype = archetypeId.HasValue
        ? (archetypes.ContainsKey(label) ? label : fallback!)
        : label;

      rowsByPlayer[player] = new LeagueDeckRow(deckId, label, player, record, archetype, archetypeId);
    }

    return new LeagueData(mtgoUrl, rowsByPlayer);

    static string NormalizeRecord(string? value)
    {
      var normalized = Regex.Replace(value ?? "", @"\s+", "");
      var match = Regex.Match(normalized, @"^(?<wins>\d+)-(?<losses>\d+)(?:-(?<draws>\d+))?$");
      return match.Success
        ? $"{match.Groups["wins"].Value}-{match.Groups["losses"].Value}-{(match.Groups["draws"].Success ? match.Groups["draws"].Value : "0")}"
        : "5-0-0";
    }
  }

  /// <summary>
  /// Updates archetype entries for events in the database.
  /// </summary>
  public static async Task UpdateArchetypesAsync()
  {
    // Temporary backfill window to recover archetypes missed earlier this month.
    var unlabeledEvents = await Database.EventRepository.GetUnlabeledEventsAsync(30);
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

      // Rate limiting: wait between search cycles for different events
      await Task.Delay(TimeSpan.FromSeconds(2));
    }
  }
}
