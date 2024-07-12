/* @file
 * Copyright (c) 2023, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

import express from 'express';

import { UpdateDecks, UpdateArchetypes } from './database/jobs.js';
import { GetPlayerArchetypes } from './mtggoldfish/archetypes.js';
import { GetDecklists } from './mtgo/decklists.js';
import { UseOptimizedDefaults } from './puppeteer/stealth.js';


const app = express();

// Initialize the browser
const { browser, page } = await UseOptimizedDefaults();
page.setCacheEnabled(false);

// Fetch the event page to extract the `window.MTGO.decklists.data` object.
app.get('/census/decklists', async (req, res) => {
  const id = req.query.id as string;
  const name = req.query.name as string;
  const date = new Date(req.query.date as string);
  if (!id || !name || !date) {
    console.log('Invalid query parameters');
    res.sendStatus(400);
    return;
  }

  const decklists = await GetDecklists(page, parseInt(id), name, date);
  if (!decklists.length) {
    console.log(`Could not find decklists for event ${name} #${id}`);
    res.sendStatus(500);
    return;
  }

  res.json({ tournament_decklist_by_id_list: decklists });

  // navigate to blank page
  await page.goto('about:blank');
});

app.post('/events/update-decks', async (req, res) => {
  try
  {
    const result = await UpdateDecks(page);
    res.sendStatus(result ? 200 : 500);
  }
  catch (e)
  {
    console.error(e);
    res.sendStatus(500);
  }

  // navigate to blank page
  await page.goto('about:blank');
});

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
