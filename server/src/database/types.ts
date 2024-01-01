/* @file
 * Copyright (c) 2023, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

/**
 * Represents a deck entry and its associated event metadata.
 */
export type DeckComposite = {
  id: number;
  event_id: number;
  event_name: string;
  event_date: Date;
  player: string;
};

/**
 * Represents basic event metadata derived from a deck entry.
 */
export type EventFragment = {
  id: number;
  name: string;
  date: Date;
};

/**
 * Represents a deck's archetype information.
 */
export type Archetype = {
  id: number;
  deck_id: number;
  name: string;
  archetype: string;
  archetype_id: number;
};
