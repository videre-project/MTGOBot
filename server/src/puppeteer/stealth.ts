/* @file
 * Copyright (c) 2023, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

import puppeteer from 'puppeteer-extra';
import AdblockerPlugin from 'puppeteer-extra-plugin-adblocker';
import StealthPlugin from 'puppeteer-extra-plugin-stealth';

/*
 * Launches a headless browser instance with optimized defaults.
 */
export async function UseOptimizedDefaults(
  abort = ['image', 'font', 'stylesheet'],
  args: string[] = ['--no-sandbox'],
  headless: boolean | 'shell' = true,
) {
  puppeteer.use(AdblockerPlugin({ blockTrackers: true }));
  puppeteer.use(StealthPlugin());

  const browser = await puppeteer.launch({
    headless,
    args,
    // Disable timeout for all puppeteer operations
    protocolTimeout: 0,
    timeout: 0,
    // Ensure Chrome exits when the parent process exits
    handleSIGINT: false,
    handleSIGTERM: false,
    handleSIGHUP: false,
  });

  const page = await browser.pages().then((pages) => pages[0]);
  page.setDefaultNavigationTimeout(0);

  await page.setRequestInterception(true);
  page.on('request', (request) =>
    abort.includes(request.resourceType())
      ? request.abort()
      : request.continue()
  );

  return { browser, page };
};
