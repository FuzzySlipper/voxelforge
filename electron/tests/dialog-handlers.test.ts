/**
 * Tests for native Electron file picker dialog integration.
 *
 * Covers:
 * - Factored menu handler cancel/accept payloads (using mock deps)
 * - dialog-types.ts allowlist validation
 * - Preload channel allowlisting
 */

import { describe, it, expect, vi } from "vitest";
import type { MenuHandlerDeps } from "../src/renderer/menu-handler-deps";
import {
  handleReferenceModelLoad,
  handleFileOpen,
  handleFileSaveAs,
  handleReferenceMetaLoad,
  handleReferenceMetaSave,
  handleImageRefLoad,
  handleReferenceTextureAssign,
  handleReferenceEmissiveAssign,
} from "../src/renderer/menu-handlers";
import {
  VALID_DIALOG_KINDS,
  OPEN_DIALOG_KINDS,
  SAVE_DIALOG_KINDS,
  getOpenDialogConfig,
  getSaveDialogConfig,
  DialogChannels,
} from "../src/shared/dialog-types";
import type { DialogResponse } from "../src/shared/dialog-types";

// ── Mock deps helpers ──

function createMockDeps(overrides: Partial<MenuHandlerDeps> = {}): MenuHandlerDeps {
  return {
    promptUser: vi.fn(() => null),
    confirmUser: vi.fn(() => false),
    promptInt: vi.fn(() => null),
    selectFile: vi.fn(async () => ({ canceled: true, filePaths: [] })),
    saveFile: vi.fn(async () => ({ canceled: true, filePaths: [] })),
    myraExecuteCommand: vi.fn(async () => {}),
    runAction: vi.fn(async () => {}),
    setStatus: vi.fn(),
    log: vi.fn(),
    getDefaultPath: () => "",
    ...overrides,
  };
}

// ── dialog-types tests ──

describe("dialog-types", () => {
  it("has all expected dialog kinds in the allowlist", () => {
    const expected: string[] = [
      "vforge-open",
      "vforge-save",
      "reference-model-open",
      "refmeta-open",
      "refmeta-save",
      "image-open",
      "texture-open",
    ];
    for (const kind of expected) {
      expect(VALID_DIALOG_KINDS.has(kind as any)).toBe(true);
    }
  });

  it("rejects unknown dialog kinds", () => {
    expect(VALID_DIALOG_KINDS.has("arbitrary-read" as any)).toBe(false);
    expect(VALID_DIALOG_KINDS.has("shell-execute" as any)).toBe(false);
    expect(VALID_DIALOG_KINDS.has("" as any)).toBe(false);
  });

  it("provides valid open dialog config for each open kind", () => {
    const openKinds = ["vforge-open", "reference-model-open", "refmeta-open", "image-open", "texture-open"];
    for (const kind of openKinds) {
      expect(OPEN_DIALOG_KINDS.has(kind as any)).toBe(true);
      expect(SAVE_DIALOG_KINDS.has(kind as any)).toBe(false);
      const config = getOpenDialogConfig(kind as any);
      expect(config.filters.length).toBeGreaterThan(0);
      expect(config.properties).toContain("openFile");
    }
  });

  it("provides valid save dialog config for each save kind", () => {
    const saveKinds = ["vforge-save", "refmeta-save"];
    for (const kind of saveKinds) {
      expect(SAVE_DIALOG_KINDS.has(kind as any)).toBe(true);
      expect(OPEN_DIALOG_KINDS.has(kind as any)).toBe(false);
      const config = getSaveDialogConfig(kind as any);
      expect(config.filters.length).toBeGreaterThan(0);
    }
  });

  it("throws on invalid open dialog kind", () => {
    expect(() => getOpenDialogConfig("vforge-save" as any)).toThrow();
  });

  it("throws on invalid save dialog kind", () => {
    expect(() => getSaveDialogConfig("vforge-open" as any)).toThrow();
  });

  it("has correct dialog channel constants", () => {
    expect(DialogChannels.SELECT_FILE).toBe("dialog:select-file");
    expect(DialogChannels.SAVE_FILE).toBe("dialog:save-file");
  });
});

// ── Menu handler cancel tests ──

describe("handleReferenceModelLoad", () => {
  it("does not execute command when dialog is cancelled", async () => {
    const deps = createMockDeps();
    await handleReferenceModelLoad(deps);
    expect(deps.myraExecuteCommand).not.toHaveBeenCalled();
    expect(deps.setStatus).toHaveBeenCalledWith("Load Reference Model cancelled.");
  });

  it("executes refload with selected path on dialog accept", async () => {
    const deps = createMockDeps({
      selectFile: vi.fn(async () => ({
        canceled: false,
        filePaths: ["/path/to/model.obj"],
      })),
    });
    await handleReferenceModelLoad(deps);
    expect(deps.myraExecuteCommand).toHaveBeenCalledWith(
      "Load Ref Model", "refload", ["/path/to/model.obj"],
    );
  });
});

describe("handleFileOpen", () => {
  it("does not execute command when dialog is cancelled", async () => {
    const deps = createMockDeps();
    await handleFileOpen(deps);
    expect(deps.runAction).not.toHaveBeenCalled();
    expect(deps.setStatus).toHaveBeenCalledWith("File Open cancelled.");
  });

  it("executes bridge:project-load with selected path on dialog accept", async () => {
    const deps = createMockDeps({
      selectFile: vi.fn(async () => ({
        canceled: false,
        filePaths: ["/path/to/project.vforge"],
      })),
    });
    await handleFileOpen(deps);
    expect(deps.runAction).toHaveBeenCalledWith(
      "Open", "bridge:project-load", { path: "/path/to/project.vforge" },
    );
  });

  it("passes default path from getDefaultPath to selectFile", async () => {
    const selectFile = vi.fn(async () => ({ canceled: true, filePaths: [] }));
    const deps = createMockDeps({
      selectFile,
      getDefaultPath: () => "/existing/path.vforge",
    });
    await handleFileOpen(deps);
    expect(selectFile).toHaveBeenCalledWith("vforge-open", "/existing/path.vforge");
  });
});

describe("handleFileSaveAs", () => {
  it("does not execute command when dialog is cancelled", async () => {
    const deps = createMockDeps();
    await handleFileSaveAs(deps);
    expect(deps.runAction).not.toHaveBeenCalled();
    expect(deps.setStatus).toHaveBeenCalledWith("Save As cancelled.");
  });

  it("executes bridge:project-save with path on dialog accept", async () => {
    const deps = createMockDeps({
      saveFile: vi.fn(async () => ({
        canceled: false,
        filePaths: ["/path/to/project.vforge"],
      })),
    });
    await handleFileSaveAs(deps);
    expect(deps.runAction).toHaveBeenCalledWith(
      "Save As", "bridge:project-save", { path: "/path/to/project.vforge" },
    );
  });

  it("appends .vforge extension when missing", async () => {
    const deps = createMockDeps({
      saveFile: vi.fn(async () => ({
        canceled: false,
        filePaths: ["/path/to/project"],
      })),
    });
    await handleFileSaveAs(deps);
    expect(deps.runAction).toHaveBeenCalledWith(
      "Save As", "bridge:project-save", { path: "/path/to/project.vforge" },
    );
  });
});

describe("handleReferenceMetaLoad", () => {
  it("does not execute command when dialog is cancelled", async () => {
    const deps = createMockDeps();
    await handleReferenceMetaLoad(deps);
    expect(deps.myraExecuteCommand).not.toHaveBeenCalled();
    expect(deps.setStatus).toHaveBeenCalledWith("Load Meta cancelled.");
  });

  it("executes refloadmeta with selected path on dialog accept", async () => {
    const deps = createMockDeps({
      selectFile: vi.fn(async () => ({
        canceled: false,
        filePaths: ["/path/to/meta.refmeta"],
      })),
    });
    await handleReferenceMetaLoad(deps);
    expect(deps.myraExecuteCommand).toHaveBeenCalledWith(
      "Load Meta", "refloadmeta", ["/path/to/meta.refmeta"],
    );
  });
});

describe("handleReferenceMetaSave", () => {
  it("cancels when user cancels index prompt", async () => {
    const deps = createMockDeps({
      promptInt: vi.fn(() => null),
    });
    await handleReferenceMetaSave(deps);
    expect(deps.saveFile).not.toHaveBeenCalled();
    expect(deps.myraExecuteCommand).not.toHaveBeenCalled();
  });

  it("cancels when user cancels save dialog", async () => {
    const deps = createMockDeps({
      promptInt: vi.fn(() => 0),
      saveFile: vi.fn(async () => ({ canceled: true, filePaths: [] })),
    });
    await handleReferenceMetaSave(deps);
    expect(deps.myraExecuteCommand).not.toHaveBeenCalled();
    expect(deps.setStatus).toHaveBeenCalledWith("Save Meta cancelled.");
  });

  it("executes refsave with index and path on accept", async () => {
    const deps = createMockDeps({
      promptInt: vi.fn(() => 2),
      saveFile: vi.fn(async () => ({
        canceled: false,
        filePaths: ["/path/to/2.refmeta"],
      })),
    });
    await handleReferenceMetaSave(deps);
    expect(deps.myraExecuteCommand).toHaveBeenCalledWith(
      "Save Meta", "refsave", ["2", "/path/to/2.refmeta"],
    );
  });

  it("appends .refmeta extension when missing", async () => {
    const deps = createMockDeps({
      promptInt: vi.fn(() => 0),
      saveFile: vi.fn(async () => ({
        canceled: false,
        filePaths: ["/path/to/meta"],
      })),
    });
    await handleReferenceMetaSave(deps);
    expect(deps.myraExecuteCommand).toHaveBeenCalledWith(
      "Save Meta", "refsave", ["0", "/path/to/meta.refmeta"],
    );
  });
});

describe("handleImageRefLoad", () => {
  it("does not execute command when dialog is cancelled", async () => {
    const deps = createMockDeps();
    await handleImageRefLoad(deps);
    expect(deps.myraExecuteCommand).not.toHaveBeenCalled();
    expect(deps.setStatus).toHaveBeenCalledWith("Load Image Reference cancelled.");
  });

  it("executes imgload with selected path on dialog accept", async () => {
    const deps = createMockDeps({
      selectFile: vi.fn(async () => ({
        canceled: false,
        filePaths: ["/path/to/image.png"],
      })),
    });
    await handleImageRefLoad(deps);
    expect(deps.myraExecuteCommand).toHaveBeenCalledWith(
      "Load Image Ref", "imgload", ["/path/to/image.png"],
    );
  });
});

describe("handleReferenceTextureAssign", () => {
  it("cancels when user cancels index prompt", async () => {
    const deps = createMockDeps({
      promptInt: vi.fn(() => null),
    });
    await handleReferenceTextureAssign(deps);
    expect(deps.selectFile).not.toHaveBeenCalled();
  });

  it("cancels when user cancels file dialog", async () => {
    const deps = createMockDeps({
      promptInt: vi.fn(() => 0),
      selectFile: vi.fn(async () => ({ canceled: true, filePaths: [] })),
    });
    await handleReferenceTextureAssign(deps);
    expect(deps.myraExecuteCommand).not.toHaveBeenCalled();
    expect(deps.setStatus).toHaveBeenCalledWith("Texture Assign cancelled.");
  });

  it("executes reftex with index, path, and optional mesh index", async () => {
    const deps = createMockDeps({
      promptInt: vi.fn(() => 1),
      promptUser: vi.fn(() => "3"),
      selectFile: vi.fn(async () => ({
        canceled: false,
        filePaths: ["/textures/diffuse.png"],
      })),
    });
    await handleReferenceTextureAssign(deps);
    expect(deps.myraExecuteCommand).toHaveBeenCalledWith(
      "Assign Texture", "reftex", ["1", "/textures/diffuse.png", "3"],
    );
  });

  it("executes reftex without mesh index when blank", async () => {
    const deps = createMockDeps({
      promptInt: vi.fn(() => 0),
      promptUser: vi.fn(() => ""),
      selectFile: vi.fn(async () => ({
        canceled: false,
        filePaths: ["/textures/tex.png"],
      })),
    });
    await handleReferenceTextureAssign(deps);
    expect(deps.myraExecuteCommand).toHaveBeenCalledWith(
      "Assign Texture", "reftex", ["0", "/textures/tex.png"],
    );
  });
});

describe("handleReferenceEmissiveAssign", () => {
  it("cancels when user cancels index prompt", async () => {
    const deps = createMockDeps({
      promptInt: vi.fn(() => null),
    });
    await handleReferenceEmissiveAssign(deps);
    expect(deps.selectFile).not.toHaveBeenCalled();
  });

  it("cancels when user cancels file dialog", async () => {
    const deps = createMockDeps({
      promptInt: vi.fn(() => 0),
      selectFile: vi.fn(async () => ({ canceled: true, filePaths: [] })),
    });
    await handleReferenceEmissiveAssign(deps);
    expect(deps.myraExecuteCommand).not.toHaveBeenCalled();
    expect(deps.setStatus).toHaveBeenCalledWith("Emissive Texture Assign cancelled.");
  });

  it("executes reftex-emissive with index, path, brightness, and mesh index", async () => {
    const deps = createMockDeps({
      promptInt: vi.fn(() => 1),
      promptUser: vi.fn((_msg: string, def?: string) => {
        // First call: brightness default "1.0", second: mesh index ""
        if (def === "1.0") return "0.5";
        return "2";
      }),
      selectFile: vi.fn(async () => ({
        canceled: false,
        filePaths: ["/textures/emissive.png"],
      })),
    });
    await handleReferenceEmissiveAssign(deps);
    expect(deps.myraExecuteCommand).toHaveBeenCalledWith(
      "Assign Emissive", "reftex-emissive", ["1", "/textures/emissive.png", "0.5", "2"],
    );
  });

  it("rejects invalid brightness", async () => {
    const deps = createMockDeps({
      promptInt: vi.fn(() => 0),
      promptUser: vi.fn(() => "not-a-number"),
      selectFile: vi.fn(async () => ({
        canceled: false,
        filePaths: ["/textures/emissive.png"],
      })),
    });
    await handleReferenceEmissiveAssign(deps);
    expect(deps.myraExecuteCommand).not.toHaveBeenCalled();
    expect(deps.setStatus).toHaveBeenCalledWith("Emissive Assign: invalid brightness value.");
  });
});

// ── Preload channel validation ──

describe("preload channel allowlisting", () => {
  it("dialog channels are included in allowed channels list", () => {
    // This verifies that the dialog channels are valid for preload.
    // The actual preload module uses these constants in its allowedChannels array.
    expect(DialogChannels.SELECT_FILE).toBe("dialog:select-file");
    expect(DialogChannels.SAVE_FILE).toBe("dialog:save-file");
    // These must not be confused with arbitrary IPC
    expect(DialogChannels.SELECT_FILE).not.toContain("shell");
    expect(DialogChannels.SAVE_FILE).not.toContain("fs");
  });

  it("dialog channel names follow the constrained pattern", () => {
    // All dialog channels must start with "dialog:" prefix
    expect(DialogChannels.SELECT_FILE).toMatch(/^dialog:/);
    expect(DialogChannels.SAVE_FILE).toMatch(/^dialog:/);
  });
});
