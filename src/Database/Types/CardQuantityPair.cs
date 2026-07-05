/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

using MTGOSDK.API.Collection;
using static Database.Sql;


namespace Database.Types;

public struct CardQuantityPair
{
  public int    Id       { get; set; }
  public string Name     { get; set; }
  public int    Quantity { get; set; }

  public CardQuantityPair(int id, string name, int quantity)
  {
    if (quantity <= 0)
      throw new ArgumentException($"Card quantity must be positive for {name}.");

    this.Id = id;
    this.Name = name;
    this.Quantity = quantity;
  }

  public CardQuantityPair(JToken json)
  {
    if (!TryFromDecklistJson(json, out var card))
      throw new ArgumentException("Card JSON does not contain a valid decklist card.");

    this.Id = card.Id;
    this.Name = card.Name;
    this.Quantity = card.Quantity;
  }

  public static bool TryFromDecklistJson(JToken json, out CardQuantityPair card)
  {
    int quantity = json["qty"]?.ToObject<int?>() ?? 0;
    if (quantity <= 0)
    {
      card = default;
      return false;
    }

    var attributes = json["card_attributes"];
    int id = json["docid"]?.ToObject<int?>()
      ?? attributes?["digitalobjectcatalogid"]?.ToObject<int?>()
      ?? json["digitalobjectcatalogid"]?.ToObject<int?>()
      ?? -1;
    string? name = attributes?["card_name"]?.ToObject<string>()
      ?? json["card_name"]?.ToObject<string>()
      ?? GetCatalogName(id);

    if (string.IsNullOrWhiteSpace(name))
    {
      card = default;
      return false;
    }

    card = new CardQuantityPair(id, name.Trim(), quantity);
    return true;
  }

  private static string? GetCatalogName(int id)
  {
    if (id < 0)
      return null;

    try
    {
      return CollectionManager.GetCard(id).Name;
    }
    catch (KeyNotFoundException)
    {
      return null;
    }
  }

  public override string ToString() =>
    string.Format(
      "({0}, {1}, {2})",
      Id, Literal(Name), Quantity
    );
}
