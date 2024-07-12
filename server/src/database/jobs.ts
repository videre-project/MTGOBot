/* @file
 * Copyright (c) 2024, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

import { AsArray } from './helpers.js';
import sql from './sql.js';
import type {
  DeckComposite,
  EventFragment,
  Archetype,
  Deck,
  CardQuantityPair,
} from './types.js';
import { GetArchetypes } from '../mtggoldfish/archetypes.js';
import { GetEventUrl } from '../mtggoldfish/events.js';
import { ICardEntry, GetDecklists } from '../mtgo/decklists.js';
import { GetOffset } from '../dates.js';


function parseDecklistField(d: Array<ICardEntry>): CardQuantityPair[] {
  return d.map((c) => ({
    id: parseInt(c.docid),
    name: c.card_attributes.card_name,
    quantity: parseInt(c.qty),
  }));
}

/**
 * Updates the Decks table with the latest event decklists.
 * @param page The puppeteer page object.
 * @param events The event metadata to update decks for. If null, all events
 *   from the last 3 weeks will be updated.
 * @returns True if all transactions were successful, false otherwise.
 */
export async function UpdateDecks(page: any, events: EventFragment[] = null) {
  if (!events?.length) {
    // Filter all events for those that contain standings entries without a
    // corresponding deck entry from events created within the last 3 weeks.
    const minDate = GetOffset(new Date(), -21).toISOString().split('T')[0];
    events = await sql`
      SELECT e.id, e.name, e.date
      FROM events e
      WHERE EXISTS (
        SELECT 1
        FROM standings s
        WHERE e.id = s.event_id
      )
      AND NOT EXISTS (
        SELECT 1
        FROM decks d
        WHERE e.id = d.event_id
      )
      AND e.date >= ${minDate}
      AND e.kind != 'Preliminary' -- FIXME: Prelim events are not yet public.
    `;
  }

  let transactionSuccess = true;
  for (const { id: eventId, name, date } of events) {
    console.log(`Updating decks for event ${name} #${eventId}`);
    const decklists = await GetDecklists(page, eventId, name, date);
    if (!decklists.length) {
      transactionSuccess = false;
      console.log("--> Could not find decklists. Skipping...");
      continue; // Skip this event if no decklists could be found.
    }

    const decks = decklists.map((d) => ({
      id: parseInt(d.decktournamentid),
      event_id: eventId,
      player: d.player,
      mainboard: parseDecklistField(d.main_deck),
      sideboard: parseDecklistField(d.sideboard_deck),
    } as Deck));

    // Insert the deck entries into the Decks table.
    await sql`
      INSERT into Decks ${sql(decks.map(({ mainboard, sideboard, ...e}) => ({
        ...e,
        mainboard: AsArray(mainboard),
        sideboard: AsArray(sideboard),
      })))}
      ON CONFLICT DO NOTHING
    `;

    console.log(`--> ${decks.length} decks updated.`);
  }

  return transactionSuccess;
}

/**
 * Updates the Archetypes table with the latest archetype information.
 * @param page The puppeteer page object.
 * @param decks The deck entries to update. If null, all unlabeled decks from
 *   the last 3 weeks will be updated.
 * @returns True if all transactions were successful, false otherwise.
 */
export async function UpdateArchetypes(page: any, decks: DeckComposite[] = null) {
  if (!decks?.length) {
    // Filter all deck entries for those without a corresponding archetype entry
    // that came from events created within the last 3 weeks.
    const minDate = GetOffset(new Date(), -7).toISOString().split('T')[0];
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
  for (const { id: eventId, name, date } of events) {
    // Filter for events that contain at least 4 unlabeled decks.
    const eventDecks = decks.filter((d) => d.event_id == eventId);
    if (eventDecks.length < 4) continue;
    console.log(`Updating archetypes for event ${name} #${eventId}`);

    // Try to fetch the event url based on the event metadata.
    const url = await GetEventUrl(page, eventId, name, date);
    if (!url) {
      transactionSuccess = false;
      console.log("--> Could not find event url. Skipping...");
      continue; // Skip this event if the url could not be found.
    }
    console.log(`--> Found event url: ${url}`);

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
