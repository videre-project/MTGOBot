/* @file
 * Copyright (c) 2024, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

import { GetOffset, GetIntl } from '../dates.js';

/**
 * Represents a list of MTGO decklists.
 */
export type IDeckEntryList = Array<IDeckEntryList>;

/**
 * Represents a decklist object.
 */
export interface IDeckEntry {
  decktournamentid: `${number}`;
  loginid: `${number}`;
  main_deck: Array<ICardEntry>;
  player: string;
  sideboard_deck: Array<ICardEntry>;
  tournamentid: `${number}`;
}

/**
 * Represents the card metadata and attributes in a decklist.
 */
export interface ICardEntry {
  card_attributes: {
    card_name: string;
  }
  docid: `${number}`;
  qty: `${number}`;
}

declare global {
  interface Window {
    MTGO: { decklists: { data: { decklists: IDeckEntryList; }; }; };
  }
}

/**
 * Retrieves the decklists for the given event id.
 * @param page The puppeteer page object
 * @param id The id of the event
 * @param name The name of the event
 * @param date The date of the event
 * @returns The decklists for the event
 */
export function GetMTGOUrl(id: number, name: string, date: Date): string {
  const dateArray = GetIntl(date).split('/');
  // Construct the event slug based on the event description, date, and id.
  const event_slug = [
    name.toLowerCase().replace(/\s/g, '-'),
    dateArray[2], dateArray[0], dateArray[1] + id
  ].join('-');

  return `https://www.mtgo.com/decklist/${event_slug}`;
}

/**
 * Retrieves the decklists for the given event id.
 * @param page The puppeteer page object
 * @param id The id of the event
 * @param name The name of the event
 * @param date The date of the event
 * @returns The decklists for the event
 */
export async function GetDecklists(
  page: any,
  id: number,
  name: string,
  date: Date
): Promise<Array<IDeckEntry>> {
  // As the date provided may be relative to the timezone of the user, it may be
  // necessary to consider the previous date to consider for any timezone offset
  // from the server's timezone (UTC).
  for (const offset of [0, -1, +1, +2]) {
    const candidateDate = GetOffset(date, offset);
    const url = GetMTGOUrl(id, name, candidateDate);
    await page.goto(url, { waitUntil: 'domcontentloaded' });

    // Check if the page exists or is redirected to the main site
    const currentUrl = page.url();
    if (currentUrl !== url) continue;

    // Return the `window.MTGO.decklists.data.decklists` object.
    const decklists = await page.evaluate(() => {
      return window.MTGO.decklists.data.decklists;
    });

    return decklists;
  }

  return [];
}
