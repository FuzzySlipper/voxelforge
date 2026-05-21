#!/usr/bin/env python3
"""
Payload size comparison: set_voxels vs set_voxels_runs.

Generates a large voxel fixture (e.g., a 64x32x64 box = 131K voxels)
and compares the JSON payload size of:

  - set_voxels: one object per voxel, e.g. {"x":0,"y":0,"z":0,"i":1}
  - set_voxels_runs: one run per horizontal line, e.g. {"x1":0,"x2":63,"y":0,"z":0,"i":1}

Outputs a comparison table to stdout.
"""

import json
import math
import sys

def gen_set_voxels_payload(x_size, y_size, z_size, palette_index=1):
    """Generate set_voxels format: one object per voxel."""
    voxels = []
    for z in range(z_size):
        for y in range(y_size):
            for x in range(x_size):
                voxels.append({"x": x, "y": y, "z": z, "i": palette_index})
    payload = {"voxels": voxels}
    return json.dumps(payload, separators=(",", ":"))


def gen_runs_payload(x_size, y_size, z_size, palette_index=1):
    """Generate set_voxels_runs format: one run per horizontal line."""
    runs = []
    for z in range(z_size):
        for y in range(y_size):
            runs.append({
                "x1": 0,
                "x2": x_size - 1,
                "y": y,
                "z": z,
                "i": palette_index,
            })
    payload = {"runs": runs}
    return json.dumps(payload, separators=(",", ":"))


def format_bytes(b):
    if b < 1024:
        return f"{b} B"
    elif b < 1024 * 1024:
        return f"{b / 1024:.1f} KB"
    else:
        return f"{b / (1024*1024):.1f} MB"


def main():
    scenarios = [
        ("Small box 4³", 4, 4, 4),
        ("Medium box 16³", 16, 16, 16),
        ("Large box 32³", 32, 32, 32),
        ("Tall wall 64×16×64", 64, 16, 64),
        ("Big box 64³", 64, 64, 64),
    ]

    print("=" * 80)
    print("  Voxel Payload Size Comparison:  set_voxels  vs  set_voxels_runs")
    print("=" * 80)
    print()
    print(f"{'Scenario':<25} {'Voxels':>10} {'set_voxels':>15} {'set_voxels_runs':>18} {'Ratio':>10} {'Savings':>10}")
    print("-" * 90)

    for label, xs, ys, zs in scenarios:
        voxel_count = xs * ys * zs
        raw = gen_set_voxels_payload(xs, ys, zs)
        runs = gen_runs_payload(xs, ys, zs)
        ratio = len(raw) / len(runs)
        savings_pct = (1 - len(runs) / len(raw)) * 100

        print(
            f"{label:<25} {voxel_count:>10,} "
            f"{format_bytes(len(raw)):>15} "
            f"{format_bytes(len(runs)):>18} "
            f"{ratio:>8.1f}x "
            f"{savings_pct:>8.1f}%"
        )

    print()
    print("Notes:")
    print("  - Payloads use compact JSON (separators=(',',':')).")
    print("  - Runs format assumes each horizontal line is a contiguous run.")
    print("  - Real-world savings vary with model geometry.")
    print("  - For sparse models, run grouping may be less efficient.")
    print()


if __name__ == "__main__":
    main()
