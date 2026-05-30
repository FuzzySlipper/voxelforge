/**
 * menu-command-dispatch.ts — Dispatch registry for menu command handlers.
 *
 * This registry is the single source of truth for which MenuChannel strings
 * have runtime handlers. The renderer registers handlers at startup; the
 * same registry keys are imported by tests to assert coverage of all items
 * in APP_MENU_MODEL.
 *
 * Usage in renderer (index.ts):
 *   import { menuCommandHandlers } from "../shared/menu-command-dispatch";
 *   menuCommandHandlers[MenuChannels.FILE_NEW] = () => { ... };
 *
 * Usage in tests:
 *   import { menuCommandHandlers } from "../src/shared/menu-command-dispatch";
 *   // assert every enabled model item's channel is a key
 */

import type { MenuChannel } from "./menu-channels";

/** Registry of menu command handlers, keyed by MenuChannel string. */
export const menuCommandHandlers: Record<string, () => void> = {};

/**
 * Return the set of channel strings that have registered handlers.
 * Used in tests to assert coverage without importing the full renderer.
 */
export function getHandledChannelSet(): Set<string> {
  return new Set(Object.keys(menuCommandHandlers));
}
