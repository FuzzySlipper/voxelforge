/**
 * menu-handlers.test.ts — Deterministic tests for factored renderer menu handlers.
 *
 * Tests the actual handler functions from menu-handlers.ts with mocked
 * dependencies, verifying the exact bridge request payloads without needing
 * a full Electron/DOM environment.
 */

import { describe, it, expect, vi } from "vitest";
import {
  handleReferenceModelLoad,
  handleReferenceModelList,
  handleReferenceModelRemove,
  handleFileNew,
  handleFileOpen,
} from "../src/renderer/menu-handlers";
import type { MenuHandlerDeps } from "../src/renderer/menu-handler-deps";

// ── Helpers ──

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
    getDefaultPath: vi.fn(() => ""),
    ...overrides,
  };
}

// ── Acceptance gap: Reference > Load Reference Model... ──

describe("handleReferenceModelLoad", () => {
  it("sends bridge:myra-command-execute with command='refload' and selected path", async () => {
    const myraExecuteCommand = vi.fn(async () => {});
    const deps = createMockDeps({
      selectFile: vi.fn(async () => ({
        canceled: false,
        filePaths: ["/home/models/test-reference.obj"],
      })),
      myraExecuteCommand,
    });

    await handleReferenceModelLoad(deps);

    expect(myraExecuteCommand).toHaveBeenCalledWith(
      "Load Ref Model",
      "refload",
      ["/home/models/test-reference.obj"],
    );
    expect(deps.log).toHaveBeenCalledWith(
      expect.stringContaining("Selected reference model path"),
    );
  });

  it("does NOT call myraExecuteCommand when dialog is cancelled", async () => {
    const myraExecuteCommand = vi.fn(async () => {});
    const setStatus = vi.fn();
    const deps = createMockDeps({
      selectFile: vi.fn(async () => ({ canceled: true, filePaths: [] })),
      myraExecuteCommand,
      setStatus,
    });

    await handleReferenceModelLoad(deps);

    expect(myraExecuteCommand).not.toHaveBeenCalled();
    expect(setStatus).toHaveBeenCalledWith(
      expect.stringContaining("cancelled"),
    );
    expect(deps.log).toHaveBeenCalledWith(
      expect.stringContaining("cancelled"),
    );
  });

  it("does NOT call myraExecuteCommand when dialog returns empty file paths", async () => {
    const myraExecuteCommand = vi.fn(async () => {});
    const setStatus = vi.fn();
    const deps = createMockDeps({
      selectFile: vi.fn(async () => ({ canceled: false, filePaths: [] })),
      myraExecuteCommand,
      setStatus,
    });

    await handleReferenceModelLoad(deps);

    expect(myraExecuteCommand).not.toHaveBeenCalled();
  });
});

// ── Reference Model List ──

describe("handleReferenceModelList", () => {
  it("sends bridge:myra-command-execute with command='reflist' and no args", () => {
    const myraExecuteCommand = vi.fn(async () => {});
    const deps = createMockDeps({ myraExecuteCommand });

    handleReferenceModelList(deps);

    expect(myraExecuteCommand).toHaveBeenCalledWith(
      "List Ref Models",
      "reflist",
      [],
    );
  });
});

// ── Reference Model Remove ──

describe("handleReferenceModelRemove", () => {
  it("sends bridge:myra-command-execute with command='refremove' and index when accepted", () => {
    const myraExecuteCommand = vi.fn(async () => {});
    const deps = createMockDeps({
      promptInt: () => 3,
      myraExecuteCommand,
    });

    handleReferenceModelRemove(deps);

    expect(myraExecuteCommand).toHaveBeenCalledWith(
      "Remove Ref Model",
      "refremove",
      ["3"],
    );
  });

  it("does NOT call myraExecuteCommand when cancelled", () => {
    const myraExecuteCommand = vi.fn(async () => {});
    const deps = createMockDeps({
      promptInt: () => null,
      myraExecuteCommand,
    });

    handleReferenceModelRemove(deps);

    expect(myraExecuteCommand).not.toHaveBeenCalled();
  });
});

// ── File > New ──

describe("handleFileNew", () => {
  it("sends bridge:project-new when confirmed", () => {
    const runAction = vi.fn(async () => {});
    const deps = createMockDeps({
      confirmUser: () => true,
      runAction,
    });

    handleFileNew(deps);

    expect(runAction).toHaveBeenCalledWith(
      "New Project",
      "bridge:project-new",
      {},
    );
  });

  it("does NOT call runAction when cancelled", () => {
    const runAction = vi.fn(async () => {});
    const deps = createMockDeps({
      confirmUser: () => false,
      runAction,
    });

    handleFileNew(deps);

    expect(runAction).not.toHaveBeenCalled();
    expect(deps.log).toHaveBeenCalledWith(
      expect.stringContaining("cancelled"),
    );
  });
});

// ── File > Open... ──

describe("handleFileOpen", () => {
  it("sends bridge:project-load with selected path from dialog", async () => {
    const runAction = vi.fn(async () => {});
    const deps = createMockDeps({
      selectFile: vi.fn(async () => ({
        canceled: false,
        filePaths: ["/path/to/project.vforge"],
      })),
      runAction,
      getDefaultPath: () => "/current/path.vforge",
    });

    await handleFileOpen(deps);

    expect(runAction).toHaveBeenCalledWith(
      "Open",
      "bridge:project-load",
      { path: "/path/to/project.vforge" },
    );
  });

  it("does NOT call runAction when dialog is cancelled", async () => {
    const runAction = vi.fn(async () => {});
    const deps = createMockDeps({
      selectFile: vi.fn(async () => ({ canceled: true, filePaths: [] })),
      runAction,
    });

    await handleFileOpen(deps);

    expect(runAction).not.toHaveBeenCalled();
  });
});
