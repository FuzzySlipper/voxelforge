#!/usr/bin/env node
/**
 * check-build-assets.mjs — Regression check for required runtime static assets
 *
 * Verifies that all files expected at runtime by the Electron app exist after
 * `npm run build`. Run from the electron/ directory after build.
 *
 * Required assets listed here correspond to paths loaded by the Electron main
 * process (src/main/index.ts) and referenced by the renderer HTML.
 *
 * Exit codes:
 *   0 — all required assets present
 *   1 — one or more required assets missing
 */

import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ELECTRON_DIR = path.resolve(__dirname, "..");

/** Required runtime assets (paths relative to electron/). */
const REQUIRED_ASSETS = [
  // Main process entry — loaded by Electron's "main" field in package.json
  "dist/main/index.js",

  // Preload script — loaded by BrowserWindow webPreferences.preload
  "dist/preload/index.js",

  // Renderer bundle — loaded via <script src="./bundle.js"> in renderer.html
  "dist/renderer/bundle.js",

  // Renderer HTML — loaded by mainWindow.loadFile() in src/main/index.ts
  "dist/renderer/renderer.html",

  // Shared modules — compiled from src/shared/* into dist/main/ and dist/renderer/
  // (at minimum one shared module that the main process imports directly)
  "dist/shared/menu-channels.js",
  "dist/shared/menu-command-model.js",
  "dist/shared/menu-command-dispatch.js",
  "dist/shared/string-utils.js",
  "dist/shared/byte-utils.js",
  "dist/shared/frame-parser.js",
  "dist/shared/compute-placement.js",
  "dist/shared/refresh-coalescer.js",

  // Renderer-core module — imported by renderer/index.ts
  "dist/renderer-core/index.js",
];

let exitCode = 0;
const missing = [];

for (const relativePath of REQUIRED_ASSETS) {
  const fullPath = path.join(ELECTRON_DIR, relativePath);
  if (!fs.existsSync(fullPath)) {
    missing.push(relativePath);
    console.error(`  MISSING  ${relativePath}`);
    exitCode = 1;
  } else {
    const stat = fs.statSync(fullPath);
    console.log(`  OK       ${relativePath}  (${stat.size} bytes)`);
  }
}

if (exitCode === 0) {
  console.log(`\n✅ All ${REQUIRED_ASSETS.length} required runtime assets present.`);
} else {
  console.error(`\n❌ ${missing.length} required runtime asset(s) missing:`);
  for (const f of missing) {
    console.error(`   - ${f}`);
  }
}

process.exit(exitCode);
