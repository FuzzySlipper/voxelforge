#!/usr/bin/env node
/**
 * run-accessible-gui-smoke.mjs — Playwright Electron accessibility/GUI harness
 *
 * Launches the VoxelForge Electron app, gets the BrowserWindow/page, captures
 * screenshots, accessibility/DOM/locator inventory, console/main-process logs,
 * and writes a compact JSON report under a durable artifact directory.
 *
 * Usage:
 *   node scripts/run-accessible-gui-smoke.mjs
 *   npm run gui:llm-smoke
 *
 * Environment:
 *   ARTIFACT_DIR    — output directory (default: ../artifacts/accessible-gui-smoke/<timestamp>)
 *   DISPLAY         — X display for headless execution (Xvfb handled automatically)
 *   ELECTRON_EXTRA  — extra Electron CLI args
 *
 * Prerequisites:
 *   npm run build   (to compile TypeScript first)
 *   playwright      (npm install)
 *
 * This harness uses Playwright's Electron integration which:
 *   - Launches the full Electron app as a child process
 *   - Connects to the first BrowserWindow
 *   - Provides Playwright's Locator API, accessibility snapshot, and screenshot
 *   - Captures console and main-process logs
 *
 * Tested workflows:
 *   1. App launches to main UI with accessible menubar
 *   2. Reference > Load Reference Model prompt/dialog is visible and accessible
 *   3. Model/path entry can be driven through labels/roles
 *   4. Accessible status/result feedback is screenshot-captured
 *
 * Report format: JSON with fields:
 *   - app_launch: { ok, url, title, process_log }
 *   - accessibility_snapshot: full Playwright accessibility tree
 *   - dom_locator_inventory: { menubar, menus, items, status_region }
 *   - screenshots: { baseline, reference_prompt, after_prompt }
 *   - console_logs: [all console messages from the page]
 *   - workflows: [ tested workflow results ]
 *   - coverage_notes: { native_menu, renderer_menu, kwin_flaui }
 */

import { _electron as electron } from "playwright";
import * as path from "path";
import * as fs from "fs";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ELECTRON_DIR = path.resolve(__dirname, "..");
const REPO_ROOT = path.resolve(ELECTRON_DIR, "..");

const ARTIFACTS_BASE = process.env.ARTIFACT_DIR || path.join(REPO_ROOT, "artifacts", "accessible-gui-smoke");
const timestamp = new Date().toISOString().replace(/[:.]/g, "-");
const artifactDir = path.join(ARTIFACTS_BASE, timestamp);

// ── Helpers ──

function log(msg) {
  const ts = new Date().toISOString().slice(11, 23);
  console.log(`[${ts}] ${msg}`);
}

function ensureDir(dir) {
  fs.mkdirSync(dir, { recursive: true });
}

async function screenshot(page, name) {
  const filePath = path.join(artifactDir, `${name}.png`);
  await page.screenshot({ path: filePath, fullPage: false });
  log(`  Screenshot: ${name}.png`);
  return filePath;
}

async function collectAccessibilitySnapshot(page) {
  try {
    // Playwright's built-in accessibility snapshot (Chrome DevTools Protocol)
    const snapshot = await page.accessibility.snapshot();
    return snapshot;
  } catch (err) {
    log(`  Accessibility snapshot failed: ${err.message}`);
    return { error: err.message };
  }
}

async function collectLocatorInventory(page) {
  const inventory = {};

  // Menubar
  const menubar = await page.$('[role="menubar"]');
  inventory.menubar_present = !!menubar;
  if (menubar) {
    inventory.menubar_label = await menubar.getAttribute("aria-label");
    const topItems = await page.$$('[role="menubar"] > [role="menuitem"]');
    inventory.top_menu_items = [];
    for (const item of topItems) {
      inventory.top_menu_items.push({
        label: await item.textContent(),
        has_popup: await item.getAttribute("aria-haspopup"),
      });
    }
  }

  // Status region
  const statusEl = await page.$('[role="status"]');
  inventory.status_region_present = !!statusEl;
  if (statusEl) {
    inventory.status_text = await statusEl.textContent();
  }

  // Skip link
  const skipLink = await page.$(".skip-link");
  inventory.skip_link_present = !!skipLink;

  // Buttons with aria-labels
  const buttons = await page.$$("button[aria-label]");
  inventory.aria_labeled_buttons = [];
  for (const btn of buttons) {
    inventory.aria_labeled_buttons.push(await btn.getAttribute("aria-label"));
  }

  // All roles
  const allRoles = await page.evaluate(() => {
    const els = document.querySelectorAll("[role]");
    const roles = {};
    els.forEach((el) => {
      const r = el.getAttribute("role");
      roles[r] = (roles[r] || 0) + 1;
    });
    return roles;
  });
  inventory.all_roles = allRoles;

  return inventory;
}

async function collectConsoleLogs(page) {
  const logs = [];
  page.on("console", (msg) => {
    logs.push({
      type: msg.type(),
      text: msg.text(),
      timestamp: new Date().toISOString(),
    });
  });
  // Clear existing listeners by waiting a frame
  await page.evaluate(() => new Promise((r) => requestAnimationFrame(r)));
  return logs;
}

// ── Launch ──

async function launchElectron() {
  const electronPath = path.join(ELECTRON_DIR, "node_modules", ".bin", "electron");
  const mainEntry = path.join(ELECTRON_DIR, "dist", "main", "index.js");

  log(`Launching Electron from: ${electronPath}`);
  log(`  Main entry: ${mainEntry}`);

  const app = await electron.launch({
    args: [
      mainEntry,
      "--headless",
      "--no-sandbox",
      "--disable-gpu",
      process.env.ELECTRON_EXTRA || "",
    ].filter(Boolean),
    executablePath: electronPath,
    env: {
      ...process.env,
      VOXELFORGE_FORWARD_RENDERER_CONSOLE: "1",
      ELECTRON_ENABLE_STACK_DUMPING: "true",
    },
    timeout: 30000,
  });

  log("  Electron app launched.");

  // Wait for the first BrowserWindow
  const page = await app.firstWindow();
  log(`  Window opened: title="${await page.title()}"`);

  const consoleLogs = [];
  page.on("console", (msg) => {
    const entry = { type: msg.type(), text: msg.text(), timestamp: new Date().toISOString() };
    consoleLogs.push(entry);
    // Also forward to stdout for real-time debugging
    if (msg.type() === "error") {
      console.error(`  [renderer-error] ${msg.text()}`);
    }
  });

  page.on("pageerror", (err) => {
    consoleLogs.push({ type: "pageerror", text: err.message, timestamp: new Date().toISOString() });
    console.error(`  [pageerror] ${err.message}`);
  });

  // Wait for the renderer to load
  await page.waitForSelector("#accessible-menu-bar", { timeout: 15000 }).catch(() => {
    log("  [warn] #accessible-menu-bar not found within timeout");
  });

  // Give renderer time to fully initialize
  await page.waitForTimeout(2000);

  return { app, page, consoleLogs: () => [...consoleLogs] };
}

// ── Workflow tests ──

async function workflowBaselineAccessibility(page) {
  log("Workflow 1: Baseline accessibility inventory...");
  const snapshot = await collectAccessibilitySnapshot(page);
  const inventory = await collectLocatorInventory(page);
  return { snapshot, inventory };
}

async function workflowReferenceModelLoad(page) {
  log("Workflow 2: Reference > Load Reference Model...");

  const results = [];

  // Get baseline screenshot
  const baselinePath = await screenshot(page, "01-baseline");

  // Verify the accessible menu surface exists with expected semantic roles
  const menubar = await page.$('[role="menubar"]');
  if (!menubar) {
    results.push({ step: "find-menubar", ok: false, error: "No [role=menubar] found" });
    return { results, screenshots: { baseline: baselinePath } };
  }
  results.push({ step: "find-menubar", ok: true });

  // Find the Reference menu item (top-level)
  const refButton = await page.$('[role="menubar"] >> text=Reference');
  if (!refButton) {
    results.push({ step: "find-reference-menu", ok: false, error: "Reference menu button not found" });
    return { results, screenshots: { baseline: baselinePath } };
  }
  results.push({ step: "find-reference-menu", ok: true });

  // Click Reference to open its submenu
  await refButton.click();
  await page.waitForTimeout(500);

  // Find the "Load Reference Model…" menu item
  const loadItem = await page.$('[role="menu"] >> text=Load Reference Model');
  if (!loadItem) {
    results.push({ step: "find-load-reference-item", ok: false, error: "Load Reference Model item not found" });
    // Close the menu
    await page.keyboard.press("Escape");
    await page.waitForTimeout(200);
    return { results, screenshots: { baseline: baselinePath } };
  }
  results.push({ step: "find-load-reference-item", ok: true });

  // Take screenshot with menu open
  const menuOpenPath = await screenshot(page, "02-reference-menu-open");

  // Click the item to trigger the prompt
  await loadItem.click();
  await page.waitForTimeout(500);

  // Take screenshot showing the prompt dialog
  const promptPath = await screenshot(page, "03-reference-prompt-visible");

  // Verify the renderer-owned dialog appeared (role="dialog")
  const dialog = await page.$('[role="dialog"]');
  if (dialog) {
    results.push({ step: "dialog-appeared", ok: true });
  } else {
    // May have closed; check status or console
    results.push({ step: "dialog-appeared", ok: false, error: "No role=dialog found after clicking menu item" });
  }

  // Try to drive the prompt dialog through accessible locators
  const dialogInput = dialog ? await dialog.$("input") : null;
  if (dialogInput) {
    await dialogInput.fill("/home/models/sample-reference.obj");
    results.push({ step: "fill-path-input", ok: true });

    // Take screenshot after filling path
    const filledPath = await screenshot(page, "04-path-filled");

    // Submit the dialog (click OK button or press Enter)
    const okButton = await dialog.$('button:has-text("OK")');
    if (okButton) {
      await okButton.click();
    } else {
      await page.keyboard.press("Enter");
    }
    await page.waitForTimeout(500);

    const afterSubmitPath = await screenshot(page, "05-after-submit");
    results.push({ step: "submit-dialog", ok: true });
    results.screenshots = { baseline: baselinePath, menuOpen: menuOpenPath, prompt: promptPath, filled: filledPath, afterSubmit: afterSubmitPath };
  } else {
    results.push({ step: "fill-path-input", ok: false, error: "No input found in prompt dialog" });
    results.screenshots = { baseline: baselinePath, menuOpen: menuOpenPath, prompt: promptPath };
  }

  // Take final screenshot
  const finalPath = await screenshot(page, "06-final");
  results.screenshots = { ...(results.screenshots || {}), final: finalPath };

  return { results, screenshots: results.screenshots };
}

// ── Main ──

async function main() {
  log("=".repeat(60));
  log("  VoxelForge — Playwright Electron Accessibility GUI Smoke Test");
  log("=".repeat(60));

  ensureDir(artifactDir);
  log(`Artifact directory: ${artifactDir}\n`);

  let app;
  let page;
  let consoleLogs = [];
  const results = {};
  let exitCode = 0;

  try {
    // Launch Electron
    const launched = await launchElectron();
    app = launched.app;
    page = launched.page;
    consoleLogs = launched.consoleLogs();

    // Workflow 1: Baseline accessibility
    const baseline = await workflowBaselineAccessibility(page);
    results.baseline = baseline;

    // Workflow 2: Reference > Load Reference Model
    const refWorkflow = await workflowReferenceModelLoad(page);
    results.reference_model_workflow = refWorkflow;

    // Check if menubar has all expected menus
    const expectedMenus = ["File", "Edit", "Reference", "View", "Tools", "Help"];
    const actualMenus = baseline.inventory?.top_menu_items?.map((i) => i.label) || [];
    const missingMenus = expectedMenus.filter((m) => !actualMenus.includes(m));
    if (missingMenus.length > 0) {
      log(`  [FAIL] Missing menus: ${missingMenus.join(", ")}`);
      results.menu_coverage = { ok: false, missing: missingMenus };
      exitCode = 1;
    } else {
      log(`  [PASS] All expected menus present: ${actualMenus.join(", ")}`);
      results.menu_coverage = { ok: true, expected: expectedMenus, actual: actualMenus };
    }

  } catch (err) {
    log(`\n  [ERROR] Harness failed: ${err.message}`);
    console.error(err.stack);
    results.fatal_error = err.message;
    exitCode = 1;
  } finally {
    // Collect final console logs
    const finalLogs = consoleLogs.length > 0 ? consoleLogs : [];
    results.console_logs = finalLogs;

    // Cleanup
    if (app) {
      try {
        await app.close();
        log("  Electron app closed.");
      } catch (e) {
        log(`  [warn] Error closing app: ${e.message}`);
      }
    }
  }

  // ── Write report ──
  const report = {
    harness: "run-accessible-gui-smoke.mjs",
    runner: "Playwright Electron",
    timestamp: new Date().toISOString(),
    artifact_dir: artifactDir,
    app: {
      electron_dir: ELECTRON_DIR,
    },
    results,
    coverage_notes: {
      native_menu:
        "Native Electron menu is retained in reduced form (OS-level items only). " +
        "Primary workflow commands are routed through the renderer-owned accessible " +
        "menu surface (role=menubar). The shared APP_MENU_MODEL ensures both menus " +
        "stay in sync.",
      renderer_menu:
        "Renderer-owned accessible menu surface provides semantic roles " +
        "(menubar/menuitem), keyboard navigation (Tab, Arrow keys, Enter, Escape), " +
        "focus-visible styles, and ARIA labels. Located at [role=menubar] in the " +
        "topbar. Created by AccessibleMenuSurface class.",
      kwin_flaui:
        "For OS-level/native menu validation, use the existing KWin live smoke " +
        "(npm run smoke-test:live-kwin) or FlaUI-based Windows automation. " +
        "The Playwright harness covers in-process renderer accessibility; " +
        "KWin covers the native Electron menu bar (when retained) and window chrome.",
      test_layers: {
        unit_bridge_tests:
          "Deterministic Vitest tests (tests/menu-handlers.test.ts, " +
          "tests/menu-bridge-parity.test.ts) verify handler logic and channel " +
          "alignment without a real BrowserWindow. Fast, reliable, gate-keeping.",
        playwright_accessibility:
          "This harness (scripts/run-accessible-gui-smoke.mjs) launches the " +
          "full Electron app and exercises the renderer-owned accessible surface. " +
          "Captures screenshots, accessibility snapshots, DOM locator inventory, " +
          "and console logs. For Runner/LLM debugging, not CI gate.",
        kwin_os_level:
          "scripts/run-live-kwin-menu-smoke.sh drives the OS-level native menu " +
          "via KWin/EIS for visual validation on the live desktop. Covers native " +
          "Electron menu items not available in the renderer surface.",
      },
    },
    exit_code: exitCode,
  };

  const reportPath = path.join(artifactDir, "report.json");
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2), "utf-8");
  log(`\nReport written: ${reportPath}`);

  // Also write a compact Markdown summary
  const mdPath = path.join(artifactDir, "report.md");
  const mdLines = [
    `# VoxelForge Accessibility GUI Smoke Report`,
    ``,
    `**Harness:** run-accessible-gui-smoke.mjs`,
    `**Timestamp:** ${report.timestamp}`,
    `**Artifact Dir:** ${artifactDir}`,
    `**Exit Code:** ${exitCode}`,
    ``,
    `## Menu Coverage`,
    ``,
    results.menu_coverage
      ? results.menu_coverage.ok
        ? `✅ All ${results.menu_coverage.expected.length} expected menus present: ${results.menu_coverage.actual.join(", ")}`
        : `❌ Missing menus: ${results.menu_coverage.missing?.join(", ")}`
      : `⚠ No menu coverage data`,
    ``,
    `## Screenshots`,
    ``,
  ];
  // Collect all screenshot paths
  const allScreenshots = [];
  if (results.reference_model_workflow?.screenshots) {
    Object.entries(results.reference_model_workflow.screenshots).forEach(([name, filepath]) => {
      allScreenshots.push({ name, filepath });
    });
  }
  for (const ss of allScreenshots) {
    mdLines.push(`- **${ss.name}:** \`${ss.filepath}\``);
  }
  mdLines.push(
    ``,
    `## Workflow Results`,
    ``,
  );
  if (results.reference_model_workflow?.results) {
    for (const step of results.reference_model_workflow.results) {
      const icon = step.ok ? "✅" : "❌";
      mdLines.push(`- ${icon} **${step.step}:** ${step.ok ? "PASS" : step.error || "FAIL"}`);
    }
  }
  mdLines.push(
    ``,
    `## Accessibility Roles Found`,
    ``,
    results.baseline?.inventory?.all_roles
      ? `\`\`\`\n${JSON.stringify(results.baseline.inventory.all_roles, null, 2)}\n\`\`\``
      : `No role data`,
    ``,
    `## Console Logs (${finalLogs.length} entries)`,
    ``,
    ...finalLogs.slice(-30).map((l) => `- [${l.type}] ${l.text}`),
    ``,
    `## Coverage Notes`,
    ``,
  );

  // Add coverage notes
  for (const [key, value] of Object.entries(report.coverage_notes)) {
    if (typeof value === "string") {
      mdLines.push(`### ${key}`);
      mdLines.push(value);
      mdLines.push(``);
    }
  }
  if (report.coverage_notes.test_layers) {
    mdLines.push(`### Test Layers`);
    for (const [layer, desc] of Object.entries(report.coverage_notes.test_layers)) {
      mdLines.push(`- **${layer}:** ${desc}`);
    }
    mdLines.push(``);
  }

  fs.writeFileSync(mdPath, mdLines.join("\n"), "utf-8");
  log(`Markdown summary: ${mdPath}`);

  log(`\n${"=".repeat(60)}`);
  log(`  Exit code: ${exitCode}`);
  log(`  Artifact directory: ${artifactDir}`);
  log(`${"=".repeat(60)}`);

  process.exit(exitCode);
}

main().catch((err) => {
  console.error("Fatal harness error:", err);
  process.exit(1);
});
