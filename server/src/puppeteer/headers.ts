/* @file
 * Copyright (c) 2023, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

import { Page } from 'puppeteer-core';


/**
 * Listens for network requests through devtools matching against a callback.
 * @param page Puppeteer `Page` class for interacting with a browser tab.
 * @param url Input page url to capture network events from.
 * @param callback A synchronous or asynchronous function to filter events.
 * @returns A promise that resolves to the first matching network event.
 */
export async function GetApiCallHeaders(page: Page, url: string, callback: Function) {
  return new Promise(async (resolve, reject) => {
    let resolved = false;
    try {
      // Start a new devtools session listening for network requests.
      const devtools = await page.target().createCDPSession();
      await devtools.send('Network.enable');
      await devtools.send('Network.setRequestInterception', {
        patterns: [{ urlPattern: '*' }],
      });
      // Filter network requests intercepted by devtools.
      devtools.on('Network.requestIntercepted', async (event) => {
        if (resolved) return;
        if (callback(event)) {
          resolved = true;
          return resolve(event);
        }
        await devtools.send('Network.continueInterceptedRequest', {
          interceptionId: event.interceptionId,
        });
      });
      // Navigate to site to capture requests
      await page.goto(url, { waitUntil: 'domcontentloaded' },);
    } catch (error) {
      if (!resolved) {
        resolved = true;
        reject(error);
      }
    }
  });
};
