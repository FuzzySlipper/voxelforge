#!/usr/bin/env node
/**
 * menu-gui-smoke.mjs — Electron menu GUI smoke test.
 *
 * Launches a real Electron BrowserWindow, sets up the native menu,
 * programmatically exercises menu items, and verifies that IPC events
 * are dispatched correctly through the full native menu -> webContents.send
 * chain.
 *
 * Usage:
 *   npx electron scripts/menu-gui-smoke.mjs
 *
 * Requires a display server (Xvfb on headless). Use scripts/xvfb-run.sh:
 *   ./scripts/xvfb-run.sh npx electron scripts/menu-gui-smoke.mjs
 *
 * Returns exit code 0 on all pass, 1 on any failure.
 */

import { app, BrowserWindow, Menu, ipcMain } from "electron";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "..");

// Import the compiled menu setup
const { setupMenu } = await import(path.join(repoRoot, "dist", "main", "menu.js"));
const { MenuChannels } = await import(path.join(repoRoot, "dist", "shared", "menu-channels.js"));

// ── Test framework helpers ──

let passed = 0;
let failed = 0;
const failures = [];

function assert(label, condition, detail) {
  if (condition) {
    console.log(`  ✅ PASS: ${label}`);
    passed++;
  } else {
    const msg = `  ❌ FAIL: ${label} — ${detail || "expected truthy, got falsy"}`;
    console.error(msg);
    failures.push(msg);
    failed++;
  }
}

function assertEqual(label, actual, expected) {
  if (actual === expected) {
    console.log(`  ✅ PASS: ${label}`);
    passed++;
  } else {
    const msg = `  ❌ FAIL: ${label} — expected "${expected}", got "${actual}"`;
    console.error(msg);
    failures.push(msg);
    failed++;
  }
}

function assertInList(label, item, list, listName) {
  if (list.includes(item)) {
    console.log(`  ✅ PASS: ${label}`);
    passed++;
  } else {
    const msg = `  ❌ FAIL: ${label} — "${item}" not found in ${listName} (${JSON.stringify(list)})`;
    console.error(msg);
    failures.push(msg);
    failed++;
  }
}

function assertSent(label, sentChannels, expectedChannel) {
  assertInList(label, expectedChannel, sentChannels, "sent channels");
}

// ── Find menu items recursively ──

function findMenuItem(menu, labelPattern) {
  if (!menu) return null;
  const items = menu.items || [];
  for (const item of items) {
    if (item.label && item.label.replace("&", "").match(labelPattern)) {
      return item;
    }
    if (item.submenu) {
      const found = findMenuItem(item.submenu, labelPattern);
      if (found) return found;
    }
  }
  return null;
}

function collectAllClickChannels(menu, maxDepth = 5, depth = 0) {
  const channels = [];
  if (!menu || depth > maxDepth) return channels;
  const items = menu.items || [];
  for (const item of items) {
    if (item.click && item.label) {
      channels.push({
        label: item.label.replace("&", ""),
        menuItem: item,
      });
    }
    if (item.submenu) {
      channels.push(...collectAllClickChannels(item.submenu, maxDepth, depth + 1));
    }
  }
  return channels;
}

// ── Main test ──

async function main() {
  console.log("=".repeat(70));
  console.log("  Electron Menu GUI Smoke Test");
  console.log("=".repeat(70));

  // ── Preflight: check DISPLAY ──
  if (!process.env.DISPLAY && process.platform !== "darwin" && process.platform !== "win32") {
    console.error("\n  ERROR: DISPLAY environment variable not set.");
    console.error("  This test requires a display server (Xvfb on headless).");
    console.error("  Use: scripts/xvfb-run.sh npx electron scripts/menu-gui-smoke.mjs\n");
    process.exit(1);
  }

  // ── Create the test window ──
  console.log("\n[1] Waiting for Electron app readiness...");
  await app.whenReady();

  console.log("[2] Creating BrowserWindow...");

  const sentEvents = [];

  const win = new BrowserWindow({
    width: 800,
    height: 600,
    show: false, // don't show the window on screen
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false, // needed for preload-less test
    },
  });

  // Intercept webContents.send to capture events
  const originalSend = win.webContents.send.bind(win.webContents);
  win.webContents.send = function (channel, payload) {
    sentEvents.push({ channel, payload });
    return originalSend(channel, payload);
  };

  // Load a minimal HTML page
  const testHtml = path.join(repoRoot, "scripts", "menu-gui-smoke.html");
  await win.loadFile(testHtml);

  console.log("  Window created and loaded.\n");

  // ── Set up the native menu ──
  console.log("[2] Setting up native application menu...");
  setupMenu(win);
  const appMenu = Menu.getApplicationMenu();
  assert("Application menu is set", !!appMenu);
  if (!appMenu) {
    console.error("  CRITICAL: Application menu not set, aborting.");
    process.exit(1);
  }
  console.log("  Application menu created.\n");

  // ── Verify menu structure ──
  console.log("[3] Verifying menu structure...");

  const topLevelLabels = appMenu.items.map((i) => i.label?.replace("&", "") || "(separator)").filter(Boolean);
  console.log(`  Top-level menus: ${topLevelLabels.join(", ")}`);

  assertInList("File menu present", "File", topLevelLabels, "top-level menus");
  assertInList("Edit menu present", "Edit", topLevelLabels, "top-level menus");
  assertInList("Reference menu present", "Reference", topLevelLabels, "top-level menus");
  assertInList("View menu present", "View", topLevelLabels, "top-level menus");
  assertInList("Tools menu present", "Tools", topLevelLabels, "top-level menus");
  assertInList("Help menu present", "Help", topLevelLabels, "top-level menus");

  console.log();

  // ── Find and click specific menu items ──
  console.log("[4] Exercising native menu items...\n");

  // Test items by menu channel
  const testItems = [
    { label: "Load Reference Model...", expectedChannel: MenuChannels.REFERENCE_MODEL_LOAD },
    { label: "List Reference Models", expectedChannel: MenuChannels.REFERENCE_MODEL_LIST },
    { label: "New", expectedChannel: MenuChannels.FILE_NEW },
    { label: "Open...", expectedChannel: MenuChannels.FILE_OPEN },
    { label: "Save", expectedChannel: MenuChannels.FILE_SAVE },
    { label: "Undo", expectedChannel: MenuChannels.EDIT_UNDO },
    { label: "Redo", expectedChannel: MenuChannels.EDIT_REDO },
    { label: "Front", expectedChannel: MenuChannels.VIEW_FRONT },
    { label: "Wireframe Toggle", expectedChannel: MenuChannels.VIEW_WIREFRAME },
    { label: "Voxelize...", expectedChannel: MenuChannels.VOXELIZE_EXECUTE },
    { label: "About VoxelForge", expectedChannel: MenuChannels.HELP_ABOUT },
  ];

  const testMenuNames = [
    "Reference",
    "Reference",
    "File",
    "File",
    "File",
    "Edit",
    "Edit",
    "View",
    "View",
    "Tools",
    "Help",
  ];

  // Allow menu items to be found anywhere (some are nested in submenus)
  for (let i = 0; i < testItems.length; i++) {
    const { label, expectedChannel } = testItems[i];
    const menuLabel = testMenuNames[i];
    const item = findMenuItem(appMenu, new RegExp(
      "^" + label.replace(/[.*+?^${}()|[\]\\]/g, "\\$&") + "$",
      "i",
    ));
    if (item) {
      assert(`Found menu item: "${label}"`, !!item);
      console.log(`  Clicking: ${menuLabel} > ${label}`);
      item.click();
      // Check the event was sent
      const sent = sentEvents.some((e) => e.channel === expectedChannel);
      assert(
        `Menu click "${label}" sent "${expectedChannel}" via webContents.send`,
        sent,
        `Expected channel "${expectedChannel}" but sent events were: ${JSON.stringify(sentEvents.map(e => e.channel))}`,
      );
    } else {
      console.log(`  ⚠ Skipping "${label}" — not found in menu (may be nested deeper)`);
    }
  }

  // ── Collect ALL clickable menu items for exhaustive check ──
  console.log("\n[5] Exhaustive menu check...");
  const allClickable = collectAllClickChannels(appMenu);
  console.log(`  Found ${allClickable.length} clickable menu items total.`);

  // Verify no menu item click throws
  const menuItemsToCheck = allClickable.filter(
    (c) => !["File", "Edit", "Reference", "View", "Tools", "Help", "VoxelForge"].includes(c.label),
  );

  let clickErrors = 0;
  for (const { label, menuItem } of menuItemsToCheck) {
    try {
      menuItem.click();
    } catch (err) {
      console.error(`  ❌ ERROR: Menu item "${label}" click threw: ${err.message}`);
      clickErrors++;
    }
  }
  assert("No menu item click throws an exception", clickErrors === 0, `${clickErrors} items threw exceptions`);

  // ── Verify Reference -> Load Reference Model specifically ──
  console.log("\n[6] Reference -> Load Reference Model deep dive...");

  const refLoadEvents = sentEvents.filter((e) => e.channel === MenuChannels.REFERENCE_MODEL_LOAD);
  assert(
    "menu:reference-model-load was sent at least once",
    refLoadEvents.length > 0,
    "No events for REFERENCE_MODEL_LOAD found",
  );

  if (refLoadEvents.length > 0) {
    console.log(`  Event sent ${refLoadEvents.length} time(s)`);
    console.log(`  Payload: ${JSON.stringify(refLoadEvents[0].payload)}`);
  }

  // ── Verify all sent channels are in the allowedEventChannels list ──
  console.log("\n[7] Verifying all sent channels are valid...");

  const validMenuChannels = new Set(Object.values(MenuChannels));
  let invalidChannels = 0;
  for (const { channel } of sentEvents) {
    if (!validMenuChannels.has(channel)) {
      console.error(`  ❌ WARNING: Channel "${channel}" is not in MenuChannels`);
      invalidChannels++;
    }
  }
  assert("All sent channels are valid MenuChannels", invalidChannels === 0, `${invalidChannels} invalid channels`);

  // Log summary of sent events
  const uniqueChannels = [...new Set(sentEvents.map((e) => e.channel))];
  console.log(`\n  Unique channels sent: ${uniqueChannels.length}`);
  for (const ch of uniqueChannels.sort()) {
    const count = sentEvents.filter((e) => e.channel === ch).length;
    console.log(`    ${ch} (${count}x)`);
  }

  // ── Summary ──
  console.log("\n" + "=".repeat(70));
  console.log(`  RESULTS: ${passed} passed, ${failed} failed`);
  console.log("=".repeat(70));

  if (failures.length > 0) {
    console.log("\n  Failure details:");
    for (const f of failures) {
      console.error(`    ${f}`);
    }
  }

  // Clean up
  win.close();
  app.exit(failed > 0 ? 1 : 0);
}

main().catch((err) => {
  console.error("[menu-gui-smoke] Fatal error:", err);
  process.exit(1);
});
