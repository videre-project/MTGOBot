/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Threading;

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

  public static ICollection<PlayerEntry> FromEvent(Tournament tournament)
  {
    var players = new List<PlayerEntry>();
    foreach (var player in tournament.Players)
    {
      players.Add(new PlayerEntry(player));
      Thread.Sleep(250);
    }
    return players;
  }

  public override string ToString() =>
    string.Format(
      "({0}, '{1}')",
      this.Id, this.Name
    );
}
