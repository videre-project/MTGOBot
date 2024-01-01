/* @file
 * Copyright (c) 2023, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

import express from 'express';

import { UpdateArchetypes } from './database/jobs.js';
import { UseOptimizedDefaults } from './puppeteer/stealth.js';


const app = express();

app.post('/events/update-archetypes', async (req, res) => {
  // Initialize the browser
  const { browser, page } = await UseOptimizedDefaults({ headless: true });
  page.setCacheEnabled(false);

  const result = await UpdateArchetypes(page);
  res.sendStatus(result ? 200 : 500);

  // Close the browser
  await page.close();
  await browser.close();
});

const port = 3000;
app.listen(port, () => {
  console.clear();
  console.log(`Listening on port ${port}`);
});
