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
 *   DISPLAY         — X display for execution
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
import { spawn, spawnSync } from "child_process";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ELECTRON_DIR = path.resolve(__dirname, "..");
const REPO_ROOT = path.resolve(ELECTRON_DIR, "..");

const ARTIFACTS_BASE = process.env.ARTIFACT_DIR || path.join(REPO_ROOT, "artifacts", "accessible-gui-smoke");
const timestamp = new Date().toISOString().replace(/[:.]/g, "-");
const artifactDir = path.join(ARTIFACTS_BASE, timestamp);

/** Track whether a real Playwright/CDP workflow actually ran. */
let ranRealWorkflow = false;
/** Array of all child process handles for cleanup. */
const childProcesses = [];

// ── Helpers ──

/** Start Xvfb if no display is available (e.g. headless CI). */
function ensureDisplay() {
  if (!process.env.DISPLAY || process.env.DISPLAY === "") {
    log("  No DISPLAY set; attempting to start Xvfb...");
    try {
      const xvfb = spawnSync("Xvfb", [
        ":99", "-screen", "0", "1920x1080x24",
      ], { stdio: "pipe", timeout: 3000 });
      if (xvfb.status === 0 || xvfb.error) {
        // If Xvfb was not found (ENOENT), proceed without it
        if (xvfb.error && xvfb.error.code === "ENOENT") {
          log("  [warn] Xvfb not found on this system; proceeding without display.");
          return;
        }
        process.env.DISPLAY = ":99";
        log("  Xvfb started on :99");
      } else {
        log(`  [warn] Xvfb exited with code ${xvfb.status}; stderr: ${xvfb.stderr.toString().trim()}`);
      }
    } catch (e) {
      log(`  [warn] Could not start Xvfb: ${e.message}; will try without it.`);
    }
  } else {
    log(`  DISPLAY=${process.env.DISPLAY}`);
  }
}

/** Detect whether we are running under Wayland. */
function isWayland() {
  return process.env.WAYLAND_DISPLAY && process.env.WAYLAND_DISPLAY !== "";
}

/** Detect whether we have a usable X11 display. */
function hasDisplay() {
  return process.env.DISPLAY && process.env.DISPLAY !== "" && fs.existsSync("/tmp/.X11-unix");
}

/** Choose the right Ozone platform for the current environment. */
function getOzonePlatform() {
  if (isWayland()) return "--ozone-platform-hint=auto";
  if (!hasDisplay()) return "--ozone-platform=headless";
  return null; // default X11, no flag needed
}

/**
 * Determine whether to pass --headless to Electron.
 * Only pass --headless when no real display is available AND Ozone isn't already
 * handling headless mode.
 */
function shouldAddHeadlessFlag() {
  // Ozone headless mode handles headless rendering natively — no --headless needed.
  if (getOzonePlatform() === "--ozone-platform=headless") return false;
  // When a real X11 display is available, never add --headless.
  if (hasDisplay()) return false;
  // Fallback: only add --headless when we have no display and Ozone isn't handling it.
  return true;
}

/** Detect whether the Ozone platform hint flag is supported (Electron 29+). */
function supportsOzoneHint() {
  // Electron 29+ supports --ozone-platform-hint; 41 certainly does.
  return true;
}

/**
 * CDP-based Electron launch via raw Chromium DevTools Protocol.
 * Uses `electron --remote-debugging-port=0` and connects via CDP to work around
 * Playwright _electron.launch reliability issues on Electron 41/Wayland.
 *
 * Requires a display server (Xvfb or real X display). This function does not
 * start Xvfb itself; call ensureDisplay() first.
 */
async function launchElectronViaCDP() {
  const electronPath = path.join(ELECTRON_DIR, "node_modules", ".bin", "electron");
  const mainEntry = path.join(ELECTRON_DIR, "dist", "main", "index.js");

  log(`[CDP] Launching Electron via CDP fallback...`);
  log(`[CDP]   Electron: ${electronPath}`);
  log(`[CDP]   Entry: ${mainEntry}`);

  const child = spawn(electronPath, [
    mainEntry,
    `--remote-debugging-port=0`,
    "--renderer-only",
    "--no-sandbox",
    "--disable-gpu",
    ...(supportsOzoneHint() && getOzonePlatform() ? [getOzonePlatform()] : []),
    ...(shouldAddHeadlessFlag() ? ["--headless"] : []),
  ].filter(Boolean), {
    env: {
      ...process.env,
      VOXELFORGE_FORWARD_RENDERER_CONSOLE: "1",
      ELECTRON_ENABLE_STACK_DUMPING: "true",
    },
    stdio: ["ignore", "pipe", "pipe"],
  });

  childProcesses.push(child);

  // Wait for DevTools listening URL from stderr
  let debugUrl = null;
  const stderrChunks = [];
  // Electron 41 prints: "DevTools listening on ws://127.0.0.1:PORT/PATH"
  const urlRegex = /DevTools listening on (ws:\/\/[^\s]+)/;

  await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      reject(new Error(`CDP launch timeout (30s): stderr so far:\n${stderrChunks.join("")}`));
    }, 30000);

    child.stderr.on("data", (chunk) => {
      const text = chunk.toString();
      stderrChunks.push(text);
      const match = text.match(urlRegex);
      if (match) {
        debugUrl = match[1];
        clearTimeout(timeout);
        resolve();
      }
    });

    child.stdout.on("data", (chunk) => {
      stderrChunks.push(chunk.toString());
    });

    child.on("error", (err) => {
      clearTimeout(timeout);
      reject(err);
    });

    child.on("exit", (code, signal) => {
      clearTimeout(timeout);
      if (!debugUrl) {
        reject(new Error(`Electron exited with code=${code} signal=${signal} before CDP URL emitted. stderr:\n${stderrChunks.join("")}`));
      }
    });
  });

  if (!debugUrl) {
    child.kill();
    throw new Error("CDP: Could not extract debug WebSocket URL from Electron output");
  }

  log(`[CDP] Debug URL: ${debugUrl}`);

  // Connect via Playwright's CDP session
  const { chromium } = await import("playwright");
  const browser = await chromium.connectOverCDP(debugUrl);
  const context = browser.contexts()[0] || await browser.newContext();
  const pages = context.pages();
  // Use an existing page (created by runRendererOnly with a BrowserWindow)
  // or create one if none exists (e.g. for non-window renderers).
  const page = pages.length > 0
    ? pages[0]
    : await context.newPage().catch(() => {
        // If newPage() fails (e.g. headless mode without target support),
        // poll for existing pages
        return new Promise((resolve, reject) => {
          const deadline = Date.now() + 10000;
          const poll = () => {
            if (Date.now() > deadline) return reject(new Error("Timeout waiting for page"));
            const ctx = browser.contexts()[0];
            if (ctx && ctx.pages().length > 0) return resolve(ctx.pages()[0]);
            setTimeout(poll, 200);
          };
          poll();
        });
      });

  // Wait for the page to load the renderer HTML
  const consoleLogs = [];
  page.on("console", (msg) => {
    const entry = { type: msg.type(), text: msg.text(), timestamp: new Date().toISOString() };
    consoleLogs.push(entry);
    if (msg.type() === "error") {
      console.error(`  [renderer-error] ${msg.text()}`);
    }
  });
  page.on("pageerror", (err) => {
    consoleLogs.push({ type: "pageerror", text: err.message, timestamp: new Date().toISOString() });
    console.error(`  [pageerror] ${err.message}`);
  });

  try {
    await page.waitForSelector("#accessible-menu-bar", { timeout: 15000 });
  } catch {
    log("  [warn] #accessible-menu-bar not found within timeout");
  }
  await page.waitForTimeout(2000);

  const title = await page.title().catch(() => "(no title)");
  log(`[CDP] Window opened: title="${title}"`);

  return {
    app: {
      child,
      async close() {
        try {
          await browser.close();
        } catch (e) { /* ignore */ }
        child.kill();
      },
    },
    page,
    consoleLogs: () => [...consoleLogs],
  };
}

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
  // Ensure a display is available
  ensureDisplay();

  const electronPath = path.join(ELECTRON_DIR, "node_modules", ".bin", "electron");
  const mainEntry = path.join(ELECTRON_DIR, "dist", "main", "index.js");

  log(`Launching Electron from: ${electronPath}`);
  log(`  Main entry: ${mainEntry}`);
  log(`  DISPLAY: ${process.env.DISPLAY || "(none)"}`);
  log(`  Wayland: ${isWayland() ? "yes" : "no"}`);
  log(`  Ozone hint: ${getOzonePlatform() || "(default X11)"}`);
  log(`  Headless flag: ${shouldAddHeadlessFlag() ? "yes" : "no"}`);

  const app = await electron.launch({
    args: [
      mainEntry,
      "--renderer-only",
      ...(shouldAddHeadlessFlag() ? ["--headless"] : []),
      "--no-sandbox",
      "--disable-gpu",
      ...(supportsOzoneHint() && getOzonePlatform() ? [getOzonePlatform()] : []),
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
  // Use a scoped approach: find the dialog, then ensure we're only interacting
  // with inputs inside [role=dialog] to avoid accidentally filling the
  // Project I/O input (#project-path) which is outside the dialog.
  const dialog = await page.$('[role="dialog"]');
  if (dialog) {
    results.push({ step: "dialog-appeared", ok: true });
  } else {
    // May have closed; check status or console
    results.push({ step: "dialog-appeared", ok: false, error: "No role=dialog found after clicking menu item" });
  }

  // Try to drive the prompt dialog through accessible locators
  // Scope the input query to [role=dialog] exclusively — this prevents
  // accidentally filling the Project I/O field (#project-path input) which
  // lives outside the dialog in the page body. Playwright's ElementHandle.$()
  // scopes to within the element, so dialog.$("input") only finds inputs
  // inside the dialog, not the project path input in the page.
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

// ── Kill all tracked child processes ──

function killChildProcesses() {
  for (const child of childProcesses) {
    try {
      if (child && !child.killed) {
        child.kill("SIGTERM");
        // Give it a moment, then SIGKILL
        try {
          const killed = child.kill("SIGKILL");
        } catch (e) { /* ignore */ }
      }
    } catch (e) { /* ignore */ }
  }
  childProcesses.length = 0;
}

// ── Write report ──

function writeReport(results, exitCode, finalLogs, app) {
  const report = {
    harness: "run-accessible-gui-smoke.mjs",
    runner: "Playwright Electron",
    timestamp: new Date().toISOString(),
    artifact_dir: artifactDir,
    app: {
      electron_dir: ELECTRON_DIR,
      launched: !!app,
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
    ran_real_workflow: ranRealWorkflow,
  };

  // Always write report.json
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
    `**Real Workflow Ran:** ${ranRealWorkflow}`,
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

  return reportPath;
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
  let finalLogs = [];
  const results = {};
  let exitCode = 0;

  try {
    // Launch Electron — try Playwright _electron.launch first
    let launched;
    let launchMethod = "_electron.launch";
    try {
      launched = await launchElectron();
      ranRealWorkflow = true;
    } catch (launchErr) {
      log(`  [info] _electron.launch failed: ${launchErr.message}`);
      log(`  [info] Attempting CDP-based fallback launch...`);
      launchMethod = "CDP";
      try {
        launched = await launchElectronViaCDP();
        ranRealWorkflow = true;
      } catch (cdpErr) {
        log(`  [info] CDP fallback also failed: ${cdpErr.message}`);
        // Both methods failed. Check if display is the issue.
        const noDisplay = !process.env.DISPLAY || process.env.DISPLAY === "";
        const suggestions = [];
        if (noDisplay) {
          suggestions.push(
            "Install Xvfb: sudo pacman -S xorg-server-xvfb (Arch) / sudo apt install xvfb (Debian)",
          );
        }
        suggestions.push("Run on a Wayland/X11 desktop with DISPLAY set");
        suggestions.push("Use ./scripts/xvfb-run.sh <command> after installing Xvfb");

        results.launch_status = {
          ok: false,
          method: launchMethod,
          error: launchErr.message,
          cdp_error: cdpErr.message,
          environment: {
            display: process.env.DISPLAY || "(none)",
            wayland: isWayland(),
            xvfb_available: false,
          },
          resolution: suggestions.join("; "),
        };
        results.menu_coverage = { ok: false, error: "App launch failed — no display server available" };
        results.reference_model_workflow = { results: [], screenshots: {} };
        results.baseline = { snapshot: null, inventory: {} };
        log(`\n  [SKIP] GUI smoke requires a display server (Xvfb or desktop).`);
        log(`  Install Xvfb: sudo pacman -S xorg-server-xvfb`);
        log(`  Then run: ./scripts/xvfb-run.sh npm run gui:llm-smoke\n`);
        log(`  Report will be written with environment diagnostics.`);
        // FAIL CLOSED: set non-zero exit code because no real workflow executed.
        // A prerequisite miss may be explicitly reported, but acceptance criterion
        // #3 (GUI workflow) was not satisfied.
        exitCode = 1;
        // Do NOT return early — fall through to report-writing below.
      }
    }

    if (launched) {
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
    }

  } catch (err) {
    log(`\n  [ERROR] Harness failed: ${err.message}`);
    console.error(err.stack);
    results.fatal_error = err.message;
    exitCode = 1;
  } finally {
    // Collect final console logs
    finalLogs = consoleLogs.length > 0 ? consoleLogs : [];
    results.console_logs = finalLogs;

    // Cleanup: close Playwright app first
    if (app) {
      try {
        await app.close();
        log("  Electron app closed.");
      } catch (e) {
        log(`  [warn] Error closing app: ${e.message}`);
      }
    }

    // Kill any orphaned child processes (CDP path, anything spawned but not cleaned up)
    killChildProcesses();
  }

  // ── Write report (always, on success AND failure) ──
  const reportPath = writeReport(results, exitCode, finalLogs, app);

  process.exit(exitCode);
}

main().catch((err) => {
  console.error("Fatal harness error:", err);
  // Kill orphaned children on fatal error too
  killChildProcesses();
  process.exit(1);
});
