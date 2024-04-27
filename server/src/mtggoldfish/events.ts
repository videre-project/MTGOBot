/* @file
 * Copyright (c) 2023, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

import { GetOffset, GetIntl } from '../dates.js';

/**
 * Represents a list of MTGGoldfish events with their ids and names.
 */
export type IEventList = Array<[number, string]>;

/**
 * Retrieves a list of events from the given MTGGoldfish tournament search url.
 * @param page The puppeteer page object
 * @param url The url to search for events
 * @returns A list of events matching the search criteria
 */
export async function GetEvents(
  page: any,
  name: string = null,
  startDate: Date,
  endDate: Date
): Promise<IEventList> {
  // Construct the tournament search url
  const url = [
    "https://www.mtggoldfish.com/tournament_searches/create?commit=Search",
    // `tournament_search[date_range]=${startDate.Intl()}+-+${endDate.Intl()}`,
    `tournament_search[date_range]=${GetIntl(startDate)}+-+${GetIntl(endDate)}`,
    name && `tournament_search[name]=${name}`,
  ].filter(Boolean).join('&');

  // Enumerate over all tournament search result pages
  let i = 0;
  let eventPage: IEventList;
  let events: IEventList = [];
  do
  {
    await page.waitForTimeout(100);
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
 * Filters the given list of events by the given event id.
 * @param page The puppeteer page object
 * @param source The source MTGO url for the event
 * @param events The list of events to search
 * @returns The MTGGoldfish url for the event
 */
export async function FilterEventList(
  page: any,
  source: string,
  events: IEventList,
) : Promise<string | null> {
  // Match each tournament page by it's source MTGO url.
  for (const [uid, _name] of events) {
    const url = `https://www.mtggoldfish.com/tournament/${uid}`;

    // Get the source url MTGGoldfish sourced the event from.
    await page.waitForTimeout(100);
    await page.goto(url, { waitUntil: 'domcontentloaded' });

    const match = await page.evaluate(() =>
      // @ts-ignore - This is valid access to the anchor element's properties.
      document?.querySelector(`div > p > a[href*="mtgo.com/decklist/"]`)?.href
    );

    if (match == source) return url;
  }

  return null;
}

/**
 * Retrieves the MTGGoldfish url for the given event id.
 * @param page The puppeteer page object
 * @param id The event id
 * @returns The MTGGoldfish url for the event
 * @throws An error if the event is not found
 */
export async function GetEventUrl(page: any, id: number): Promise<string | null> {
  // Get the event name and date from the Daybreak API
  const res = await fetch(`https://census.daybreakgames.com/s:dgc/get/mtgo:v1/tournament_cover_page?event_id=${id}`);
  if (!res.ok)
    throw new Error(`Event ${id} not found`);

  const event = (await res.json())['tournament_cover_page_list'][0];
  const {
    description: name,
    starttime: date,
    site_name: slug
  } = event;

  // Search a day before and after the event date
  const startDate = GetOffset(new Date(date), -1);
  const endDate = GetOffset(new Date(date), +1);
  const events = await GetEvents(page, name, startDate, endDate);

  // Match each tournament page by it's source MTGO url.
  const source = `https://www.mtgo.com/decklist/${slug}`;
  const match = await FilterEventList(page, source, events);

  return match;
}
