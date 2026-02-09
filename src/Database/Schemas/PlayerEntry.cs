/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.API.Users;


namespace Database.Schemas;

public struct PlayerEntry
{
  public int    Id    { get; set; }
  public string Name  { get; set; }

  public PlayerEntry(User player)
  {
    // If the player ID is -1 (anonymous/private profile), generate a deterministic
    // negative ID based on their name to preserve identity within the database.
    this.Id   = (player.Id == -1) 
        ? -Math.Abs(player.Name.GetHashCode()) 
        : player.Id;
    this.Name = player.Name;
  }

  public static ICollection<PlayerEntry> FromEvent(
    Tournament tournament,
    ICollection<PlayerEntry>? existing = null)
  {
    ICollection<PlayerEntry> players = existing ?? [];
    
    // Use a hash set of (Id, Name) to ensure uniqueness in this collection
    var uniquePlayers = new HashSet<(int, string)>(
        players.Select(p => (p.Id, p.Name))
    );

    void AddUnique(User user)
    {
      var entry = new PlayerEntry(user);
      if (uniquePlayers.Add((entry.Id, entry.Name)))
      {
        players.Add(entry);
      }
    }

    foreach (var player in tournament.Players)
    {
      AddUnique(player);
    }

    foreach (var standing in tournament.Standings)
    {
      AddUnique(standing.Player);
    }

    return players;
  }

  public override string ToString() =>
    string.Format(
      "({0}, '{1}')",
      this.Id, this.Name
    );
}
