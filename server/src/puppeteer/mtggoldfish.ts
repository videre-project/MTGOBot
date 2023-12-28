/* @file
 * Copyright (c) 2023, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

/**
 * Represents a list of MTGGoldfish events with their ids and names.
 */
export type IEventList = Array<[number, string]>;

/**
 * A mapping of player names to their archetypes.
 */
export interface IPlayerArchetypes {
  [player: string]: {
    id: number,
    name: string,
    archetype: string
  }
}

/**
 * Retrieves a list of events from the given MTGGoldfish tournament search url.
 * @param page The puppeteer page object
 * @param url The url to search for events
 * @returns A list of events matching the search criteria
 */
export async function GetEventsList(page: any, url: string): Promise<IEventList> {
  let i = 0;
  let eventPage: IEventList;
  let events: IEventList = [];
  do
  {
    await page.goto(`${url}&page=${++i}`, { waitUntil: 'domcontentloaded' });

    eventPage = await page.evaluate(() => {
      const table = document.querySelector('table[class="table table-striped"]');
      if (!table) return [];

      const rows = Array.from(table!.querySelectorAll('tr > td:nth-child(2)'));
      return rows.reduce((acc, row) => {
        const node = row.querySelector('a');
        const href = node?.getAttribute('href');
        if (!href) return acc;

        const id = parseInt(href.split('/').pop()!);
        const name = node!.textContent;
        acc.push([id, name]);

        return acc;
      }, []);
    });

    eventPage.forEach(event => events.push(event));
  }
  while (eventPage.length > 0);

  return events;
}

/**
 * Retrieves the MTGGoldfish url for the given event id.
 * @param page The puppeteer page object
 * @param id The event id
 * @returns The MTGGoldfish url for the event
 * @throws An error if the event is not found
 */
export async function GetEventUrl(page: any, id: number): Promise<string> {
  // Get the event name and date from the Daybreak API
  const res = await fetch(`https://census.daybreakgames.com/s:dgc/get/mtgo:v1/tournament_cover_page?event_id=${id}`);
  if (!res.ok) throw new Error(`Event ${id} not found`);
  const event = (await res.json())['tournament_cover_page_list'][0];
  const { description: name, starttime, site_name } = event;

  // Search a day before and after the event date
  const getOffset = (offset: number) => {
    const d = new Date(starttime);
    d.setDate(d.getDate() + offset);
    return new Intl.DateTimeFormat("en-US", {
      year: "numeric",
      month: "2-digit",
      day: "2-digit"
    }).format(d);
  };

  const url = [
    "https://www.mtggoldfish.com/tournament_searches/create?commit=Search",
    `tournament_search[name]=${name}`,
    `tournament_search[date_range]=${getOffset(-1)}+-+${getOffset(+1)}`,
  ].join('&');

  const events: IEventList = await GetEventsList(page, url);
  for (const [uid, _name] of events) {
    const url = `https://www.mtggoldfish.com/tournament/${uid}`;

    // Get the source url MTGGoldfish sourced the event from.
    await page.goto(url, { waitUntil: 'domcontentloaded' });
    const source = await page.evaluate(() =>
      document.querySelector('div > p:nth-child(6) > a')?.getAttribute('href')
    );

    if (source?.endsWith(`/decklist/${site_name}`)) return url;
  }

  throw new Error(`Event ${name} #${id} not found`);
}

/**
 * Retrieves the players and their archetypes from the given event id.
 * @param page The puppeteer page object
 * @param id The ID of the event to retrieve
 * @returns A list of players and their archetypes
 */
export async function GetEventArchetypes(page: any, id: number) : Promise<IPlayerArchetypes> {

  const url = await GetEventUrl(page, id);
  await page.goto(url, { waitUntil: 'domcontentloaded' });

  const archetypes = await page.evaluate(() => {
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

  // Enumerate over tournament standings pagination to collect player archetypes
  let i = 0;
  let playerPage: IPlayerArchetypes;
  let playerArchetypes: IPlayerArchetypes = {};
  do
  {
    await page.goto(url + `?page=${++i}`, { waitUntil: 'domcontentloaded' });
    playerPage = await page.evaluate((archetypes) => {
      const table = document.querySelector('table[class="table-tournament"]');
      if (!table) return {};

      const rows = Array.from(table!.querySelectorAll('tr:not([style="display: none;"])'));
      return rows.reduce((acc, row) => {
        const nodes = Array.from(row.querySelectorAll('td > a')).slice(0, 2);
        const [name, player] = nodes.map((node) => node.textContent);
        if (!name || !player) return acc;

        // Use a fallback archetype name if a custom name is used
        // e.g. 'Boros Burn' -> 'Burn'
        // e.g. 'Mono-Green Hardened Scales' -> 'Hardened Scales'
        const fallback = name.split(' ').slice(1).join(' ');

        // Attempt to determine the archetype id by it's given name
        // or from it's fallback name
        let id = archetypes[name];
        let archetype = id ? name : fallback;
        if (!id) id = archetypes[archetype];
        if (!id) archetype = undefined;

        acc[player] = { id, name, archetype };

        return acc;
      }, {});
    }, archetypes);

    playerArchetypes = { ...playerArchetypes, ...playerPage };
  }
  while (Object.keys(playerPage).length > 0);

  return playerArchetypes;
}
