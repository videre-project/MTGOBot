/* @file
 * Copyright (c) 2023, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

import { DeckComposite, Archetype } from '../database/types.js';


/**
 * Represents a mapping of archetype names to their ids.
 */
export interface IArchetypeMapping {
  [key: string]: number;
}

/**
 * A mapping of player names to their archetypes.
 */
export interface IPlayerArchetypes {
  [player: string]: {
    id: number,
    name: string,
    archetype: string
    archetype_id: number
  }
}

/**
 * Retrieves the players and their archetypes from the given event url.
 * @param page The puppeteer page object
 * @param url The url of the event to retrieve
 * @returns A object mapping archetype names to their ids
 */
export async function GetEventArchetypes(page: any, url: string) : Promise<IArchetypeMapping> {
  await page.goto(url, { waitUntil: 'domcontentloaded' });

  const archetypes: IArchetypeMapping = await page.evaluate(() => {
    const table = document.querySelector('table[class="table table-striped table-sm"]');
    if (!table) return {};

    const rows = Array.from(table!.querySelectorAll('tr > td:first-child'));
    return rows.reduce((acc, row) => {
      const node = row.querySelector('a');
      const href = node?.getAttribute('href');
      if (!href) return acc;

      const id = parseInt(href.split('/').pop()!);
      const name = node!.textContent;
      acc[name] = id;

      return acc;
    }, {});
  });

  return archetypes
}

/**
 * Retrieves the players and their archetypes from the given event url.
 * @param page The puppeteer page object
 * @param url The url of the event to retrieve
 * @returns A list of players and their archetypes
 */
export async function GetPlayerArchetypes(page: any, url: string) : Promise<IPlayerArchetypes> {
  // Navigate to the event url and extract the archetype groups.
  const archetypes = await GetEventArchetypes(page, url);

  // Replace all text after the archor in the url
  url = page.url().replace(/#.*/, '');

  // Enumerate over tournament standings pagination to collect player archetypes
  let i = 0;
  let playerPage: IPlayerArchetypes;
  let playerArchetypes: IPlayerArchetypes = {};
  do
  {
    if (++i > 1) {
      await new Promise(resolve => setTimeout(resolve, 100));
      await page.goto(url + `?page=${i}`, { waitUntil: 'domcontentloaded' });
    }

    playerPage = await page.evaluate((archetypes: IArchetypeMapping) => {
      const table = document.querySelector('table[class="table-tournament"]');
      if (!table) return {};

      const rows = Array.from(table!.querySelectorAll('tr:not([style="display: none;"])'));
      return rows.reduce((acc, row) => {
        const nodes = Array.from(row.querySelectorAll('td > a'));
        const [name, player] = nodes.map((node) => node.textContent).slice(0, 2);
        if (!name || !player) return acc;

        // Extract the deck id from the 'expand_deck' uri
        const id_uri = nodes[2]?.getAttribute('href');
        const id = parseInt(id_uri?.match(/\((\d+)\)/)?.pop()!);

        // Use a fallback archetype name if a custom name is used
        // e.g. 'Boros Burn' -> 'Burn'
        // e.g. 'Mono-Green Hardened Scales' -> 'Hardened Scales'
        const fallback = name.split(' ').slice(1).join(' ') || null;

        // Attempt to determine the archetype id by it's given name
        let archetype_id = archetypes[name];
        let archetype = archetype_id ? name : fallback;
        if (!archetype_id) archetype_id = archetypes[archetype];

        acc[player] = { id, name, archetype, archetype_id };

        return acc;
      }, {});
    }, archetypes);

    // For any unmatched decks, get the archetype category from the deck page
    for (const [player, { id, archetype_id }] of Object.entries(playerPage)) {
      if (!id || archetype_id) continue;

      // Navigate to the deck page from the deck id
      const deck_url = `https://www.mtggoldfish.com/deck/${id}`;
      await new Promise(resolve => setTimeout(resolve, 100));
      await page.goto(deck_url, { waitUntil: 'domcontentloaded' });

      // Extract the archetype name from on the deck info's archetype uri
      const archetype = await page.evaluate(() => {
        const info = document.querySelector('p[class="deck-container-information"]');
        const archetypeLink = Array.from(info.querySelectorAll('a'))
          .find((link) => link.href.includes('/archetype/'));

        return archetypeLink?.textContent;
      });

      // Assign the archetype name and id (if found) to the player entry
      playerPage[player].archetype = archetype;
      playerPage[player].archetype_id = archetypes[archetype];
    }

    playerArchetypes = { ...playerArchetypes, ...playerPage };
  }
  while (Object.keys(playerPage).length > 0);

  return playerArchetypes;
}

/**
 * Builds a list of archetype entries from the given deck entries.
 * @param page The puppeteer page object
 * @param url The url of the event to retrieve
 * @param decks A list of unlabeled deck entries
 * @returns A list of archetype entries
 */
export async function GetArchetypes(
  page: any,
  url: string,
  decks: DeckComposite[]
): Promise<Archetype[]> {
  // Filter deck entries with a player key matching the archetype mapping
  const archetypeMap = await GetPlayerArchetypes(page, url);
  const archetypes: Archetype[] = [];
  decks
    .filter((d) => archetypeMap.hasOwnProperty(d.player))
    .forEach(({ id: deck_id, player }) => {
      const { id, name, archetype, archetype_id } = archetypeMap[player];
      archetypes.push({
        id,
        deck_id,
        name,
        archetype,
        archetype_id
      });
    });

  return archetypes;
}
