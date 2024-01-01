/* @file
 * Copyright (c) 2023, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

import postgres from 'postgres';


const { PGHOST, PGDATABASE, PGUSER, PGPASSWORD } = process.env;

export default postgres({
  host: PGHOST,
  database: PGDATABASE,
  username: PGUSER,
  password: PGPASSWORD,
  port: 5432,
  ssl: "require",
  // Type mapping options
  transform: {
    undefined: null
  }
});
