/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Net.Http;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using MTGOSDK.Core.Logging;


namespace Scraper;

public static class MTGODecklistScraper
{
  private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
  {
    AllowAutoRedirect = true
  });

  static MTGODecklistScraper()
  {
    _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/110.0.0.0 Safari/537.36");
  }

  /// <summary>
  /// Construct the event slug based on the event description, date, and id.
  /// </summary>
  public static string GetMTGOUrl(int id, string name, DateTime date)
  {
    string eventSlug = string.Join("-",
      name.ToLower().Replace(" ", "-").Replace("'", "").Replace(".", ""),
      date.ToString("yyyy-MM-dd") + id
    );

    return $"https://www.mtgo.com/decklist/{eventSlug}";
  }

  /// <summary>
  /// Retrieves the decklists for the given event id.
  /// </summary>
  public static async Task<JArray?> GetDecklists(int id, string name, DateTime date)
  {
    foreach (int offset in new[] { 0, -1, 1, 2 })
    {
      DateTime candidateDate = date.AddDays(offset);
      string url = GetMTGOUrl(id, name, candidateDate);
      
      try
      {
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
          Log.Trace("Failed to fetch MTGO decklist from {Url} (Status: {StatusCode})", url, response.StatusCode);
          continue;
        }

        // Verify we weren't redirected away from the decklist page.
        var finalUrl = response.RequestMessage?.RequestUri?.ToString();
        if (finalUrl != url && finalUrl != url + "/")
        {
          Log.Trace("Redirected from {Url} to {FinalUrl}. Skipping.", url, finalUrl);
          continue;
        }

        string html = await response.Content.ReadAsStringAsync();
        var result = ExtractDecklistDataFromHtml(html)?["decklists"] as JArray;
        if (result != null)
        {
          Log.Debug("Successfully extracted decklists from {Url}", url);
          return result;
        }
      }
      catch (Exception e)
      {
        Log.Trace("Error fetching {Url}: {Message}", url, e.Message);
        continue;
      }
    }

    return null;
  }

  public static async Task<JObject?> GetDecklistDataFromUrl(string url)
  {
    try
    {
      var response = await _httpClient.GetAsync(url);
      if (!response.IsSuccessStatusCode)
      {
        Log.Trace("Failed to fetch MTGO decklist data from {Url} (Status: {StatusCode})", url, response.StatusCode);
        return null;
      }

      var finalUrl = response.RequestMessage?.RequestUri?.ToString();
      if (finalUrl != url && finalUrl != url + "/")
      {
        Log.Trace("Redirected from {Url} to {FinalUrl}. Skipping.", url, finalUrl);
        return null;
      }

      string html = await response.Content.ReadAsStringAsync();
      return ExtractDecklistDataFromHtml(html);
    }
    catch (Exception e)
    {
      Log.Trace("Error fetching {Url}: {Message}", url, e.Message);
      return null;
    }
  }

  /// <summary>
  /// Extracts the JSON decklist data from the MTGO source HTML.
  /// </summary>
  public static JObject? ExtractDecklistDataFromHtml(string html)
  {
    const string marker = "window.MTGO.decklists.data";
    int markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
    if (markerIndex < 0) return null;

    int equalsIndex = html.IndexOf("=", markerIndex, StringComparison.Ordinal);
    if (equalsIndex < 0) return null;

    int index = equalsIndex + 1;
    while (index < html.Length && char.IsWhiteSpace(html[index]))
      index++;

    if (index >= html.Length || html[index] != '{') return null;

    int start = index;
    int depth = 0;
    bool inString = false;
    bool escaped = false;

    for (; index < html.Length; index++)
    {
      char current = html[index];

      if (inString)
      {
        if (escaped)
        {
          escaped = false;
        }
        else if (current == '\\')
        {
          escaped = true;
        }
        else if (current == '"')
        {
          inString = false;
        }
        continue;
      }

      if (current == '"')
      {
        inString = true;
        continue;
      }

      if (current == '{')
      {
        depth++;
      }
      else if (current == '}')
      {
        depth--;
        if (depth == 0)
        {
          index++;
          break;
        }
      }
    }

    if (depth != 0) return null;

    try
    {
      return JObject.Parse(html[start..index]);
    }
    catch
    {
      return null;
    }
  }
}
