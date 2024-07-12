/* @file
 * Copyright (c) 2024, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

export function AsArray(entries: any[]) {
  return entries.map(e => `(${
    JSON.stringify(Object.values(e), null)
      // Remove the outer brackets from the JSON string.
      .slice(1, -1)
  })`)
}
