#!/usr/bin/env python3
"""Generate combined-summary.md from C# and Electron benchmark JSON files."""

import json
import os
import sys


def main():
    csharp_path = sys.argv[1]
    electron_path = sys.argv[2] if len(sys.argv) > 2 else None
    summary_path = sys.argv[3] if len(sys.argv) > 3 else None

    with open(csharp_path) as f:
        csharp = json.load(f)

    electron = {}
    if electron_path and os.path.exists(electron_path):
        with open(electron_path) as f:
            electron = json.load(f)

    lines = []
    lines.append("# VoxelForge Renderer Performance Benchmark")
    lines.append("")
    lines.append(f"Date: {csharp.get('timestamp_utc', 'unknown')}")
    lines.append(f"Git commit: {csharp.get('git_commit', 'unknown')}")
    lines.append(f"Warmup trials: {csharp['warmup_trials']}")
    lines.append(f"Measurement trials: {csharp['measurement_trials']}")
    lines.append("")
    lines.append("## C# Benchmark Results")
    lines.append("")
    lines.append("| Scene | Voxels | Greedy Mesh (ms) | Greedy Tris | Naive Mesh (ms) | Naive Tris | Snapshot (ms) | Est. JSON (bytes) | Edit/Remesh (ms) |")
    lines.append("|-------|--------|-----------------|-------------|-----------------|-------------|--------------|-------------------|-----------------|")
    for s in csharp["scenes"]:
        g = s["greedy_mesher"]
        n = s["naive_mesher"]
        snap = s["mesh_snapshot"]
        edit = s["edit_mutation"]
        lines.append(
            f"| {s['scene_id']} | {s['actual_voxel_count']} | "
            f"{g['median_ms']} | {g['triangle_count']} | "
            f"{n['median_ms']} | {n['triangle_count']} | "
            f"{snap['median_ms']} | {snap['estimated_json_bytes']} | "
            f"{edit['median_ms']} |"
        )

    lines.append("")
    lines.append("### Key Observations")
    greedy_first = csharp["scenes"][0]["greedy_mesher"]
    naive_first = csharp["scenes"][0]["naive_mesher"]
    ratio = max(naive_first["triangle_count"] // max(greedy_first["triangle_count"], 1), 1)
    lines.append(f"- GreedyMesher reduces triangle count by ~{ratio}x vs NaiveMesher for the small scene.")
    lines.append("- Mesh snapshot (GreedyMesher + MeshSnapshotService) cost tracks GreedyMesher time closely.")
    last_scene = csharp["scenes"][-1]
    kb_estimate = last_scene["mesh_snapshot"]["estimated_json_bytes"] // 1000
    lines.append(
        f"- {last_scene['scene_id']} (~{last_scene['actual_voxel_count']} voxels) produces "
        f"~{kb_estimate}KB JSON payload."
    )
    max_edit_ms = max(s["edit_mutation"]["median_ms"] for s in csharp["scenes"])
    lines.append(f"- Edit mutation + remesh latency stays under {max_edit_ms}ms for all default scenes.")
    lines.append("")

    if electron:
        lines.append("## Electron/Renderer Metrics")
        lines.append("")
        if electron.get("status") == "skipped":
            lines.append(f"Electron smoke test skipped: {electron.get('reason', 'unknown')}")
        elif "scene_construction_ms" in electron or "total_renderer_ms" in electron:
            lines.append("| Metric | Value |")
            lines.append("|--------|-------|")
            for k, v in electron.items():
                lines.append(f"| {k} | {v} |")
            lines.append("")
            lines.append("> Note: Electron metrics are from the default C# initial model (empty/new).")
            lines.append("> For per-scene Electron metrics, a bridge benchmark command needs to create each scene.")
        else:
            lines.append("Electron smoke output was captured but metrics format not recognized.")
            lines.append("```json")
            lines.append(json.dumps(electron, indent=2))
            lines.append("```")
    else:
        lines.append("## Electron/Renderer Metrics")
        lines.append("")
        lines.append("(Not collected. Run with `--electron` flag and a display server.)")

    lines.append("")
    lines.append("## Limitations")
    lines.append("")
    lines.append("- C# benchmarks measure CPU-side only: mesh generation, snapshot construction, and model mutation.")
    lines.append("- GPU upload, draw-call, and frame-time measurements require a graphics API (FNA or Electron/WebGL).")
    lines.append("- Electron Scene construction performance is measured via the existing `--renderer-smoke-test` path.")
    lines.append("- To measure per-scene Electron construction, a bridge command to create benchmark scenes on the C# sidecar would be needed.")
    lines.append("- JSON serialization estimate is approximate (float/byte/int size heuristics).")
    lines.append("")

    content = "\n".join(lines)
    if summary_path:
        with open(summary_path, "w") as f:
            f.write(content)
        print(f"Wrote summary: {summary_path}")
    else:
        print(content)


if __name__ == "__main__":
    main()
