/* @file
 * Copyright (c) 2023, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

/**
 * Returns a new date offset by the given number of days.
 * @param d The date to offset
 * @param offset The number of days to offset the date
 */
export function GetOffset(d: Date, offset: number): Date {
  return new Date(d.getTime() + offset * 24*60*60*1000)
  // d.setDate(d.getDate() + offset);
  // return d;
}

/**
 * Returns a new date formatted as "MM/DD/YYYY".
 * @param d The date to format
 */
export function GetIntl(d: Date): string {
  return new Intl.DateTimeFormat("en-US", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit"
  }).format(d);
}
