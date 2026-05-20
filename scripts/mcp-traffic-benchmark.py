#!/usr/bin/env python3
"""
MCP Traffic / Token Benchmark for VoxelForge.

Captures per-tool-call metrics (request/response bytes, wall time,
voxel counts) by replaying tool-call JSONL files or running a built-in
smoke scenario against the running VoxelForge MCP HTTP server.

Outputs JSON artifacts under /tmp/voxelforge/ (or --output-dir).

Usage:
  # Replay an existing tool-calls JSONL
  python3 scripts/mcp-traffic-benchmark.py replay tests/VoxelForge.Import.Tests/Fixtures/benchmark-tool-calls.jsonl

  # Run a built-in smoke sequence (small, medium, or large)
  python3 scripts/mcp-traffic-benchmark.py smoke [--size small|medium|large]

  # Run both
  python3 scripts/mcp-traffic-benchmark.py all

  # Custom MCP URL
  python3 scripts/mcp-traffic-benchmark.py --mcp-url http://localhost:5201/mcp smoke

  # Custom output directory
  python3 scripts/mcp-traffic-benchmark.py --output-dir /tmp/voxelforge/mcp-traffic all
"""

import argparse
import hashlib
import json
import os
import sys
import time
import urllib.error
import urllib.request
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path


# ── Constants ──────────────────────────────────────────────────────────────

DEFAULT_MCP_URL = "http://localhost:5201/mcp"
DEFAULT_HEALTH_URL = "http://localhost:5201/health"
DEFAULT_VIEWER_STATE_URL = "http://localhost:5201/api/viewer-state"
DEFAULT_OUTPUT_DIR = "/tmp/voxelforge"

SCHEMA_VERSION = 1

# One megabyte — around 25k tool calls in the worst case (tiny JSON‑RPC payload).
# This is a safe cap for a single artifact.
MAX_ARTIFACT_BYTES = 1_000_000


# ── Data Classes ───────────────────────────────────────────────────────────


class PerCallMetrics:
    """Metrics captured for one MCP tool call."""

    __slots__ = (
        "index", "tool_name", "round_num", "tool_call_id",
        "request_bytes", "response_bytes",
        "wall_ms", "ok",
        "result_summary",
        "arguments_snapshot",
        "before_voxel_count", "after_voxel_count",
        "applied_voxels",
    )

    def __init__(
        self,
        index: int,
        tool_name: str,
        round_num: int | None,
        tool_call_id: str | None,
        request_bytes: int,
        response_bytes: int,
        wall_ms: float,
        ok: bool,
        result_summary: str = "",
        arguments_snapshot: str = "",
        before_voxel_count: int = 0,
        after_voxel_count: int = 0,
        applied_voxels: int = 0,
    ):
        self.index = index
        self.tool_name = tool_name
        self.round_num = round_num
        self.tool_call_id = tool_call_id
        self.request_bytes = request_bytes
        self.response_bytes = response_bytes
        self.wall_ms = wall_ms
        self.ok = ok
        self.result_summary = result_summary
        self.arguments_snapshot = arguments_snapshot
        self.before_voxel_count = before_voxel_count
        self.after_voxel_count = after_voxel_count
        self.applied_voxels = applied_voxels

    def to_dict(self) -> dict:
        return {
            "index": self.index,
            "tool_name": self.tool_name,
            "round": self.round_num,
            "tool_call_id": self.tool_call_id,
            "request_bytes": self.request_bytes,
            "response_bytes": self.response_bytes,
            "wall_ms": round(self.wall_ms, 2),
            "ok": self.ok,
            "result_summary": self.result_summary,
            "arguments_snapshot": self.arguments_snapshot,
            "before_voxel_count": self.before_voxel_count,
            "after_voxel_count": self.after_voxel_count,
            "applied_voxels": self.applied_voxels,
        }


# ── MCP HTTP Helpers ──────────────────────────────────────────────────────


def _parse_sse_response(raw: bytes) -> dict | None:
    """Parse an SSE stream and return the first JSON data payload.

    SSE format:
        event: message
        data: {json_payload}

    Returns the parsed JSON dict, or None if no data event found.
    """
    decoded = raw.decode("utf-8", errors="replace")
    data_lines = []
    in_data = False
    for line in decoded.splitlines():
        if line.startswith("data: "):
            data_lines.append(line[6:])
            in_data = True
        elif line.startswith("event: "):
            pass  # skip event type
        elif line.strip() == "" and in_data:
            break  # empty line ends event

    if not data_lines:
        return None
    data_str = "".join(data_lines)
    return json.loads(data_str)


class McpSession:
    """Manages an MCP HTTP session lifecycle.

    Handles initialize/initialized handshake and session ID tracking
    for subsequent tool calls.
    """

    def __init__(self, mcp_url: str):
        self.mcp_url = mcp_url
        self.session_id: str | None = None
        self._request_id = 0

    def _next_id(self) -> int:
        self._request_id += 1
        return self._request_id

    def _build_request(
        self, method: str, params: dict | None = None, include_session: bool = True
    ) -> tuple[bytes, dict]:
        """Build an HTTP request for an MCP method.

        Returns (body_bytes, extra_headers).
        """
        body = {
            "jsonrpc": "2.0",
            "id": self._next_id(),
            "method": method,
        }
        if params is not None:
            body["params"] = params

        payload = json.dumps(body, ensure_ascii=False).encode("utf-8")
        headers = {
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
        }
        if include_session and self.session_id:
            headers["Mcp-Session-Id"] = self.session_id
        return payload, headers

    def initialize(self) -> tuple[int, int, str]:
        """Send initialize request and capture session ID.

        Returns (request_bytes, response_bytes, server_info_name).
        """
        payload, headers = self._build_request(
            "initialize",
            params={
                "protocolVersion": "2025-03-26",
                "capabilities": {},
                "clientInfo": {"name": "mcp-traffic-benchmark", "version": "1.0"},
            },
            include_session=False,
        )
        request_bytes = len(payload)

        req = urllib.request.Request(
            self.mcp_url,
            data=payload,
            headers=headers,
            method="POST",
        )

        with urllib.request.urlopen(req, timeout=30) as resp:
            raw = resp.read()
            response_bytes = len(raw)
            # Capture session ID from response header
            session_id = resp.headers.get("Mcp-Session-Id")
            if session_id:
                self.session_id = session_id

            response = _parse_sse_response(raw) or {}
            result = response.get("result", {})
            server_info = result.get("serverInfo", {})
            server_name = server_info.get("name", "unknown")

        return request_bytes, response_bytes, server_name

    def send_initialized(self) -> tuple[int, int]:
        """Send the initialized notification.

        Returns (request_bytes, response_bytes).
        """
        body = {
            "jsonrpc": "2.0",
            "method": "notifications/initialized",
        }
        payload = json.dumps(body, ensure_ascii=False).encode("utf-8")
        request_bytes = len(payload)
        headers = {
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
        }

        req = urllib.request.Request(
            self.mcp_url,
            data=payload,
            headers=headers,
            method="POST",
        )

        raw = b""
        try:
            with urllib.request.urlopen(req, timeout=30) as resp:
                raw = resp.read()
        except urllib.error.HTTPError as e:
            raw = e.read()
        except Exception:
            pass

        return request_bytes, len(raw)

    def call_tool(self, tool_name: str, arguments: dict | None = None) -> tuple[int, int, float, bool, str]:
        """Call an MCP tool.

        Returns (request_bytes, response_bytes, wall_ms, ok, result_summary).
        """
        payload, headers = self._build_request(
            "tools/call",
            params={"name": tool_name, "arguments": arguments or {}},
        )
        request_bytes = len(payload)

        req = urllib.request.Request(
            self.mcp_url,
            data=payload,
            headers=headers,
            method="POST",
        )

        start = time.perf_counter()
        try:
            with urllib.request.urlopen(req, timeout=60) as resp:
                wall_ms = (time.perf_counter() - start) * 1000
                raw = resp.read()
                response_bytes = len(raw)
                response = _parse_sse_response(raw) or {}
        except urllib.error.HTTPError as e:
            wall_ms = (time.perf_counter() - start) * 1000
            raw = e.read()
            response_bytes = len(raw)
            parsed = _parse_sse_response(raw)
            if parsed and "error" in parsed:
                err_msg = parsed["error"].get("message", str(parsed["error"]))
                return request_bytes, response_bytes, wall_ms, False, err_msg
            return request_bytes, response_bytes, wall_ms, False, f"HTTP {e.code}: {raw.decode('utf-8', errors='replace')[:200]}"
        except Exception as e:
            wall_ms = (time.perf_counter() - start) * 1000
            return request_bytes, 0, wall_ms, False, f"Error: {e}"

        if "error" in response and response["error"] is not None:
            err_msg = response["error"].get("message", str(response["error"]))
            return request_bytes, response_bytes, wall_ms, False, err_msg

        result = response.get("result", {})
        content_list = result.get("content", [])
        summary_parts = []
        for item in content_list:
            if isinstance(item, dict) and item.get("type") == "text":
                summary_parts.append(item.get("text", ""))
        summary = " ".join(summary_parts)[:500] if summary_parts else "(no text content)"

        is_error = result.get("isError", False)
        return request_bytes, response_bytes, wall_ms, not is_error, summary


def fetch_viewer_state(viewer_url: str) -> dict | None:
    """Fetch current viewer state. Returns dict or None on failure."""
    try:
        with urllib.request.urlopen(viewer_url, timeout=10) as resp:
            return json.loads(resp.read())
    except Exception:
        return None


def fetch_voxel_count_via_tool(session: McpSession) -> int:
    """Call get_model_info MCP tool and extract voxel_count."""
    _, _, _, ok, summary = session.call_tool("get_model_info", {})
    if not ok:
        return 0
    # Parse the JSON summary for voxel_count
    try:
        info = json.loads(summary)
        return info.get("voxel_count", 0)
    except (json.JSONDecodeError, AttributeError):
        pass
    # Fallback: try to extract from viewer state
    state = fetch_viewer_state(DEFAULT_VIEWER_STATE_URL)
    if state:
        return state.get("voxel_count", 0)
    return 0


def parse_applied_voxels(tool_name: str, result_summary: str) -> int:
    """Try to extract number of applied/generated voxels from result summary."""
    lower = result_summary.lower()
    for keyword in ("voxels", "voxel"):
        if keyword in lower:
            # Try to find a number before the keyword
            import re
            match = re.search(r'(\d+)\s+' + keyword, lower)
            if match:
                return int(match.group(1))
    if tool_name == "new_model":
        # A new empty model has 0 voxels
        return 0
    return 0


# ── Scenario Helpers ──────────────────────────────────────────────────────


def build_small_smoke_scenario() -> list[dict]:
    """Built-in small scenario: create model, set palette, box primitive."""
    return [
        {"name": "new_model", "arguments": {"name": "mcp-traffic-smoke-small", "grid_hint": 16}},
        {"name": "set_palette_entry", "arguments": {"index": 1, "name": "brick", "r": 120, "g": 40, "b": 30}},
        {"name": "apply_voxel_primitives", "arguments": {"primitives": [{"kind": "box", "mode": "filled", "palette_index": 1, "from": {"x": 0, "y": 0, "z": 0}, "to": {"x": 3, "y": 3, "z": 3}}]}},
        {"name": "get_model_info", "arguments": {}},
        {"name": "count_voxels", "arguments": {}},
        {"name": "get_voxel", "arguments": {"x": 0, "y": 0, "z": 0}},
    ]


def build_medium_smoke_scenario() -> list[dict]:
    """Built-in medium scenario: larger box, palette, region, multiple primitives."""
    return [
        {"name": "new_model", "arguments": {"name": "mcp-traffic-smoke-medium", "grid_hint": 48}},
        {"name": "set_palette_entry", "arguments": {"index": 1, "name": "stone", "r": 100, "g": 100, "b": 100}},
        {"name": "set_palette_entry", "arguments": {"index": 2, "name": "wood", "r": 140, "g": 90, "b": 50}},
        {"name": "set_palette_entry", "arguments": {"index": 3, "name": "leaf", "r": 40, "g": 140, "b": 40}},
        {"name": "apply_voxel_primitives", "arguments": {"primitives": [{"kind": "box", "mode": "shell", "palette_index": 1, "from": {"x": 0, "y": 0, "z": 0}, "to": {"x": 15, "y": 15, "z": 15}}]}},
        {"name": "apply_voxel_primitives", "arguments": {"primitives": [{"kind": "box", "mode": "filled", "palette_index": 2, "from": {"x": 6, "y": 0, "z": 6}, "to": {"x": 8, "y": 6, "z": 8}}]}},
        {"name": "create_region", "arguments": {"name": "floor"}},
        {"name": "get_model_info", "arguments": {}},
        {"name": "list_regions", "arguments": {}},
        {"name": "describe_model", "arguments": {}},
        {"name": "count_voxels", "arguments": {}},
        {"name": "set_palette_entry", "arguments": {"index": 4, "name": "glass", "r": 180, "g": 210, "b": 240}},
        {"name": "get_model_info", "arguments": {}},
    ]


def build_large_smoke_scenario() -> list[dict]:
    """Built-in larger scenario: 32^3 hollow room + pillars + contents."""
    calls = list(build_medium_smoke_scenario())
    # More palette entries
    calls.insert(2, {"name": "set_palette_entry", "arguments": {"index": 4, "name": "glass", "r": 180, "g": 210, "b": 240}})
    calls.insert(3, {"name": "set_palette_entry", "arguments": {"index": 5, "name": "metal", "r": 160, "g": 160, "b": 170}})
    # Replace the small scene with something larger
    calls[5] = {"name": "apply_voxel_primitives", "arguments": {"primitives": [{"kind": "box", "mode": "hollow", "palette_index": 1, "from": {"x": 0, "y": 0, "z": 0}, "to": {"x": 31, "y": 16, "z": 31}}]}}
    calls[6] = {"name": "apply_voxel_primitives", "arguments": {"primitives": [
        {"kind": "box", "mode": "filled", "palette_index": 4, "from": {"x": 2, "y": 2, "z": 2}, "to": {"x": 4, "y": 6, "z": 4}},
        {"kind": "box", "mode": "filled", "palette_index": 5, "from": {"x": 26, "y": 2, "z": 26}, "to": {"x": 28, "y": 6, "z": 28}},
        {"kind": "box", "mode": "filled", "palette_index": 2, "from": {"x": 2, "y": 2, "z": 26}, "to": {"x": 4, "y": 6, "z": 28}},
        {"kind": "box", "mode": "filled", "palette_index": 2, "from": {"x": 26, "y": 2, "z": 2}, "to": {"x": 28, "y": 6, "z": 28}},
        {"kind": "box", "mode": "filled", "palette_index": 5, "from": {"x": 14, "y": 0, "z": 14}, "to": {"x": 16, "y": 8, "z": 16}},
    ]}}
    return calls


# ── Loaders ────────────────────────────────────────────────────────────────


def load_tool_call_jsonl(path: str) -> list[dict]:
    """Load tool calls from a JSONL file (the benchmark-tool-calls.jsonl format)."""
    calls = []
    with open(path, "r") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            obj = json.loads(line)
            calls.append({
                "name": obj["name"],
                "arguments": obj.get("arguments", {}),
                "_index": obj.get("index"),
                "_round": obj.get("round"),
                "_tool_call_id": obj.get("tool_call_id"),
            })
    return calls


# ── Instrumentation Runner ─────────────────────────────────────────────────


def run_tool_sequence(
    mcp_url: str,
    viewer_url: str,
    mcp_tools: list[dict],
    label: str,
) -> list[PerCallMetrics]:
    """Execute a sequence of MCP tool calls and capture per-call metrics."""
    metrics_list: list[PerCallMetrics] = []

    # Create session and perform initialize handshake
    session = McpSession(mcp_url)

    # Initialize
    init_req_bytes, init_resp_bytes, server_name = session.initialize()
    if not session.session_id:
        print(f"WARNING: No session ID received from {server_name}", file=sys.stderr)

    # Send initialized notification
    notif_req_bytes, notif_resp_bytes = session.send_initialized()

    # Log init and notif as synthetic entries
    init_metric = PerCallMetrics(
        index=0,
        tool_name="initialize",
        round_num=None,
        tool_call_id=None,
        request_bytes=init_req_bytes,
        response_bytes=init_resp_bytes,
        wall_ms=0,
        ok=True,
        result_summary=f"Session initialized: {server_name} (session_id: {session.session_id[:16] if session.session_id else 'none'}...)",
        arguments_snapshot="",
        before_voxel_count=0,
        after_voxel_count=0,
        applied_voxels=0,
    )
    metrics_list.append(init_metric)

    for i, call_spec in enumerate(mcp_tools):
        tool_name = call_spec["name"]
        arguments = call_spec.get("arguments", {})

        # Get voxel count before
        before_count = fetch_voxel_count_via_tool(session)

        # Call the tool
        request_bytes, response_bytes, wall_ms, ok, summary = session.call_tool(
            tool_name, arguments
        )

        # Get voxel count after
        after_count = fetch_voxel_count_via_tool(session)

        # Estimate applied voxels
        applied = 0
        if ok and after_count >= before_count:
            # Net new voxels (won't capture removes/deletes, but covers the common case)
            delta = after_count - before_count
            applied = delta if delta > 0 else parse_applied_voxels(tool_name, summary)

        m = PerCallMetrics(
            index=i + 1,
            tool_name=tool_name,
            round_num=call_spec.get("_round"),
            tool_call_id=call_spec.get("_tool_call_id"),
            request_bytes=request_bytes,
            response_bytes=response_bytes,
            wall_ms=wall_ms,
            ok=ok,
            result_summary=summary[:200],
            arguments_snapshot=json.dumps(arguments, ensure_ascii=False)[:200],
            before_voxel_count=before_count,
            after_voxel_count=after_count,
            applied_voxels=applied,
        )
        metrics_list.append(m)

        # Brief pause between calls to let server settle
        time.sleep(0.1)

    return metrics_list


# ── Summary Computation ────────────────────────────────────────────────────


def compute_summary(metrics_list: list[PerCallMetrics], label: str) -> dict:
    """
    Compute summary metrics from per-call data.
    Token estimate uses chars/4 as documented in the task AC.
    """
    total_request_bytes = sum(m.request_bytes for m in metrics_list)
    total_response_bytes = sum(m.response_bytes for m in metrics_list)
    total_request_chars = total_request_bytes  # ASCII-compatible; close enough
    total_response_chars = total_response_bytes

    total_wall_ms = sum(m.wall_ms for m in metrics_list)
    tool_counts: Counter = Counter(m.tool_name for m in metrics_list)
    ok_count = sum(1 for m in metrics_list if m.ok)
    fail_count = sum(1 for m in metrics_list if not m.ok)

    # Token estimate: total_chars / 4 (documented approximate heuristic)
    total_chars = total_request_chars + total_response_chars
    estimated_total_tokens = total_chars // 4
    estimated_request_tokens = total_request_chars // 4
    estimated_response_tokens = total_response_chars // 4

    # Voxel metrics
    total_applied_voxels = sum(m.applied_voxels for m in metrics_list)
    first_voxel_count = metrics_list[0].before_voxel_count if metrics_list else 0
    last_voxel_count = metrics_list[-1].after_voxel_count if metrics_list else 0

    # Derived estimates
    bytes_per_voxel = (total_request_bytes + total_response_bytes) / max(total_applied_voxels, 1)
    tokens_per_voxel = estimated_total_tokens / max(total_applied_voxels, 1)

    # Per-tool breakdown
    per_tool = {}
    for m in metrics_list:
        if m.tool_name not in per_tool:
            per_tool[m.tool_name] = {
                "call_count": 0,
                "total_request_bytes": 0,
                "total_response_bytes": 0,
                "total_wall_ms": 0.0,
                "ok_count": 0,
                "fail_count": 0,
            }
        t = per_tool[m.tool_name]
        t["call_count"] += 1
        t["total_request_bytes"] += m.request_bytes
        t["total_response_bytes"] += m.response_bytes
        t["total_wall_ms"] += m.wall_ms
        if m.ok:
            t["ok_count"] += 1
        else:
            t["fail_count"] += 1
        # Average
        t["avg_request_bytes"] = t["total_request_bytes"] / t["call_count"]
        t["avg_response_bytes"] = t["total_response_bytes"] / t["call_count"]

    return {
        "label": label,
        "total_calls": len(metrics_list),
        "ok_count": ok_count,
        "fail_count": fail_count,
        "total_request_bytes": total_request_bytes,
        "total_response_bytes": total_response_bytes,
        "total_chars": total_chars,
        "estimated_request_tokens": estimated_request_tokens,
        "estimated_response_tokens": estimated_response_tokens,
        "estimated_total_tokens": estimated_total_tokens,
        "total_wall_ms": round(total_wall_ms, 2),
        "avg_wall_ms_per_call": round(total_wall_ms / max(len(metrics_list), 1), 2),
        "first_voxel_count": first_voxel_count,
        "last_voxel_count": last_voxel_count,
        "total_applied_voxels": total_applied_voxels,
        "net_voxel_delta": last_voxel_count - first_voxel_count,
        "bytes_per_voxel": round(bytes_per_voxel, 2),
        "tokens_per_voxel": round(tokens_per_voxel, 2),
        "tool_counts": dict(tool_counts),
        "per_tool": per_tool,
    }


def build_report(metrics_list: list[PerCallMetrics], summary: dict) -> dict:
    return {
        "schema_version": SCHEMA_VERSION,
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "summary": summary,
        "per_call": [m.to_dict() for m in metrics_list],
    }


# ── Markdown Report ────────────────────────────────────────────────────────


def build_markdown(report: dict) -> str:
    s = report["summary"]
    lines = [
        f"# MCP Traffic / Token Benchmark: {s['label']}",
        "",
        f"Generated: {report['generated_at_utc']}",
        f"Schema version: {report['schema_version']}",
        "",
        "## Summary",
        "",
        f"| Metric | Value |",
        f"|--------|-------|",
        f"| Total calls | {s['total_calls']} |",
        f"| OK / Failed | {s['ok_count']} / {s['fail_count']} |",
        f"| Total request bytes | {s['total_request_bytes']:,} |",
        f"| Total response bytes | {s['total_response_bytes']:,} |",
        f"| Total chars (request + response) | {s['total_chars']:,} |",
        f"| Estimated request tokens (chars/4) | {s['estimated_request_tokens']:,} |",
        f"| Estimated response tokens (chars/4) | {s['estimated_response_tokens']:,} |",
        f"| **Estimated total tokens (chars/4)** | **{s['estimated_total_tokens']:,}** |",
        f"| Total wall time | {s['total_wall_ms']:.1f} ms |",
        f"| Avg wall time per call | {s['avg_wall_ms_per_call']:.1f} ms |",
        f"| Initial voxel count | {s['first_voxel_count']:,} |",
        f"| Final voxel count | {s['last_voxel_count']:,} |",
        f"| Total applied voxels (estimated) | {s['total_applied_voxels']:,} |",
        f"| Net voxel delta | {s['net_voxel_delta']:,} |",
        f"| **Bytes per voxel** | **{s['bytes_per_voxel']:.1f}** |",
        f"| **Tokens per voxel** | **{s['tokens_per_voxel']:.1f}** |",
        "",
        "## Tool Call Breakdown",
        "",
        "| Tool | Calls | OK | Failed | Req bytes | Resp bytes | Avg req | Avg resp | Total wall (ms) |",
        "|------|------:|---:|------:|---------:|----------:|--------:|---------:|----------------:|",
    ]
    per_tool = s.get("per_tool", {})
    for tool_name, t in sorted(per_tool.items()):
        lines.append(
            f"| {tool_name} | {t['call_count']} | {t['ok_count']} | {t['fail_count']} | "
            f"{t['total_request_bytes']:,} | {t['total_response_bytes']:,} | "
            f"{t['avg_request_bytes']:.0f} | {t['avg_response_bytes']:.0f} | "
            f"{t['total_wall_ms']:.1f} |"
        )
    lines.append("")
    lines.append("## Per-Call Log")
    lines.append("")
    lines.append("| # | Tool | OK | Req B | Resp B | Wall (ms) | Before | After | Applied | Summary |")
    lines.append("|---|------|---|------:|-------:|----------:|------:|-----:|-------:|---------|")
    for m in report["per_call"]:
        lines.append(
            f"| {m['index']} | {m['tool_name']} | {'Y' if m['ok'] else 'N'} | "
            f"{m['request_bytes']:,} | {m['response_bytes']:,} | "
            f"{m['wall_ms']:.1f} | {m['before_voxel_count']:,} | "
            f"{m['after_voxel_count']:,} | {m['applied_voxels']} | "
            f"{m['result_summary'][:80]} |"
        )
    lines.append("")
    lines.append("## Interpretation Notes")
    lines.append("")
    lines.append("- **Token estimate:** Uses `chars/4` heuristic (1 token ≈ 4 characters).")
    lines.append("  Actual token counts depend on the model's tokenizer (Claude ≈3.5 chars/token,")
    lines.append("  GPT ≈4 chars/token). This is a rough approximation for ranging purposes.")
    lines.append("- **Bytes ≈ chars** for ASCII text. Non-ASCII characters may inflate byte counts.")
    lines.append("- **Applied voxels** is estimated from the delta between before/after model states.")
    lines.append("  It may undercount when voxels are removed/overwritten or overcount when tools")
    lines.append("  report summary text mentions voxel counts differently.")
    lines.append("- **Wall time** includes network round-trip, server processing, JSON-RPC overhead,")
    lines.append("  and a 100ms settling pause between calls. Subtract the pause overhead when")
    lines.append("  comparing raw tool execution speed.")
    lines.append("- Run with representative model sizes and tool sequences for meaningful ranging.")
    lines.append("")
    return "\n".join(lines)


# ── Output ─────────────────────────────────────────────────────────────────


def write_artifact(
    output_dir: str,
    label: str,
    report: dict,
    markdown: str,
) -> tuple[str, str, str]:
    """
    Write JSON, markdown, and summary JSON artifacts.
    Returns (json_path, md_path, summary_path).
    """
    os.makedirs(output_dir, exist_ok=True)
    safe_label = label.replace(" ", "-").replace("/", "-").lower()

    json_path = os.path.join(output_dir, f"mcp-traffic-{safe_label}.json")
    md_path = os.path.join(output_dir, f"mcp-traffic-{safe_label}.md")
    summary_path = os.path.join(output_dir, f"mcp-traffic-{safe_label}-summary.json")

    # Write full JSON report
    blob = json.dumps(report, indent=2, ensure_ascii=False)
    if len(blob.encode("utf-8")) > MAX_ARTIFACT_BYTES:
        # Truncate per-call data for large runs
        summary_only = {
            "schema_version": report["schema_version"],
            "generated_at_utc": report["generated_at_utc"],
            "summary": report["summary"],
            "per_call": f"(truncated — {len(report['per_call'])} calls omitted)",
        }
        blob = json.dumps(summary_only, indent=2, ensure_ascii=False)
    with open(json_path, "w") as f:
        f.write(blob)

    # Write markdown
    with open(md_path, "w") as f:
        f.write(markdown)

    # Write summary-only JSON
    summary_blob = json.dumps(report["summary"], indent=2, ensure_ascii=False)
    with open(summary_path, "w") as f:
        f.write(summary_blob)

    return json_path, md_path, summary_path


# ── Health Check ────────────────────────────────────────────────────────────


def check_server_health(health_url: str) -> bool:
    """Check that the MCP server is running and healthy."""
    try:
        with urllib.request.urlopen(health_url, timeout=5) as resp:
            data = json.loads(resp.read())
            status = data.get("status", "")
            if status == "healthy":
                return True
            print(f"Server health check returned status: {status}", file=sys.stderr)
            return False
    except Exception as e:
        print(f"Server health check failed: {e}", file=sys.stderr)
        return False


# ── CLI ────────────────────────────────────────────────────────────────────


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="MCP Traffic / Token Benchmark for VoxelForge",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  %(prog)s replay tests/VoxelForge.Import.Tests/Fixtures/benchmark-tool-calls.jsonl
  %(prog)s smoke --size medium
  %(prog)s all
  %(prog)s --mcp-url http://localhost:5201/mcp --output-dir /tmp/voxelforge/mcp-traffic all
        """,
    )
    parser.add_argument(
        "--mcp-url",
        default=DEFAULT_MCP_URL,
        help=f"MCP HTTP endpoint (default: {DEFAULT_MCP_URL})",
    )
    parser.add_argument(
        "--output-dir",
        default=DEFAULT_OUTPUT_DIR,
        help=f"Output directory for artifacts (default: {DEFAULT_OUTPUT_DIR})",
    )
    parser.add_argument(
        "--check-health",
        action="store_true",
        default=True,
        dest="check_health",
        help="Check server health before running (default: true)",
    )
    parser.add_argument(
        "--no-health-check",
        action="store_false",
        dest="check_health",
        help="Skip server health check",
    )

    subparsers = parser.add_subparsers(dest="mode", required=True)

    # replay
    replay_parser = subparsers.add_parser("replay", help="Replay tool-call JSONL")
    replay_parser.add_argument("input", help="Path to tool-calls JSONL file")
    replay_parser.add_argument("--label", help="Label for the report (default: filename stem)")

    # smoke
    smoke_parser = subparsers.add_parser("smoke", help="Run built-in smoke scenario")
    smoke_parser.add_argument(
        "--size",
        choices=["small", "medium", "large"],
        default="medium",
        help="Smoke scenario size (default: medium)",
    )
    smoke_parser.add_argument("--label", help="Label for the report (default: smoke-{size})")

    # all (replay default inputs then smoke)
    all_parser = subparsers.add_parser("all", help="Run all built-in scenarios")
    all_parser.add_argument(
        "--smoke-size",
        choices=["small", "medium", "large"],
        default="medium",
        help="Smoke scenario size for 'all' mode (default: medium)",
    )
    all_parser.add_argument(
        "--input",
        default=None,
        help="Optional tool-call JSONL to include (in addition to smoke scenarios)",
    )

    return parser.parse_args(argv)


def main() -> int:
    args = parse_args(sys.argv[1:])

    mcp_url = args.mcp_url
    output_dir = args.output_dir
    viewer_url = mcp_url.rsplit("/mcp", 1)[0] + "/api/viewer-state" if "/mcp" in mcp_url else DEFAULT_VIEWER_STATE_URL

    # Health check
    if args.check_health:
        health_url = mcp_url.rsplit("/mcp", 1)[0] + "/health" if "/mcp" in mcp_url else DEFAULT_HEALTH_URL
        if not check_server_health(health_url):
            print(
                "ERROR: MCP server is not healthy. Start it with:\n"
                f"  scripts/ensure-mcp-web.sh\n"
                f"then retry.",
                file=sys.stderr,
            )
            return 1

    mode = args.mode
    all_reports = []

    if mode == "replay":
        input_path = args.input
        label = args.label or Path(input_path).stem
        if not os.path.exists(input_path):
            print(f"Input file not found: {input_path}", file=sys.stderr)
            return 1
        scenario = load_tool_call_jsonl(input_path)
        metrics = run_tool_sequence(mcp_url, viewer_url, scenario, label)
        summary = compute_summary(metrics, label)
        report = build_report(metrics, summary)
        md = build_markdown(report)
        json_path, md_path, summary_path = write_artifact(output_dir, label, report, md)
        print(f"Wrote {len(metrics)} call metrics to:")
        print(f"  JSON: {json_path}")
        print(f"  MD:   {md_path}")
        print(f"  Summary: {summary_path}")
        _print_summary_table(summary)

    elif mode == "smoke":
        size = args.size
        label = args.label or f"smoke-{size}"
        if size == "small":
            scenario = build_small_smoke_scenario()
        elif size == "large":
            scenario = build_large_smoke_scenario()
        else:
            scenario = build_medium_smoke_scenario()
        metrics = run_tool_sequence(mcp_url, viewer_url, scenario, label)
        summary = compute_summary(metrics, label)
        report = build_report(metrics, summary)
        md = build_markdown(report)
        json_path, md_path, summary_path = write_artifact(output_dir, label, report, md)
        print(f"Wrote {len(metrics)} call metrics to:")
        print(f"  JSON: {json_path}")
        print(f"  MD:   {md_path}")
        print(f"  Summary: {summary_path}")
        _print_summary_table(summary)

    elif mode == "all":
        # Smoke scenarios
        for size_name in ("small", "medium", "large"):
            label = f"smoke-{size_name}"
            if size_name == "small":
                scenario = build_small_smoke_scenario()
            elif size_name == "large":
                scenario = build_large_smoke_scenario()
            else:
                scenario = build_medium_smoke_scenario()
            metrics = run_tool_sequence(mcp_url, viewer_url, scenario, label)
            summary = compute_summary(metrics, label)
            report = build_report(metrics, summary)
            md = build_markdown(report)
            paths = write_artifact(output_dir, label, report, md)
            all_reports.append((label, paths, report))
            print(f"  [{size_name}] {paths[2]}")

        # Optional user-supplied JSONL
        if args.input:
            input_path = args.input
            if not os.path.exists(input_path):
                print(f"Input file not found: {input_path}", file=sys.stderr)
                return 1
            label = Path(input_path).stem
            scenario = load_tool_call_jsonl(input_path)
            metrics = run_tool_sequence(mcp_url, viewer_url, scenario, label)
            summary = compute_summary(metrics, label)
            report = build_report(metrics, summary)
            md = build_markdown(report)
            paths = write_artifact(output_dir, label, report, md)
            all_reports.append((label, paths, report))
            print(f"  [{label}] {paths[2]}")

        # Write combined comparison summary
        _write_combined_comparison(output_dir, all_reports)
        print(f"Wrote comparison: {os.path.join(output_dir, 'mcp-traffic-comparison.md')}")

    return 0


def _print_summary_table(s: dict) -> None:
    """Print a compact summary to stdout."""
    print()
    print(f"  Scenario: {s['label']}")
    print(f"  Calls: {s['total_calls']} ({s['ok_count']} ok, {s['fail_count']} failed)")
    print(f"  Request:  {s['total_request_bytes']:,} bytes → ~{s['estimated_request_tokens']:,} tokens")
    print(f"  Response: {s['total_response_bytes']:,} bytes → ~{s['estimated_response_tokens']:,} tokens")
    print(f"  Total:    ~{s['estimated_total_tokens']:,} tokens ({s['total_wall_ms']:.0f} ms)")
    print(f"  Voxels: {s['first_voxel_count']:,} → {s['last_voxel_count']:,} ({s['total_applied_voxels']:,} applied)")
    print(f"  Bytes/voxel:  {s['bytes_per_voxel']:.1f}")
    print(f"  Tokens/voxel: {s['tokens_per_voxel']:.1f}")
    print()


def _write_combined_comparison(output_dir: str, all_reports: list) -> None:
    """Write a comparison markdown across multiple benchmark runs."""
    lines = [
        "# MCP Traffic / Token Benchmark: Combined Comparison",
        "",
        "## Overview",
        "",
        f"Generated: {datetime.now(timezone.utc).isoformat()}",
        "",
        "| Scenario | Calls | Req bytes | Resp bytes | Est tokens | Wall (ms) | Voxels (before→after) | Applied | Bytes/voxel | Tokens/voxel |",
        "|----------|------:|----------:|-----------:|-----------:|----------:|:---------------------:|-------:|-----------:|-------------:|",
    ]

    for label, paths, report in all_reports:
        s = report["summary"]
        lines.append(
            f"| {s['label']} | {s['total_calls']} | "
            f"{s['total_request_bytes']:,} | "
            f"{s['total_response_bytes']:,} | "
            f"{s['estimated_total_tokens']:,} | "
            f"{s['total_wall_ms']:.0f} | "
            f"{s['first_voxel_count']}→{s['last_voxel_count']} | "
            f"{s['total_applied_voxels']:,} | "
            f"{s['bytes_per_voxel']:.1f} | "
            f"{s['tokens_per_voxel']:.1f} |"
        )

    lines.append("")
    lines.append("## Running Cost Estimates")
    lines.append("")
    lines.append("| Model | Input $/1M tok | Output $/1M tok | Est cost (input) | Est cost (output) | Est total |")
    lines.append("|-------|--------------:|---------------:|-----------------:|------------------:|----------:|")
    cost_examples = [
        ("GPT-4o", 2.50, 10.00),
        ("Claude Sonnet 4", 3.00, 15.00),
        ("Claude Haiku 3.5", 0.80, 4.00),
        ("GPT-4.1 Mini", 0.40, 1.60),
        ("DeepSeek V4", 0.50, 2.00),
    ]
    for label, cost_in, cost_out in cost_examples:
        for label2, _, report2 in all_reports:
            s = report2["summary"]
            input_cost = s["estimated_request_tokens"] / 1_000_000 * cost_in
            output_cost = s["estimated_response_tokens"] / 1_000_000 * cost_out
            total_cost = input_cost + output_cost
            if label2 == all_reports[-1][0]:
                # Only show costs for the last / largest scenario
                lines.append(
                    f"| {label} | ${cost_in:.2f} | ${cost_out:.2f} | "
                    f"${input_cost:.5f} | ${output_cost:.5f} | **${total_cost:.5f}** |"
                )

    lines.append("")
    lines.append("## Per-Scenario Details")
    lines.append("")
    for label, paths, report in all_reports:
        _, _, summary_path = paths
        lines.append(f"- [{label}]({os.path.basename(summary_path)}) — see `{label}.json` and `{label}.md`")
    lines.append("")

    content = "\n".join(lines)
    comp_path = os.path.join(output_dir, "mcp-traffic-comparison.md")
    with open(comp_path, "w") as f:
        f.write(content)
    print(f"Wrote comparison: {comp_path}")


if __name__ == "__main__":
    sys.exit(main())
