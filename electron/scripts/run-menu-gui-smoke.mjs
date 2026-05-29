#!/usr/bin/env node
/**
 * run-menu-gui-smoke.mjs — Launcher for the Electron menu GUI smoke test.
 *
 * Checks for a display server (Xvfb/xvfb-run), then runs the menu GUI smoke
 * test via Electron. Provides clear error messages on headless hosts without
 * Xvfb installed.
 *
 * Usage:
 *   node scripts/run-menu-gui-smoke.mjs
 *
 * Environment variables:
 *   XVFB_DISPLAY   — Xvfb display to use (default: :99)
 *   XVFB_SCREEN    — Xvfb screen config (default: 0 1920x1080x24)
 */

import { spawnSync, spawn } from "child_process";
import { fileURLToPath } from "url";
import path from "path";
import fs from "fs";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const scriptPath = path.join(__dirname, "menu-gui-smoke.mjs");
const electronPkgPath = path.join(__dirname, "..", "node_modules", ".bin", "electron");

function findElectron() {
  const candidates = [
    electronPkgPath,
    path.join(__dirname, "..", "node_modules", "electron", "dist", "electron"),
    path.join(__dirname, "..", "node_modules", "electron", "cli.js"),
    path.join(process.env.HOME || "~", "node_modules", ".bin", "electron"),
  ];

  for (const p of candidates) {
    try {
      const resolved = fs.realpathSync(p);
      if (fs.statSync(resolved).isFile()) return resolved;
    } catch {
      // not found
    }
  }
  return null;
}

function findXvfbRun() {
  try {
    const result = spawnSync("which", ["xvfb-run"], { stdio: "pipe", encoding: "utf-8" });
    if (result.status === 0) return result.stdout.trim();
  } catch { /* not found */ }
  return null;
}

function checkXvfbInstalled() {
  try {
    const result = spawnSync("which", ["Xvfb"], { stdio: "pipe", encoding: "utf-8" });
    return result.status === 0;
  } catch { return false; }
}

async function main() {
  console.log("=".repeat(70));
  console.log("  Electron Menu GUI Smoke Test — Launcher");
  console.log("=".repeat(70));

  const hasDisplay = !!process.env.DISPLAY;
  const xvfbRunPath = findXvfbRun();
  const xvfbInstalled = checkXvfbInstalled();

  if (!hasDisplay && !xvfbRunPath && !xvfbInstalled) {
    console.error(`
  ERROR: No display server available and Xvfb is not installed.

  This test requires a display server (Xvfb or real X display) to launch
  Electron with a BrowserWindow.

  Install Xvfb:
    sudo apt install xvfb            # Debian/Ubuntu
    sudo pacman -S xorg-server-xvfb  # Arch
    sudo dnf install xorg-x11-server-Xvfb  # Fedora

  Then run using the xvfb-run.sh wrapper:
    ./scripts/xvfb-run.sh node scripts/run-menu-gui-smoke.mjs

  Or install xvfb-run (from xvfb package) and it will be auto-detected.
`);
    process.exit(1);
  }

  const electronPath = findElectron();
  if (!electronPath) {
    console.log("  [info] electron binary not found via realpath; trying 'npx electron'...");
  }

  const electronCmd = electronPath || "npx";
  const electronArgs = electronPath ? [scriptPath] : ["electron", scriptPath];
  if (process.env.WAYLAND_DISPLAY) {
    electronArgs.splice(electronPath ? 0 : 1, 0, "--ozone-platform=wayland");
  }

  if (hasDisplay) {
    // DISPLAY is set (or macOS/Windows), run directly
    console.log(`  Running: ${electronCmd} ${electronArgs.join(" ")}\n`);
    const child = spawn(electronCmd, electronArgs, { stdio: "inherit" });
    child.on("exit", (code) => process.exit(code ?? 1));
    return;
  }

  if (xvfbRunPath) {
    // xvfb-run is available (Debian/Ubuntu style)
    const args = ["--auto-servernum", "--server-args=-screen 0 1920x1080x24", electronCmd, ...electronArgs];
    console.log(`  Running via xvfb-run: ${xvfbRunPath} ${args.join(" ")}\n`);
    const child = spawn(xvfbRunPath, args, { stdio: "inherit" });
    child.on("exit", (code) => process.exit(code ?? 1));
    return;
  }

  // Xvfb is installed but no xvfb-run script; start Xvfb manually
  const display = process.env.XVFB_DISPLAY || ":99";
  const screen = process.env.XVFB_SCREEN || "0 1920x1080x24";
  console.log(`  Starting Xvfb on ${display}...`);

  const xvfb = spawn("Xvfb", [display, "-screen", ...screen.split(" ")], {
    stdio: ["ignore", "pipe", "pipe"],
  });

  xvfb.stderr.on("data", (d) => process.stderr.write(`[xvfb] ${d}`));
  xvfb.on("error", (err) => {
    console.error(`  Failed to start Xvfb: ${err.message}`);
    process.exit(1);
  });

  // Wait for Xvfb to be ready
  await new Promise((resolve) => setTimeout(resolve, 1500));

  const child = spawn(electronCmd, electronArgs, {
    stdio: "inherit",
    env: { ...process.env, DISPLAY: display },
  });

  child.on("exit", (code) => {
    xvfb.kill();
    process.exit(code ?? 1);
  });
}

main().catch((err) => {
  console.error("Fatal error:", err);
  process.exit(1);
});
