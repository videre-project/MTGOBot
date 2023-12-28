/* @file
 * Copyright (c) 2023, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

import express from 'express';
import { GetEventArchetypes } from "./puppeteer/mtggoldfish.js";
import { UseOptimizedDefaults } from "./puppeteer/stealth.js";


const app = express();

const { browser, page } = await UseOptimizedDefaults({ headless: true });

app.get('/archetypes', async (req, res) => {
  const id = parseInt(req.query.event_id as string);
  const url = await GetEventArchetypes(page, id);
  res.send(url);
});

const port = 3000;
app.listen(port, () => {
  console.log(`Listening on port ${port}`);
});
