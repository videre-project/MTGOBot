/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

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
    this.Id   = player.Id;
    this.Name = player.Name;
  }

  public static ICollection<PlayerEntry> FromEvent(
    Tournament tournament,
    ICollection<PlayerEntry>? existing = null)
  {
    ICollection<PlayerEntry> players = existing ?? [];
    foreach (var player in tournament.Players)
    {
      // Skip any duplicate players
      if (players.Any(p => p.Id != -1 && p.Id == player.Id))
        continue;

      players.Add(new PlayerEntry(player));
    }
    return players;
  }

  public override string ToString() =>
    string.Format(
      "({0}, '{1}')",
      this.Id, this.Name
    );
}
