/**
 * menu-handlers.ts — Factored renderer menu command handlers.
 *
 * Each handler is a pure function taking a `MenuHandlerDeps` object so that the
 * same code path can be tested deterministically (with mock deps) and wired
 * into the real renderer (with real window.prompt / bridge request / etc.).
 *
 * The real renderer's setupMenuEventListeners() imports these and passes real
 * deps; vitest tests import the same functions and pass mock deps to assert
 * the exact bridge request payloads.
 */

import type { MenuHandlerDeps } from "./menu-handler-deps";

// Re-export for convenience
export type { MenuHandlerDeps };

// ── Reference Model handlers ──

/**
 * Reference > Load Reference Model...
 *
 * Prompts for a file path; if accepted, sends a `bridge:myra-command-execute`
 * request with command "refload" and the path as an argument.
 */
export function handleReferenceModelLoad(deps: MenuHandlerDeps): void {
  deps.log("[renderer] Menu event received: menu:reference-model-load");
  const path = deps.promptUser(
    "Load Reference Model — Enter file path\n\nSupported: .obj .fbx .gltf .glb .stl .dae .x .3ds .blend",
    "",
  );
  if (path) {
    deps.log(`[renderer] Accepted path: "${path}"`);
    void deps.myraExecuteCommand("Load Ref Model", "refload", [path]);
  } else {
    deps.log("[renderer] menu:reference-model-load cancelled by user (no path entered)");
    deps.setStatus("Load Reference Model cancelled.");
  }
}

/**
 * Reference > List Reference Models
 *
 * Sends a bridge:myra-command-execute request with command "reflist".
 */
export function handleReferenceModelList(deps: MenuHandlerDeps): void {
  deps.log("[renderer] Menu event received: menu:reference-model-list");
  void deps.myraExecuteCommand("List Ref Models", "reflist", []);
}

/**
 * Reference > Remove Reference Model...
 *
 * Prompts for an index; if accepted, sends bridge:myra-command-execute
 * with command "refremove".
 */
export function handleReferenceModelRemove(deps: MenuHandlerDeps): void {
  deps.log("[renderer] Menu event received: menu:reference-model-remove");
  const idx = deps.promptInt("Remove Reference Model — enter index:");
  if (idx !== null) {
    void deps.myraExecuteCommand("Remove Ref Model", "refremove", [String(idx)]);
  } else {
    deps.log("[renderer] menu:reference-model-remove cancelled by user");
  }
}

// ── File menu handlers (representative) ──

/**
 * File > New
 *
 * Confirms with the user; if accepted, sends a bridge:project-new request.
 */
export function handleFileNew(deps: MenuHandlerDeps): void {
  deps.log("[renderer] Menu event received: menu:file-new");
  if (deps.confirmUser("Clear the current model? Unsaved changes will be lost.")) {
    void deps.runAction("New Project", "bridge:project-new", {});
  } else {
    deps.log("[renderer] menu:file-new cancelled by user");
  }
}

/**
 * File > Open...
 *
 * Prompts for a project path; if accepted, sends bridge:project-load.
 */
export function handleFileOpen(deps: MenuHandlerDeps): void {
  deps.log("[renderer] Menu event received: menu:file-open");
  const path = deps.promptUser("Enter project file path (.vforge):", deps.getDefaultPath?.() ?? "");
  if (path) {
    void deps.runAction("Open", "bridge:project-load", { path });
  } else {
    deps.log("[renderer] menu:file-open cancelled by user");
  }
}
