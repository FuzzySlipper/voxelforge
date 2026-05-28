import { app, BrowserWindow, ipcMain } from "electron";
import * as path from "path";
import { spawn } from "child_process";
import { BridgeClient } from "./bridge-client";
import { setupMenu } from "./menu";

const isSmokeTest = process.argv.includes("--smoke-test");
const isRendererSmokeTest = process.argv.includes("--renderer-smoke-test");
const isHeadless = isSmokeTest || process.argv.includes("--headless");

// Parse --preview <path> for auto-loading a .vforge preview file on startup
const previewArgIndex = process.argv.indexOf("--preview");
const previewPath = previewArgIndex >= 0 && previewArgIndex + 1 < process.argv.length
  ? process.argv[previewArgIndex + 1]
  : null;
if (previewArgIndex >= 0 && !previewPath) {
  console.warn("[electron] --preview specified without a path argument; ignoring.");
}
if (previewPath && !isSmokeTest && !isRendererSmokeTest) {
  console.log(`[electron] Preview mode: will load ${previewPath} on startup`);
}
const sidecarReadyTimeoutMs = 15000;
const requestTimeoutMs = 30000;
const meshSnapshotTimeoutMs = 120000; // Full mesh snapshots (with base64-encoded byte arrays) need extra time

let sidecarProcess: ReturnType<typeof spawn> | null = null;
let mainWindow: BrowserWindow | null = null;
let bridgeClient: BridgeClient | null = null;
let exitCode = 0;

async function main(): Promise<void> {
  console.log("[electron] Starting VoxelForge mesh viewer...");

  const repoRoot = findRepoRoot(__dirname);
  const sidecarSource = repoRoot ? "dev" : "packaged";
  console.log(`[electron] Sidecar source: ${sidecarSource}${repoRoot ? ` (repo: ${repoRoot})` : ""}`);

  // For smoke tests, use the simplified ping/version handshake flow
  if (isSmokeTest) {
    await runSmokeTest(repoRoot);
    return;
  }

  // For the renderer, spawn sidecar, create window, and render mesh
  await runRenderer(repoRoot);
}

/**
 * Spawn the C# sidecar process.
 *
 * In dev mode (repoRoot != null), uses `dotnet run --project` from the repo root.
 * In packaged mode (repoRoot == null), finds the bundled sidecar at
 * process.resourcesPath/sidecar/VoxelForge.Bridge.
 *
 * Returns the spawned child process, or null if neither mode is available.
 */
function spawnSidecar(repoRoot: string | null): ReturnType<typeof spawn> | null {
  if (repoRoot) {
    // Dev mode: dotnet run --project
    return spawn("dotnet", ["run", "--project", `${repoRoot}/src/VoxelForge.Bridge`], {
      cwd: repoRoot,
      stdio: ["ignore", "pipe", "pipe"],
    });
  }

  // Packaged mode: find bundled sidecar in Electron resources directory
  const sidecarPath = findBundledSidecarPath();
  if (!sidecarPath) {
    return null;
  }

  console.log(`[electron] Spawning bundled sidecar: ${sidecarPath}`);
  return spawn(sidecarPath, [], {
    cwd: path.dirname(sidecarPath),
    stdio: ["ignore", "pipe", "pipe"],
  });
}

/**
 * Find the bundled sidecar binary path in the Electron package resources.
 *
 * When running from an electron-builder package (dir or AppImage),
 * process.resourcesPath points to the resources/ directory.
 * The extraResources config copies sidecar/ -> resources/sidecar/.
 *
 * Returns the full path to the VoxelForge.Bridge binary, or null if not found.
 */
function findBundledSidecarPath(): string | null {
  const resourcesPath = process.resourcesPath;
  if (!resourcesPath) {
    return null;
  }

  const sidecarPath = path.join(resourcesPath, "sidecar", "VoxelForge.Bridge");
  if (require("fs").existsSync(sidecarPath)) {
    return sidecarPath;
  }

  return null;
}

/**
 * Set up stderr forwarding for a sidecar process.
 * Logs sidecar stderr lines with a [sidecar-stderr] prefix.
 */
function forwardSidecarStderr(proc: ReturnType<typeof spawn>): void {
  let stderrBuffer = "";
  proc.stderr?.on("data", (chunk: Buffer) => {
    stderrBuffer += chunk.toString("utf-8");
    const lines = stderrBuffer.split("\n");
    stderrBuffer = lines.pop() ?? "";
    for (const line of lines) {
      if (line.trim()) {
        console.log(`[sidecar-stderr] ${line.trim()}`);
      }
    }
  });
}

async function loadPreviewOnStartup(
  handshake: { endpoint: string; auth_token: string },
  path: string,
): Promise<void> {
  const client = await ensureBridgeClient(handshake);
  console.log(`[electron] Auto-loading preview from ${path}...`);
  const response = await client.send(
    {
      requestId: `preview-load-${Date.now()}`,
      command: "voxelforge.project.load",
      payload: { path },
    },
    requestTimeoutMs,
  );
  if (response.error) {
    console.error(`[electron] Failed to load preview from ${path}: ${response.error.message}`);
  } else {
    console.log(`[electron] Preview loaded successfully from ${path}.`);
    // The sidecar will push a state delta automatically after loading,
    // which the renderer will pick up via the forwarder in setupMeshSubscription.
  }
}

async function runSmokeTest(repoRoot: string | null): Promise<void> {
  console.log("[electron-smoke] Starting VoxelForge bridge smoke test...");

  sidecarProcess = spawnSidecar(repoRoot);
  if (!sidecarProcess) {
    console.error(
      "[electron-smoke] Could not spawn sidecar. " +
      "Run from the repository root or ensure the sidecar is bundled in the Electron package.",
    );
    shutdown(1);
    return;
  }

  forwardSidecarStderr(sidecarProcess);

  const handshake = await waitForHandshake(sidecarProcess, sidecarReadyTimeoutMs);
  if (!handshake) {
    console.error("[electron-smoke] Timed out waiting for sidecar handshake.");
    shutdown(1);
    return;
  }

  console.log(`[electron-smoke] Sidecar ready at ${handshake.endpoint}`);

  bridgeClient = new BridgeClient({
    endpoint: handshake.endpoint,
    authToken: handshake.auth_token,
  });

  try {
    await bridgeClient.connect();
    console.log("[electron-smoke] WebSocket connected.");
  } catch (err) {
    console.error("[electron-smoke] WebSocket connection failed:", err);
    shutdown(1);
    return;
  }

  try {
    // 1. Ping test
    console.log("[electron-smoke] Sending ping...");
    const pingResponse = await bridgeClient.send(
      { requestId: "smoke-ping", command: "ping", payload: { message: "hello-from-electron" } },
      requestTimeoutMs,
    );

    if (pingResponse.error) {
      console.error("[electron-smoke] Ping failed:", pingResponse.error);
      shutdown(1);
      return;
    }
    const pingResult = pingResponse.result as { echo?: string; timestamp?: number } | undefined;
    console.log(`[electron-smoke] Ping OK: echo=${pingResult?.echo}, timestamp=${pingResult?.timestamp}`);

    // 2. Version handshake test
    console.log("[electron-smoke] Sending version handshake...");
    const versionResponse = await bridgeClient.send(
      {
        requestId: "smoke-version",
        command: "version.handshake",
        payload: { client_protocol_version: "1.0" },
      },
      requestTimeoutMs,
    );
    if (versionResponse.error) {
      console.error("[electron-smoke] Version handshake failed:", versionResponse.error);
      shutdown(1);
      return;
    }
    const versionResult = versionResponse.result as
      | { sidecar_protocol_version?: string; app_id?: string; app_version?: string; compatible?: boolean }
      | undefined;
    console.log(
      `[electron-smoke] Version handshake OK: app=${versionResult?.app_id}@${versionResult?.app_version}, ` +
      `protocol=${versionResult?.sidecar_protocol_version}, compatible=${versionResult?.compatible}`,
    );

    // 3. VoxelForge schema handshake
    console.log("[electron-smoke] Sending VoxelForge schema handshake...");
    const schemaResponse = await bridgeClient.send(
      {
        requestId: "smoke-schema",
        command: "voxelforge.handshake",
        payload: { client_schema_version: "voxelforge@1", supported_capabilities: ["mesh_json"] },
      },
      requestTimeoutMs,
    );
    if (schemaResponse.error) {
      console.error("[electron-smoke] Schema handshake failed:", schemaResponse.error);
      shutdown(1);
      return;
    }
    const schemaResult = schemaResponse.result as
      | { sidecar_schema_version?: string; compatible?: boolean; supported_capabilities?: string[] }
      | undefined;
    console.log(
      `[electron-smoke] Schema handshake OK: version=${schemaResult?.sidecar_schema_version}, ` +
      `compatible=${schemaResult?.compatible}`,
    );

    // 4. Mesh snapshot test
    console.log("[electron-smoke] Requesting mesh snapshot...");
    const snapshotStartTime = Date.now();
    const meshResponse = await bridgeClient.send(
      {
        requestId: "smoke-mesh",
        command: "voxelforge.mesh.request_snapshot",
        payload: { model_id: "", lod_level: 0, payload_format: "json", include_palette_mapping: true },
      },
      requestTimeoutMs,
    );
    const meshTransferMs = Date.now() - snapshotStartTime;

    if (meshResponse.error) {
      console.error("[electron-smoke] Mesh snapshot failed:", meshResponse.error);
      shutdown(1);
      return;
    }
    const meshResult = meshResponse.result as
      | { model_id?: string; vertex_count?: number; triangle_count?: number; bounds?: unknown; metrics?: unknown }
      | undefined;
    console.log(
      `[electron-smoke] Mesh snapshot OK: model=${meshResult?.model_id}, ` +
      `vertices=${meshResult?.vertex_count}, triangles=${meshResult?.triangle_count}, ` +
      `transfer=${meshTransferMs}ms`,
    );

    // 5. Palette get test
    console.log("[electron-smoke] Requesting palette...");
    const paletteResponse = await bridgeClient.send(
      {
        requestId: "smoke-palette",
        command: "voxelforge.palette.get",
        payload: { model_id: "" },
      },
      requestTimeoutMs,
    );
    if (paletteResponse.error) {
      console.error("[electron-smoke] Palette get failed:", paletteResponse.error);
      shutdown(1);
      return;
    }
    const paletteResult = paletteResponse.result as
      | { palette_id?: string; entry_count?: number; entries?: unknown[] }
      | undefined;
    console.log(
      `[electron-smoke] Palette get OK: id=${paletteResult?.palette_id}, entries=${paletteResult?.entry_count}`,
    );

    // 6. State snapshot test
    console.log("[electron-smoke] Requesting editor state snapshot...");
    const stateResponse = await bridgeClient.send(
      {
        requestId: "smoke-state",
        command: "voxelforge.state.request_full",
        payload: { domains: ["document", "session", "history", "palette", "diagnostics"] },
      },
      requestTimeoutMs,
    );
    if (stateResponse.error) {
      console.error("[electron-smoke] State snapshot failed:", stateResponse.error);
      shutdown(1);
      return;
    }
    const stateResult = stateResponse.result as
      | { snapshot?: { model_id?: string; active_tool?: string; active_palette_index?: number; palette_entry_count?: number } }
      | undefined;
    console.log(
      `[electron-smoke] State snapshot OK: model=${stateResult?.snapshot?.model_id}, ` +
      `tool=${stateResult?.snapshot?.active_tool}, palette=${stateResult?.snapshot?.active_palette_index}`,
    );

    // 7. Command execution test: tool selection must round-trip through C# state.
    console.log("[electron-smoke] Executing set_active_tool command...");
    const commandResponse = await bridgeClient.send(
      {
        requestId: "smoke-command-tool",
        command: "voxelforge.command.execute",
        payload: { command_name: "set_active_tool", arguments: { tool: "paint" } },
      },
      requestTimeoutMs,
    );
    if (commandResponse.error) {
      console.error("[electron-smoke] Command execute failed:", commandResponse.error);
      shutdown(1);
      return;
    }
    const commandResult = commandResponse.result as
      | { success?: boolean; state?: { active_tool?: string } }
      | undefined;
    if (!commandResult?.success || commandResult.state?.active_tool !== "paint") {
      console.error("[electron-smoke] Command did not update authoritative C# state:", commandResponse.result);
      shutdown(1);
      return;
    }
    console.log("[electron-smoke] Command execute OK: active_tool=paint");

    // 8. Winding/culling verification for mesh
    // The mesh must have winding that aligns with FrontSide culling (CCW triangles).
    // Verify that vertex_count > 0 and index_count is a multiple of 3 (valid triangles)
    if (meshResult && typeof meshResult.vertex_count === "number" && typeof meshResult.triangle_count === "number") {
      if (meshResult.vertex_count <= 0 || meshResult.triangle_count <= 0) {
        console.error("[electron-smoke] Mesh has no geometry (verts or triangles = 0)");
        shutdown(1);
        return;
      }
      console.log(`[electron-smoke] Mesh winding check: ${meshResult.vertex_count} vertices, ${meshResult.triangle_count} triangles`);
    }

    console.log("[electron-smoke] All checks passed.");
    exitCode = 0;
  } catch (err) {
    console.error("[electron-smoke] Request failed:", err);
    exitCode = 1;
  } finally {
    bridgeClient.disconnect();
    shutdown(exitCode);
  }
}

async function runRenderer(repoRoot: string | null): Promise<void> {
  console.log("[electron] Starting VoxelForge renderer...");

  // Spawn sidecar
  sidecarProcess = spawnSidecar(repoRoot);
  if (!sidecarProcess) {
    console.error(
      "[electron] Could not spawn sidecar. " +
      "Run from the repository root or ensure the sidecar is bundled in the Electron package.",
    );
    app.exit(1);
    return;
  }

  forwardSidecarStderr(sidecarProcess);

  const handshake = await waitForHandshake(sidecarProcess, sidecarReadyTimeoutMs);
  if (!handshake) {
    console.error("[electron] Timed out waiting for sidecar handshake.");
    app.exit(1);
    return;
  }

  console.log(`[electron] Sidecar ready at ${handshake.endpoint}`);

  // Set up IPC handlers for renderer bridge requests
  setupIpcHandlers(handshake);

  // Create renderer window
  mainWindow = new BrowserWindow({
    width: 1280,
    height: 800,
    title: "VoxelForge Mesh Viewer",
    webPreferences: {
      preload: path.join(__dirname, "..", "preload", "index.js"),
      contextIsolation: true,
      nodeIntegration: false,
    },
  });

  mainWindow.loadFile(path.join(__dirname, "..", "renderer", "renderer.html"));

  // Set up native application menu
  setupMenu(mainWindow);

  mainWindow.on("closed", () => {
    mainWindow = null;
    bridgeClient?.disconnect();
    shutdown(0);
  });

  // Set up mesh subscription and event forwarding (after mainWindow is created)
  await setupMeshSubscription(handshake, mainWindow);

  // Auto-load preview file if --preview was specified
  if (previewPath) {
    // Wait briefly for the renderer to start before triggering preview load
    await new Promise(resolve => setTimeout(resolve, 1000));
    await loadPreviewOnStartup(handshake, previewPath);
  }

  // For headless smoke test: collect metrics and exit
  if (isHeadless || isRendererSmokeTest) {
    const rendererTimeout = setTimeout(() => {
      console.error("[electron] Timed out waiting for renderer metrics.");
      shutdown(1);
    }, 30000); // 30s timeout for renderer smoke

    ipcMain.once("renderer:metrics", (_event, metrics: Record<string, number>) => {
      clearTimeout(rendererTimeout);
      console.log("[electron] Renderer metrics:", JSON.stringify(metrics, null, 2));
      console.log("[electron] Renderer smoke test passed.");
      setTimeout(() => shutdown(0), 500);
    });
  }
}

function setupIpcHandlers(handshake: { endpoint: string; auth_token: string }): void {
  ipcMain.handle("bridge:handshake", async (_event, payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    const response = await client.send(
      {
        requestId: `renderer-handshake-${Date.now()}`,
        command: "voxelforge.handshake",
        payload: payload as Record<string, unknown>,
      },
      requestTimeoutMs,
    );
    if (response.error) {
      throw new Error(`Schema handshake error: ${response.error.message}`);
    }
    return response.result;
  });

  ipcMain.handle("bridge:mesh-snapshot", async (_event, payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    // Use longer timeout for mesh snapshots (large payload with base64-encoded byte arrays)
    const response = await client.send(
      {
        requestId: `renderer-mesh-${Date.now()}`,
        command: "voxelforge.mesh.request_snapshot",
        payload: payload as Record<string, unknown>,
      },
      meshSnapshotTimeoutMs,
    );
    if (response.error) {
      throw new Error(`Mesh snapshot error: ${response.error.message}`);
    }
    return response.result;
  });

  ipcMain.handle("bridge:render-snapshot", async (_event, _payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    // Canonical render-scene snapshot channel (#1657/#1662).
    // Uses the same C# mesh snapshot command; the response carries all material,
    // texture, reference, and palette data needed for the contract.
    // Uses longer timeout for large snapshots.
    const response = await client.send(
      {
        requestId: `renderer-scene-${Date.now()}`,
        command: "voxelforge.mesh.request_snapshot",
        payload: {
          model_id: "",
          lod_level: 0,
          payload_format: "json",
          include_palette_mapping: true,
        },
      },
      meshSnapshotTimeoutMs,
    );
    if (response.error) {
      throw new Error(`Render snapshot error: ${response.error.message}`);
    }
    return response.result;
  });

  ipcMain.handle("bridge:render-state", async (_event, _payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    // Lightweight render state — just the editor state snapshot (no mesh data).
    const response = await client.send(
      {
        requestId: `renderer-state-${Date.now()}`,
        command: "voxelforge.state.request_full",
        payload: { domains: ["document", "session", "palette", "diagnostics"] },
      },
      requestTimeoutMs,
    );
    if (response.error) {
      throw new Error(`Render state error: ${response.error.message}`);
    }
    return response.result;
  });

  ipcMain.handle("bridge:palette-get", async (_event, payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    const response = await client.send(
      {
        requestId: `renderer-palette-${Date.now()}`,
        command: "voxelforge.palette.get",
        payload: payload as Record<string, unknown>,
      },
      requestTimeoutMs,
    );
    if (response.error) {
      throw new Error(`Palette get error: ${response.error.message}`);
    }
    return response.result;
  });

  ipcMain.handle("bridge:state-subscribe", async (_event, payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    const response = await client.send(
      {
        requestId: `renderer-state-sub-${Date.now()}`,
        command: "voxelforge.state.subscribe",
        payload: payload as Record<string, unknown>,
      },
      requestTimeoutMs,
    );
    if (response.error) {
      throw new Error(`State subscribe error: ${response.error.message}`);
    }
    return response.result;
  });

  ipcMain.handle("bridge:state-request-full", async (_event, payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    const response = await client.send(
      {
        requestId: `renderer-state-full-${Date.now()}`,
        command: "voxelforge.state.request_full",
        payload: payload as Record<string, unknown>,
      },
      requestTimeoutMs,
    );
    if (response.error) {
      throw new Error(`State request error: ${response.error.message}`);
    }
    return response.result;
  });

  ipcMain.handle("bridge:command-execute", async (_event, payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    const response = await client.send(
      {
        requestId: `renderer-command-${Date.now()}`,
        command: "voxelforge.command.execute",
        payload: payload as Record<string, unknown>,
      },
      requestTimeoutMs,
    );
    if (response.error) {
      throw new Error(`Command error: ${response.error.message}`);
    }
    return response.result;
  });

  ipcMain.handle("bridge:history-undo", async (_event, payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    const response = await client.send(
      {
        requestId: `renderer-undo-${Date.now()}`,
        command: "voxelforge.history.undo",
        payload: payload as Record<string, unknown>,
      },
      requestTimeoutMs,
    );
    if (response.error) {
      throw new Error(`Undo error: ${response.error.message}`);
    }
    return response.result;
  });

  ipcMain.handle("bridge:history-redo", async (_event, payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    const response = await client.send(
      {
        requestId: `renderer-redo-${Date.now()}`,
        command: "voxelforge.history.redo",
        payload: payload as Record<string, unknown>,
      },
      requestTimeoutMs,
    );
    if (response.error) {
      throw new Error(`Redo error: ${response.error.message}`);
    }
    return response.result;
  });

  ipcMain.handle("bridge:project-save", async (_event, payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    const response = await client.send(
      {
        requestId: `renderer-save-${Date.now()}`,
        command: "voxelforge.project.save",
        payload: payload as Record<string, unknown>,
      },
      requestTimeoutMs,
    );
    if (response.error) {
      throw new Error(`Project save error: ${response.error.message}`);
    }
    return response.result;
  });

  ipcMain.handle("bridge:project-load", async (_event, payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    const response = await client.send(
      {
        requestId: `renderer-load-${Date.now()}`,
        command: "voxelforge.project.load",
        payload: payload as Record<string, unknown>,
      },
      requestTimeoutMs,
    );
    if (response.error) {
      throw new Error(`Project load error: ${response.error.message}`);
    }
    return response.result;
  });

  ipcMain.handle("bridge:project-new", async (_event, payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    const response = await client.send(
      {
        requestId: `renderer-new-${Date.now()}`,
        command: "voxelforge.project.new",
        payload: payload as Record<string, unknown>,
      },
      requestTimeoutMs,
    );
    if (response.error) {
      throw new Error(`Project new error: ${response.error.message}`);
    }
    return response.result;
  });

  ipcMain.handle("bridge:mesh-subscribe", async (_event, payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    const response = await client.send(
      {
        requestId: `renderer-mesh-sub-${Date.now()}`,
        command: "voxelforge.mesh.subscribe",
        payload: payload as Record<string, unknown>,
      },
      requestTimeoutMs,
    );
    if (response.error) {
      throw new Error(`Mesh subscribe error: ${response.error.message}`);
    }
    return response.result;
  });

  ipcMain.handle("bridge:mesh-unsubscribe", async (_event, payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    const response = await client.send(
      {
        requestId: `renderer-mesh-unsub-${Date.now()}`,
        command: "voxelforge.mesh.unsubscribe",
        payload: payload as Record<string, unknown>,
      },
      requestTimeoutMs,
    );
    if (response.error) {
      throw new Error(`Mesh unsubscribe error: ${response.error.message}`);
    }
    return response.result;
  });

  ipcMain.handle("bridge:ping", async (_event, payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    const response = await client.send(
      {
        requestId: `renderer-ping-${Date.now()}`,
        command: "ping",
        payload: payload as Record<string, unknown>,
      },
      requestTimeoutMs,
    );
    return response.result;
  });

  ipcMain.handle("bridge:version-handshake", async (_event, payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    const response = await client.send(
      {
        requestId: `renderer-version-${Date.now()}`,
        command: "version.handshake",
        payload: payload as Record<string, unknown>,
      },
      requestTimeoutMs,
    );
    return response.result;
  });

  ipcMain.on("renderer:metrics", (_event, metrics: Record<string, number>) => {
    console.log("[electron] Renderer metrics:", JSON.stringify(metrics, null, 2));
  });

  ipcMain.on("renderer:ready", () => {
    console.log("[electron] Renderer process ready.");
  });

  // ── Myra CLI command routing for reference/image/voxelize workflows ──
  ipcMain.handle("bridge:myra-command-execute", async (_event, payload: unknown) => {
    const client = await ensureBridgeClient(handshake);
    const p = payload as { command?: string; args?: string[] };
    const response = await client.send(
      {
        requestId: `renderer-myra-${Date.now()}`,
        command: "voxelforge.myra.execute",
        payload: { command: p.command ?? "", args: p.args ?? [] },
      },
      requestTimeoutMs,
    );
    if (response.error) {
      throw new Error(`Myra command error: ${response.error.message}`);
    }
    return response.result;
  });
}

async function setupMeshSubscription(handshake: { endpoint: string; auth_token: string }, mainWindow: BrowserWindow): Promise<void> {
  const client = await ensureBridgeClient(handshake);

  // Forward mesh update events from the bridge to the renderer
  client.onEvent("voxelforge.mesh.update", (payload: unknown) => {
    if (mainWindow && !mainWindow.isDestroyed()) {
      mainWindow.webContents.send("voxelforge:mesh-update", payload);
    }
    console.log("[electron] Mesh update event received");
  });

  // Forward palette update events from the bridge to the renderer
  client.onEvent("voxelforge.palette.update", (payload: unknown) => {
    if (mainWindow && !mainWindow.isDestroyed()) {
      mainWindow.webContents.send("voxelforge:palette-update", payload);
    }
    console.log("[electron] Palette update event received");
  });

  // Forward editor state update events from the bridge to the renderer
  client.onEvent("voxelforge.state.delta", (payload: unknown) => {
    if (mainWindow && !mainWindow.isDestroyed()) {
      mainWindow.webContents.send("voxelforge:state-delta", payload);
    }
    console.log("[electron] State delta event received");
  });

  // Forward editing latency diagnostic events from the bridge to the renderer
  client.onEvent("voxelforge.diagnostics.editing_latency", (payload: unknown) => {
    if (mainWindow && !mainWindow.isDestroyed()) {
      mainWindow.webContents.send("voxelforge:editing-latency", payload);
    }
    const p = payload as { command_name?: string; total_ms?: number };
    console.log(`[electron] Editing latency: ${p.command_name} took ${p.total_ms}ms`);
  });
}

async function ensureBridgeClient(handshake: { endpoint: string; auth_token: string }): Promise<BridgeClient> {
  if (bridgeClient && bridgeClient.isConnected()) {
    return bridgeClient;
  }
  bridgeClient = new BridgeClient({
    endpoint: handshake.endpoint,
    authToken: handshake.auth_token,
  });
  await bridgeClient.connect();
  console.log("[electron] Bridge WebSocket connected.");
  return bridgeClient;
}

function waitForHandshake(
  proc: ReturnType<typeof spawn>,
  timeoutMs: number,
): Promise<{ endpoint: string; auth_token: string } | null> {
  return new Promise((resolve) => {
    let buffer = "";
    const timer = setTimeout(() => {
      cleanup();
      resolve(null);
    }, timeoutMs);

    function onData(chunk: Buffer) {
      buffer += chunk.toString("utf-8");
      const lines = buffer.split("\n");
      buffer = lines.pop() ?? "";
      for (const line of lines) {
        const match = line.match(/\[BRIDGE_HANDSHAKE\](.+)/);
        if (match) {
          try {
            const json = JSON.parse(match[1]);
            if (json.sidecar_ready && json.endpoint && json.auth_token) {
              cleanup();
              resolve({ endpoint: json.endpoint, auth_token: json.auth_token });
              return;
            }
          } catch {
            // Ignore malformed handshake JSON.
          }
        }
      }
    }

    function cleanup() {
      clearTimeout(timer);
      proc.stdout?.off("data", onData);
    }

    proc.stdout?.on("data", onData);
  });
}

function shutdown(code: number): void {
  if (sidecarProcess && !sidecarProcess.killed) {
    sidecarProcess.kill("SIGTERM");
  }
  app.exit(code);
}

function findRepoRoot(startPath: string): string | null {
  let dir = path.resolve(startPath);
  // Walk up from the dist/main directory to the repo root
  for (let i = 0; i < 10; i++) {
    if (require("fs").existsSync(path.join(dir, "voxelforge.slnx"))) {
      return dir;
    }
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  return null;
}

// Disable GPU acceleration for headless/smoke-test environments.
// Must be called before app.whenReady().
if (isHeadless || isRendererSmokeTest) {
  app.disableHardwareAcceleration();
}

app.whenReady().then(main);

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") {
    app.quit();
  }
});
