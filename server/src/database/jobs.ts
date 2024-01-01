/* @file
 * Copyright (c) 2023, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

import sql from './sql.js';
import { DeckComposite, EventFragment, Archetype } from './types.js';
import { GetArchetypes } from '../mtggoldfish/archetypes.js';
import { GetEventUrl } from '../mtggoldfish/events.js';
import { GetOffset } from '../dates.js';


/**
 * Updates the Archetypes table with the latest archetype information.
 * @param page The puppeteer page object.
 * @param decks The deck entries to update. If null, all unlabeled decks from
 *  the last 3 weeks will be updated.
 * @returns True if all transactions were successful, false otherwise.
 */
export async function UpdateArchetypes(page: any, decks: DeckComposite[] = null)
{
  if (!decks?.length)
  {
    // Filter all deck entries for those without a corresponding archetype entry
    // that came from events created within the last 3 weeks.
    const minDate = GetOffset(new Date(), -21).toISOString().split('T')[0];
    decks = await sql`
      SELECT d.id, d.event_id, e.name as event_name, e.date as event_date, d.player
      FROM decks d
      JOIN events e ON d.event_id = e.id
      WHERE NOT EXISTS (
        SELECT 1
        FROM archetypes a
        WHERE d.id = a.deck_id
      )
      AND e.date >= ${minDate}
    `;
  }

  // Get the parent events from each unlabeled deck entry
  const events = new Set<EventFragment>();
  decks
    .filter((d, i, self) =>
        self.findIndex((t) => t.event_id === d.event_id) === i)
    .forEach((d) => {
      events.add({
        id: d.event_id,
        name: d.event_name,
        date: d.event_date,
      });
    });

  let transactionSuccess = true;
  for (const { id: eventId, name } of events) {
    // Filter for events that contain at least 4 unlabeled decks.
    const eventDecks = decks.filter((d) => d.event_id == eventId);
    if (eventDecks.length < 4) continue;
    console.log(`Updating archetypes for event ${name} #${eventId}`);

    // Try to fetch the event url based on the event metadata.
    const url = await GetEventUrl(page, eventId);
    if (!url) {
      transactionSuccess = false;
      console.log("--> Could not find event url. Skipping...");
      continue; // Skip this event if the url could not be found.
    }

    // Build the archetype entries for the event from the deck entries.
    const archetypes = await GetArchetypes(page, url, eventDecks);
    if (!archetypes.length) {
      transactionSuccess = false;
      console.log("--> Could not find archetypes. Skipping...");
      continue; // Skip this event if no archetypes could be found.
    }

    // Insert the archetype entries into the Archetypes table.
    // - On conflict, update the entry's archetype and archetype_id fields.
    await sql`
      INSERT INTO Archetypes (id, deck_id, name, archetype, archetype_id)
      VALUES ${sql(archetypes.map((a: Archetype) => Object.values(a)))}
      ON CONFLICT (deck_id) DO UPDATE SET
        archetype = COALESCE(archetypes.archetype, EXCLUDED.archetype),
        archetype_id = COALESCE(archetypes.archetype_id, EXCLUDED.archetype_id)
    `;
    console.log(`--> ${archetypes.length} / ${eventDecks.length} archetypes updated.`);
  }

  return transactionSuccess;
}
