/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace Database.Schemas;

public struct ArchetypeEntry
{
  public int    Id          { get; set; }
  public int    DeckId      { get; set; }
  public string Name        { get; set; }
  public string Archetype   { get; set; }
  public int?   ArchetypeId { get; set; }

  public ArchetypeEntry(int id, int deckId, string name, string archetype, int? archetypeId)
  {
    this.Id          = id;
    this.DeckId      = deckId;
    this.Name        = name;
    this.Archetype   = archetype;
    this.ArchetypeId = archetypeId;
  }

  public override string ToString() =>
    string.Format(
      "({0}, {1}, '{2}', '{3}', {4})",
      Id, DeckId, Name, Archetype.Replace("'", "''"), ArchetypeId.HasValue ? ArchetypeId.Value.ToString() : "NULL"
    );
}
