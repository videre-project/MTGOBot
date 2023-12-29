/* @file
 * Copyright (c) 2023, Cory Bennett. All rights reserved.
 * SPDX-License-Identifier: Apache-2.0
*/

export type DeckComposite = {
  id: number;
  event_id: number;
  event_name: string;
  event_date: Date;
  player: string;
};

export type EventFragment = {
  id: number;
  name: string;
  date: Date;
};

export type Archetype = {
  id: number;
  deck_id: number;
  name: string;
  archetype: string;
  archetype_id: number;
};
