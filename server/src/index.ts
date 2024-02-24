/* @file
 * Copyright (c) 2023, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

import express from 'express';

import { UpdateArchetypes } from './database/jobs.js';
import { GetPlayerArchetypes } from './mtggoldfish/archetypes.js';
import { UseOptimizedDefaults } from './puppeteer/stealth.js';


const app = express();

// Initialize the browser
const { browser, page } = await UseOptimizedDefaults();
page.setCacheEnabled(false);

app.get('/events/get-archetypes', async (req, res) => {
  const url = req.query.url as string;
  if (!url) {
    res.sendStatus(400);
    return;
  }

  var archetypes = await GetPlayerArchetypes(page, url);
  res.json(archetypes);

  // navigate to blank page
  await page.goto('about:blank');
});

app.post('/events/update-archetypes', async (req, res) => {
  try
  {
    const result = await UpdateArchetypes(page);
    res.sendStatus(result ? 200 : 500);
  }
  catch
  {
    res.sendStatus(500);
  }

  // navigate to blank page
  await page.goto('about:blank');
});

const port = 3000;
const server = app.listen(port, () => {
  console.clear();
  console.log(`Listening on port ${port}`);
});

// // Error handler
// app.use((err, req, res, next) => {
//   console.error(err.stack);
//   next(err);
// });

process.on('SIGTERM', () => {
  server.close(async () => {
    // Close the browser
    await page.close();
    await browser.close();
  });
})
