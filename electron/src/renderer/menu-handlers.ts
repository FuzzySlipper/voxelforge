/**
 * menu-handlers.ts — Factored renderer menu command handlers.
 *
 * Each handler is a pure function taking a `MenuHandlerDeps` object so that the
 * same code path can be tested deterministically (with mock deps) and wired
 * into the real renderer (with real native dialogs / bridge request / etc.).
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
 * Opens native file dialog filtered to supported 3D model formats.
 * On selection, sends a `bridge:myra-command-execute` request with
 * command "refload" and the selected path.
 * On cancel, logs and sets status — does not silently no-op.
 */
export async function handleReferenceModelLoad(deps: MenuHandlerDeps): Promise<void> {
  deps.log("[renderer] Menu event received: menu:reference-model-load");
  const result = await deps.selectFile("reference-model-open");
  if (!result.canceled && result.filePaths.length > 0) {
    const selectedPath = result.filePaths[0];
    deps.log(`[renderer] Selected reference model path: "${selectedPath}"`);
    void deps.myraExecuteCommand("Load Ref Model", "refload", [selectedPath]);
  } else {
    deps.log("[renderer] menu:reference-model-load cancelled by user (dialog canceled)");
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
 * Opens native file dialog filtered to .vforge files.
 * On selection, sends bridge:project-load with selected path.
 * On cancel, logs and sets status.
 */
export async function handleFileOpen(deps: MenuHandlerDeps): Promise<void> {
  deps.log("[renderer] Menu event received: menu:file-open");
  const defaultPath = deps.getDefaultPath?.() ?? "";
  const result = await deps.selectFile("vforge-open", defaultPath);
  if (!result.canceled && result.filePaths.length > 0) {
    const selectedPath = result.filePaths[0];
    deps.log(`[renderer] Selected project file: "${selectedPath}"`);
    void deps.runAction("Open", "bridge:project-load", { path: selectedPath });
  } else {
    deps.log("[renderer] menu:file-open cancelled by user (dialog canceled)");
    deps.setStatus("File Open cancelled.");
  }
}

/**
 * File > Save As...
 *
 * Opens native save dialog for .vforge files.
 * On selection, ensures .vforge extension and sends bridge:project-save.
 */
export async function handleFileSaveAs(deps: MenuHandlerDeps): Promise<void> {
  deps.log("[renderer] Menu event received: menu:file-save-as");
  const defaultPath = deps.getDefaultPath?.() ?? "";
  const result = await deps.saveFile("vforge-save", defaultPath);
  if (!result.canceled && result.filePaths.length > 0) {
    let selectedPath = result.filePaths[0];
    if (!selectedPath.endsWith(".vforge")) {
      selectedPath += ".vforge";
    }
    deps.log(`[renderer] Save As path: "${selectedPath}"`);
    void deps.runAction("Save As", "bridge:project-save", { path: selectedPath });
  } else {
    deps.log("[renderer] menu:file-save-as cancelled by user (dialog canceled)");
    deps.setStatus("Save As cancelled.");
  }
}

/**
 * Reference > Load Meta...
 *
 * Opens native file dialog filtered to .refmeta files.
 * On selection, runs refloadmeta command.
 * If backend rejects (no loaded reference model), error is surfaced by myraExecuteCommand.
 */
export async function handleReferenceMetaLoad(deps: MenuHandlerDeps): Promise<void> {
  deps.log("[renderer] Menu event received: menu:reference-meta-load");
  const result = await deps.selectFile("refmeta-open");
  if (!result.canceled && result.filePaths.length > 0) {
    const selectedPath = result.filePaths[0];
    deps.log(`[renderer] Selected refmeta file: "${selectedPath}"`);
    void deps.myraExecuteCommand("Load Meta", "refloadmeta", [selectedPath]);
  } else {
    deps.log("[renderer] menu:reference-meta-load cancelled by user (dialog canceled)");
    deps.setStatus("Load Meta cancelled.");
  }
}

/**
 * Reference > Save Meta...
 *
 * Collects reference index via prompt, then opens native save dialog for .refmeta.
 */
export async function handleReferenceMetaSave(deps: MenuHandlerDeps): Promise<void> {
  deps.log("[renderer] Menu event received: menu:reference-meta-save");
  const idx = deps.promptInt("Save Meta — Reference Model Index:");
  if (idx === null) {
    deps.log("[renderer] menu:reference-meta-save cancelled by user (no index)");
    return;
  }
  const result = await deps.saveFile("refmeta-save", `${idx}.refmeta`);
  if (!result.canceled && result.filePaths.length > 0) {
    let selectedPath = result.filePaths[0];
    if (!selectedPath.endsWith(".refmeta")) {
      selectedPath += ".refmeta";
    }
    deps.log(`[renderer] Save Meta path: "${selectedPath}"`);
    void deps.myraExecuteCommand("Save Meta", "refsave", [String(idx), selectedPath]);
  } else {
    deps.log("[renderer] menu:reference-meta-save cancelled by user (dialog canceled)");
    deps.setStatus("Save Meta cancelled.");
  }
}

/**
 * Reference > Image Ref Load...
 *
 * Opens native file dialog filtered to image files.
 */
export async function handleImageRefLoad(deps: MenuHandlerDeps): Promise<void> {
  deps.log("[renderer] Menu event received: menu:image-ref-load");
  const result = await deps.selectFile("image-open");
  if (!result.canceled && result.filePaths.length > 0) {
    const selectedPath = result.filePaths[0];
    deps.log(`[renderer] Selected image ref: "${selectedPath}"`);
    void deps.myraExecuteCommand("Load Image Ref", "imgload", [selectedPath]);
  } else {
    deps.log("[renderer] menu:image-ref-load cancelled by user (dialog canceled)");
    deps.setStatus("Load Image Reference cancelled.");
  }
}

/**
 * Reference > Texture Assignment...
 *
 * Collects model index and optional mesh index via prompt, then opens native
 * texture file picker.
 */
export async function handleReferenceTextureAssign(deps: MenuHandlerDeps): Promise<void> {
  deps.log("[renderer] Menu event received: menu:reference-texture-assign");
  const idx = deps.promptInt("Texture Assign — Reference Model Index:");
  if (idx === null) return;
  const result = await deps.selectFile("texture-open");
  if (result.canceled || result.filePaths.length === 0) {
    deps.log("[renderer] menu:reference-texture-assign cancelled by user (dialog canceled)");
    deps.setStatus("Texture Assign cancelled.");
    return;
  }
  const texPath = result.filePaths[0];
  const meshIdx = deps.promptUser("Texture Assign — Mesh index (optional, blank for all):", "");
  const args = [String(idx), texPath];
  if (meshIdx) args.push(meshIdx);
  void deps.myraExecuteCommand("Assign Texture", "reftex", args);
}

/**
 * Reference > Emissive Texture Assignment...
 *
 * Collects model index and brightness via prompt, then opens native
 * texture file picker.
 */
export async function handleReferenceEmissiveAssign(deps: MenuHandlerDeps): Promise<void> {
  deps.log("[renderer] Menu event received: menu:reference-emissive-assign");
  const idx = deps.promptInt("Emissive Assign — Reference Model Index:");
  if (idx === null) return;
  const result = await deps.selectFile("texture-open");
  if (result.canceled || result.filePaths.length === 0) {
    deps.log("[renderer] menu:reference-emissive-assign cancelled by user (dialog canceled)");
    deps.setStatus("Emissive Texture Assign cancelled.");
    return;
  }
  const texPath = result.filePaths[0];
  const brightnessStr = deps.promptUser("Emissive Assign — Brightness (default 1.0):", "1.0");
  const brightness = parseFloat(brightnessStr ?? "1.0");
  if (isNaN(brightness)) {
    deps.setStatus("Emissive Assign: invalid brightness value.");
    return;
  }
  const meshIdx = deps.promptUser("Emissive Assign — Mesh index (optional):", "");
  const args = [String(idx), texPath, String(brightness)];
  if (meshIdx) args.push(meshIdx);
  void deps.myraExecuteCommand("Assign Emissive", "reftex-emissive", args);
}
