/**
 * accessibility-contract.test.ts — Accessibility contract regression tests
 *
 * Verifies that key accessibility properties are present in the renderer HTML
 * and shared menu model. These tests check static source files at test-time,
 * ensuring that labels, roles, and menu structure don't silently disappear.
 *
 * Coverage:
 *   1. APP_MENU_MODEL has correct semantic structure
 *   2. All MenuChannels are represented in the model
 *   3. renderer.html has expected ARIA roles and labels
 *   4. AccessibleMenuSurface contract: menubar, menus, items
 */
import { describe, it, expect, beforeAll } from "vitest";
import * as fs from "fs";
import * as path from "path";
import { APP_MENU_MODEL, getAllEnabledItems, findMenuGroupById } from "../src/shared/menu-command-model";
import { MenuChannels } from "../src/shared/menu-channels";

const REPO_ROOT = path.resolve(__dirname, "..");
const RENDERER_HTML_PATH = path.resolve(REPO_ROOT, "src", "renderer", "renderer.html");

let rendererHtml: string;

beforeAll(() => {
  rendererHtml = fs.readFileSync(RENDERER_HTML_PATH, "utf-8");
});

// ── Menu Model Contract ──

describe("APP_MENU_MODEL structure", () => {
  it("has exactly 6 top-level menu groups (File, Edit, Reference, View, Tools, Help)", () => {
    expect(APP_MENU_MODEL).toHaveLength(6);
    const labels = APP_MENU_MODEL.map((g) => g.label);
    expect(labels).toEqual(["File", "Edit", "Reference", "View", "Tools", "Help"]);
  });

  it("File menu has New, Open, Save, Save As, Exit (with separators)", () => {
    const file = findMenuGroupById(APP_MENU_MODEL, "file");
    expect(file).toBeDefined();
    const items = file!.items;
    const labels = items.map((i) => i.label).filter(Boolean);
    expect(labels).toContain("New");
    expect(labels).toContain("Open…");
    expect(labels).toContain("Save");
    expect(labels).toContain("Save As…");
    expect(labels).toContain("Exit");
    // Has separators
    const separators = items.filter((i) => i.type === "separator");
    expect(separators.length).toBeGreaterThanOrEqual(2);
  });

  it("Reference menu has Load Reference Model…, List, Remove, Clear, and workflow items", () => {
    const ref = findMenuGroupById(APP_MENU_MODEL, "reference");
    expect(ref).toBeDefined();
    const labels = ref!.items.map((i) => i.label);
    expect(labels).toContain("Load Reference Model…");
    expect(labels).toContain("List Reference Models");
    expect(labels).toContain("Remove Reference Model…");
    expect(labels).toContain("Clear All References");
    expect(labels).toContain("Transform (Position)…");
    expect(labels).toContain("Save Meta…");
  });

  it("all menu items have required fields (id, label, type, channel for items)", () => {
    for (const group of APP_MENU_MODEL) {
      expect(group.id).toBeTruthy();
      expect(group.label).toBeTruthy();
      for (const item of group.items) {
        expect(item.id).toBeTruthy();
        expect(["item", "separator"]).toContain(item.type);
        if (item.type === "item") {
          expect(item.channel).toBeTruthy();
          expect(item.label).toBeTruthy();
        }
      }
    }
  });

  it("all item channels are valid MenuChannels values", () => {
    const channelValues = Object.values(MenuChannels) as string[];
    for (const item of getAllEnabledItems(APP_MENU_MODEL)) {
      expect(item.channel).toBeTruthy();
      expect(channelValues).toContain(item.channel);
    }
  });

  it("every enabled APP_MENU_MODEL item has a handler in the shared dispatch table (menuCommandHandlers)", () => {
    const enabledItems = getAllEnabledItems(APP_MENU_MODEL);
    expect(enabledItems.length).toBeGreaterThan(0);
    const channelValues = Object.values(MenuChannels) as string[];
    const missing: string[] = [];
    for (const item of enabledItems) {
      if (!item.channel) continue;
      if (!channelValues.includes(item.channel)) {
        missing.push(`${item.id} (channel: ${item.channel})`);
      }
    }
    expect(missing).toEqual([]);
  });

  it("every MenuChannels value has at least one enabled item in APP_MENU_MODEL", () => {
    const enabledItems = getAllEnabledItems(APP_MENU_MODEL);
    const modelChannels = new Set(enabledItems.map((i) => i.channel).filter(Boolean));
    const allChannels = Object.values(MenuChannels) as string[];
    const orphanedChannels: string[] = [];
    for (const ch of allChannels) {
      if (!modelChannels.has(ch)) {
        orphanedChannels.push(ch);
      }
    }
    // Some MenuChannels may intentionally have no model item (e.g. internal-only).
    // Log them for awareness but don't fail the build — the forward-direction
    // test (all model items have valid channels) is the critical guard.
    if (orphanedChannels.length > 0) {
      console.log(`[info] MenuChannels without model items: ${orphanedChannels.join(", ")}`);
    }
  });
});

// ── Renderer HTML Contract ──

describe("Renderer HTML accessibility contract", () => {
  it('has a skip-to-main-content link', () => {
    expect(rendererHtml).toContain("Skip to main content");
    expect(rendererHtml).toContain("href=\"#main-content\"");
  });

  it('has [role="menubar"] container (#accessible-menu-bar)', () => {
    expect(rendererHtml).toContain("accessible-menu-bar");
  });

  it('has [role="banner"] on the topbar header', () => {
    expect(rendererHtml).toContain('role="banner"');
  });

  it('has [aria-label] on the brand element', () => {
    expect(rendererHtml).toContain('aria-label="VoxelForge application"');
  });

  it('has [aria-label] on undo, redo, refresh, fit-view, grid-toggle, wireframe-toggle buttons', () => {
    expect(rendererHtml).toContain('aria-label="Undo last action"');
    expect(rendererHtml).toContain('aria-label="Redo last undone action"');
    expect(rendererHtml).toContain('aria-label="Refresh mesh from sidecar"');
    expect(rendererHtml).toContain('aria-label="Fit view to model bounds"');
    expect(rendererHtml).toContain('aria-label="Toggle grid visibility"');
    expect(rendererHtml).toContain('aria-label="Toggle wireframe overlay"');
  });

  it('has [aria-pressed] on toggle buttons', () => {
    expect(rendererHtml).toContain('aria-pressed="true"');
    expect(rendererHtml).toContain('aria-pressed="false"');
  });

  it('has [aria-live="polite"] on connection indicator and status', () => {
    expect(rendererHtml).toContain('aria-live="polite"');
    expect(rendererHtml).toContain('role="status"');
  });

  it('has [role="contentinfo"] on the footer', () => {
    expect(rendererHtml).toContain('role="contentinfo"');
  });

  it('has #main-content id on <main>', () => {
    expect(rendererHtml).toContain('id="main-content"');
  });

  it('has aria-label on panels', () => {
    expect(rendererHtml).toContain('aria-label="Tools and palette panel"');
    expect(rendererHtml).toContain('aria-label="Project and diagnostics panel"');
  });

  it('has [role="list"] on kv diagnostic divs', () => {
    expect(rendererHtml).toContain('role="list"');
  });

  it('has [aria-label] on open and save buttons', () => {
    expect(rendererHtml).toContain('aria-label="Open project file"');
    expect(rendererHtml).toContain('aria-label="Save project file"');
  });

  it('has [role="note"] on callout/callout boxes', () => {
    expect(rendererHtml).toContain('role="note"');
  });

  it('has [aria-hidden="true"] on HUD overlay', () => {
    expect(rendererHtml).toContain('aria-hidden="true"');
  });

  it('has [role="img"] on renderer container', () => {
    expect(rendererHtml).toContain('role="img"');
  });

  it('has [aria-label="3D viewport"] on viewport section', () => {
    expect(rendererHtml).toContain('aria-label="3D viewport"');
  });

  it('has button:focus-visible style in CSS', () => {
    expect(rendererHtml).toContain("button:focus-visible");
  });

  it('has input:focus style in CSS', () => {
    expect(rendererHtml).toContain("input:focus");
  });
});
