/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using Newtonsoft.Json.Linq;

using MTGOSDK.API.Collection;


namespace Database.Types;

public struct CardQuantityPair
{
  public int    Id       { get; set; }
  public string Name     { get; set; }
  public int    Quantity { get; set; }

  public CardQuantityPair(JToken json)
  {
    this.Id = json["docid"].ToObject<int>();
    this.Quantity = json["qty"].ToObject<int>();

    this.Name = CollectionManager.GetCard(this.Id).Name;
    if (string.IsNullOrEmpty(this.Name))
      throw new ArgumentException($"Card with ID {this.Id} has no name.");
  }

  public override string ToString() =>
    string.Format(
      "({0}, '{1}', {2})",
      Id, Name.Escape(), Quantity
    );
}
