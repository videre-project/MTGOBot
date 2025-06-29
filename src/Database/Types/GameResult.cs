/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Linq;

using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.API.Users;


namespace Database.Types;

public struct GameResult
{
  public int        Id     { get; set; }
  public ResultType Result { get; set; }

  public GameResult(GameStandingRecord game, User player)
  {
    this.Id     = game.Id;
    this.Result = GetResult(game, player);
  }

  private static ResultType GetResult(GameStandingRecord game, User player) =>
    game.WinnerIds.Contains(player.Id)
      ? ResultType.win
      : game.WinnerIds.Count() > 0
        ? ResultType.loss
        : ResultType.draw;

  public override string ToString() =>
    string.Format(
      "({0}, '{1}')",
      Id, Result
    );
}
