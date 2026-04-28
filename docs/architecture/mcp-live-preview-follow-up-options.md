# MCP Live Preview Follow-up Options

Task 925 evaluates whether VoxelForge should add local push notifications or a launcher/viewer workflow after the snapshot-based MCP live preview path from tasks 924, 926, and 927.

## Current baseline

The implemented baseline is intentionally simple:

```text
agent -> VoxelForge.Mcp headless session -> publish_preview snapshot
                                           -> watched .vforge file
                                           -> GUI reload on update thread
```

This keeps the MCP session authoritative, keeps `VoxelForge.Mcp` headless, and lets the GUI act as a human-facing observer. It also keeps benchmarks and non-GUI MCP use independent from FNA/Myra.

## Option A: local push notification channel

A push channel would notify a running GUI/viewer when a snapshot is ready instead of relying only on file-system events.

Possible shapes:

- MCP exposes a lightweight local event stream such as SSE or WebSocket.
- GUI preview mode optionally connects to that stream and reloads the path named by the event payload.
- The event payload stays small: session id, preview name/path, manifest path, updated timestamp, and counts.
- The `.vforge` file remains the data interchange format; the push event is only a nudge to reload.

Benefits:

- Clearer feedback when file-system watcher behavior varies by platform.
- Easier future UI affordances such as "connected to MCP session X".
- Could support a session list or status panel without sharing mutable document state.

Costs/risks:

- Requires connection lifecycle, reconnect behavior, and port/session discovery.
- Adds another local transport to test and document.
- Does not eliminate the need for snapshot files unless we also add a much larger shared-state protocol.

Recommendation: defer until file-watch preview has real usage feedback. If implemented, keep it as a reload nudge over the existing snapshot path rather than sending raw model mutations.

## Option B: launcher or read-only viewer workflow

A launcher/viewer workflow would make it easier to open the preview window without manually typing the GUI `--watch` command.

Possible shapes:

- Add a small script, for example `scripts/open-mcp-preview.sh <project-dir> [preview-name]`.
- Add a GUI flag such as `--preview-session <project-dir>:<preview-name>` only if it materially improves the command line over `--watch`.
- Let `VoxelForge.Mcp` expose the exact suggested GUI command in `/health` or a future `get_preview_info` tool, but do not have MCP directly spawn the GUI by default.

Benefits:

- Low risk compared with a push channel.
- Improves onboarding for first-time users.
- Keeps the renderer in the Engine process and preserves architecture boundaries.

Costs/risks:

- Cross-platform process launching from MCP can get awkward quickly.
- Auto-launching a GUI from an MCP server may surprise users or CI runs.
- Scripts need platform coverage or clear docs.

Recommendation: prefer a documented helper command/script over MCP auto-launch. Do not make `publish_preview` spawn windows.

## Non-goal: true live shared-document editing

Do not replace the snapshot observer flow with direct GUI document manipulation unless a separate product decision approves collaborative editing. A direct shared-document model would need explicit ownership, conflict handling, undo semantics, thread dispatch rules, and UI state synchronization.

## Recommended next steps

1. Keep the current file-watch + `publish_preview` workflow as the supported v1 live preview path.
2. Collect usage feedback on whether file watcher reloads are reliable enough on target platforms.
3. Revisit the push/launcher deferral after the first real MCP live-preview collaboration pilot, after the next major platform/client integration, or sooner if users report missed refreshes or repeated command-line setup friction.
4. Add a small launcher/helper script only if command friction becomes noticeable.
5. Add SSE/WebSocket reload nudges only if file-system events prove unreliable or the GUI needs richer session status.
6. Continue treating push/launcher work as UX polish, not a prerequisite for agent collaboration.
