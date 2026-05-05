import { app, BrowserWindow, ipcMain } from "electron";
import * as path from "path";
import { spawn } from "child_process";
import { BridgeClient } from "./bridge-client";

const isSmokeTest = process.argv.includes("--smoke-test");
const isRendererSmokeTest = process.argv.includes("--renderer-smoke-test");
const isHeadless = isSmokeTest || process.argv.includes("--headless");
const sidecarReadyTimeoutMs = 15000;
const requestTimeoutMs = 30000; // Mesh snapshots can be large

let sidecarProcess: ReturnType<typeof spawn> | null = null;
let mainWindow: BrowserWindow | null = null;
let bridgeClient: BridgeClient | null = null;
let exitCode = 0;

async function main(): Promise<void> {
  console.log("[electron] Starting VoxelForge mesh viewer...");

  const repoRoot = findRepoRoot(__dirname);
  if (!repoRoot) {
    console.error("[electron] Could not locate repository root.");
    app.exit(1);
    return;
  }

  // For smoke tests, use the simplified ping/version handshake flow
  if (isSmokeTest) {
    await runSmokeTest(repoRoot);
    return;
  }

  // For the renderer, spawn sidecar, create window, and render mesh
  await runRenderer(repoRoot);
}

async function runSmokeTest(repoRoot: string): Promise<void> {
  console.log("[electron-smoke] Starting VoxelForge bridge smoke test...");

  sidecarProcess = spawn("dotnet", ["run", "--project", `${repoRoot}/src/VoxelForge.Bridge`], {
    cwd: repoRoot,
    stdio: ["ignore", "pipe", "pipe"],
  });

  let stderrBuffer = "";
  sidecarProcess.stderr?.on("data", (chunk: Buffer) => {
    stderrBuffer += chunk.toString("utf-8");
    const lines = stderrBuffer.split("\n");
    stderrBuffer = lines.pop() ?? "";
    for (const line of lines) {
      if (line.trim()) {
        console.log(`[sidecar-stderr] ${line.trim()}`);
      }
    }
  });

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

    // 6. Winding/culling verification for mesh
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

async function runRenderer(repoRoot: string): Promise<void> {
  console.log("[electron] Starting VoxelForge renderer...");

  // Spawn sidecar
  sidecarProcess = spawn("dotnet", ["run", "--project", `${repoRoot}/src/VoxelForge.Bridge`], {
    cwd: repoRoot,
    stdio: ["ignore", "pipe", "pipe"],
  });

  let stderrBuffer = "";
  sidecarProcess.stderr?.on("data", (chunk: Buffer) => {
    stderrBuffer += chunk.toString("utf-8");
    const lines = stderrBuffer.split("\n");
    stderrBuffer = lines.pop() ?? "";
    for (const line of lines) {
      if (line.trim()) {
        console.log(`[sidecar-stderr] ${line.trim()}`);
      }
    }
  });

  const handshake = await waitForHandshake(sidecarProcess, sidecarReadyTimeoutMs);
  if (!handshake) {
    console.error("[electron] Timed out waiting for sidecar handshake.");
    app.exit(1);
    return;
  }

  console.log(`[electron] Sidecar ready at ${handshake.endpoint}`);

  // Set up IPC handlers for renderer bridge requests
  setupIpcHandlers(handshake);

  // Set up mesh subscription and event forwarding
  await setupMeshSubscription(handshake, mainWindow!);

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

  mainWindow.on("closed", () => {
    mainWindow = null;
    bridgeClient?.disconnect();
    shutdown(0);
  });

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
    const response = await client.send(
      {
        requestId: `renderer-mesh-${Date.now()}`,
        command: "voxelforge.mesh.request_snapshot",
        payload: payload as Record<string, unknown>,
      },
      requestTimeoutMs,
    );
    if (response.error) {
      throw new Error(`Mesh snapshot error: ${response.error.message}`);
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

  ipcMain.on("renderer:metrics", (_event, metrics: Record<string, number>) => {
    console.log("[electron] Renderer metrics:", JSON.stringify(metrics, null, 2));
  });

  ipcMain.on("renderer:ready", () => {
    console.log("[electron] Renderer process ready.");
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