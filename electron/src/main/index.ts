import { app } from "electron";
import { spawn } from "child_process";
import { BridgeClient } from "./bridge-client";

const isSmokeTest = process.argv.includes("--smoke-test");
const sidecarReadyTimeoutMs = 15000;
const requestTimeoutMs = 5000;

let sidecarProcess: ReturnType<typeof spawn> | null = null;
let exitCode = 1;

async function main(): Promise<void> {
  console.log("[electron-smoke] Starting VoxelForge bridge smoke test...");

  const repoRoot = findRepoRoot(__dirname);
  if (!repoRoot) {
    console.error("[electron-smoke] Could not locate repository root.");
    app.exit(1);
    return;
  }

  const sidecarBinary = `${repoRoot}/src/VoxelForge.Bridge/bin/Debug/net10.0/VoxelForge.Bridge`;
  console.log(`[electron-smoke] Spawning sidecar: ${sidecarBinary}`);

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
    shutdown();
    return;
  }

  console.log(`[electron-smoke] Sidecar ready at ${handshake.endpoint}`);

  const client = new BridgeClient({
    endpoint: handshake.endpoint,
    authToken: handshake.auth_token,
  });

  try {
    await client.connect();
    console.log("[electron-smoke] WebSocket connected.");
  } catch (err) {
    console.error("[electron-smoke] WebSocket connection failed:", err);
    shutdown();
    return;
  }

  try {
    // 1. Ping test
    console.log("[electron-smoke] Sending ping...");
    const pingResponse = await client.send(
      { requestId: "smoke-ping", command: "ping", payload: { message: "hello-from-electron" } },
      requestTimeoutMs,
    );

    if (pingResponse.error) {
      console.error("[electron-smoke] Ping failed:", pingResponse.error);
      shutdown();
      return;
    }

    const pingResult = pingResponse.result as { echo?: string; timestamp?: number } | undefined;
    console.log(`[electron-smoke] Ping OK: echo=${pingResult?.echo}, timestamp=${pingResult?.timestamp}`);

    // 2. Version handshake test
    console.log("[electron-smoke] Sending version handshake...");
    const versionResponse = await client.send(
      {
        requestId: "smoke-version",
        command: "version.handshake",
        payload: { client_protocol_version: "1.0" },
      },
      requestTimeoutMs,
    );

    if (versionResponse.error) {
      console.error("[electron-smoke] Version handshake failed:", versionResponse.error);
      shutdown();
      return;
    }

    const versionResult = versionResponse.result as
      | {
          sidecar_protocol_version?: string;
          app_id?: string;
          app_version?: string;
          compatible?: boolean;
        }
      | undefined;

    console.log(
      `[electron-smoke] Version handshake OK: app=${versionResult?.app_id}@${versionResult?.app_version}, ` +
        `protocol=${versionResult?.sidecar_protocol_version}, compatible=${versionResult?.compatible}`,
    );

    if (!versionResult?.compatible) {
      console.error("[electron-smoke] Protocol version mismatch.");
      shutdown();
      return;
    }

    console.log("[electron-smoke] All checks passed.");
    exitCode = 0;
  } catch (err) {
    console.error("[electron-smoke] Request failed:", err);
  } finally {
    client.disconnect();
    shutdown();
  }
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

function shutdown(): void {
  if (sidecarProcess && !sidecarProcess.killed) {
    sidecarProcess.kill("SIGTERM");
  }
  app.exit(exitCode);
}

function findRepoRoot(startPath: string): string | null {
  const path = require("path");
  let dir = path.resolve(startPath);
  while (dir !== path.dirname(dir)) {
    if (require("fs").existsSync(path.join(dir, "voxelforge.slnx"))) {
      return dir;
    }
    dir = path.dirname(dir);
  }
  return null;
}

app.whenReady().then(main);

app.on("window-all-closed", () => {
  // No windows are created in smoke-test mode.
});
