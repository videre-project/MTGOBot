/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using static Database.Sql;


namespace Database.Schemas;

public struct ArchetypeEntry
{
  public int    Id          { get; set; }
  public int    DeckId      { get; set; }
  public string Name        { get; set; }
  public string Archetype   { get; set; }
  public int?   ArchetypeId { get; set; }
  public string Provider    { get; set; }

  public ArchetypeEntry(
    int id,
    int deckId,
    string name,
    string archetype,
    int? archetypeId,
    string provider = "mtggoldfish")
  {
    this.Id          = id;
    this.DeckId      = deckId;
    this.Name        = name;
    this.Archetype   = archetype;
    this.ArchetypeId = archetypeId;
    this.Provider    = provider;
  }

  public override string ToString() =>
    string.Format(
      "({0}, {1}, {2}, {3}, {4}, {5})",
      Id,
      DeckId,
      Literal(Name),
      Literal(Archetype),
      Literal(ArchetypeId),
      Literal(Provider)
    );
}
