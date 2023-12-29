/* @file
 * Copyright (c) 2023, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

import sql from './database/sql.js';
import { DeckComposite, EventFragment, Archetype } from './database/types.js';
import { GetArchetypes } from './mtggoldfish/archetypes.js';
import { UseOptimizedDefaults } from './puppeteer/stealth.js';
import { GetOffset } from './dates.js';


// Filter all deck entries for those without a corresponding archetype entry
// that came from events created within the last 3 weeks.
const minDate = GetOffset(new Date(), -21).toISOString().split('T')[0];
const decks: DeckComposite[] = await sql`
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

// Initialize the browser
const { browser, page } = await UseOptimizedDefaults({ headless: true });
page.setCacheEnabled(false);

for (const { id: eventId, name, date } of events) {
  try
  {
    // Filter for events that contain at least 4 unlabeled decks.
    const eventDecks = decks.filter((d) => d.event_id == eventId);
    if (eventDecks.length < 4) continue;
    console.log(`Updating archetypes for event ${name} #${eventId}`);

    // Build the archetype entries for the event from the deck entries.
    const archetypes = await GetArchetypes(page, eventId, eventDecks);

    // Insert the archetype entries into the Archetypes table.
    // - On conflict, update the entry's archetype and archetype_id fields.
    await sql`
      INSERT INTO Archetypes (id, deck_id, name, archetype, archetype_id)
      VALUES ${sql(archetypes.map((a: Archetype) => Object.values(a)))}
      ON CONFLICT (deck_id) DO UPDATE SET
        archetype = COALESCE(archetypes.archetype, EXCLUDED.archetype),
        archetype_id = COALESCE(archetypes.archetype_id, EXCLUDED.archetype_id)
    `;
  }
  catch (e: any)
  {
    console.error(`Failed to update archetypes for event ${eventId}: ${e.stack}`);
  }
}

// Cleanup the browser
await page.close();
await browser.close();

process.exit(0);
