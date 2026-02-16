/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

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
        var result = ExtractDecklistsFromHtml(html);
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

  /// <summary>
  /// Extracts the JSON decklist data from the MTGO source HTML.
  /// </summary>
  private static JArray? ExtractDecklistsFromHtml(string html)
  {
    // The data is stored in `window.MTGO.decklists.data`.
    // We look for the script tag containing this data.
    string pattern = @"window\.MTGO\.decklists\.data\s*=\s*(\{.*?\});";
    var match = Regex.Match(html, pattern, RegexOptions.Singleline);
    
    if (match.Success)
    {
      try
      {
        var json = JObject.Parse(match.Groups[1].Value);
        var decklists = json["decklists"];
        return decklists as JArray;
      }
      catch
      {
        return null;
      }
    }

    return null;
  }
}
