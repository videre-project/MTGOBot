/* @file
 * Copyright (c) 2023, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

import postgres from 'postgres';


const { PGHOST, PGDATABASE, PGUSER, PGPASSWORD, PGPORT, PGSSL } = process.env;

export default postgres({
  host: PGHOST,
  database: PGDATABASE,
  username: PGUSER,
  password: PGPASSWORD,
  port: parseInt(PGPORT || '5432'),
  ssl: PGSSL === 'require' ? 'require' : false,
  // Type mapping options
  transform: {
    undefined: null
  }
});
