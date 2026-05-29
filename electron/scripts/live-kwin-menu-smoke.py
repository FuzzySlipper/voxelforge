#!/usr/bin/env python3
"""Live KWin smoke loop for Electron native menu behavior.

This is intentionally a Runner/debugging workflow, not a deterministic CI gate.
It connects to the agent user's live Plasma/KWin session via kwin-mcp, launches
VoxelForge Electron, drives Reference -> Load Reference Model..., and captures
screenshots plus app log evidence so menu paths that look clickable but do
nothing are visible without waiting for manual Patch testing.

Run through scripts/run-live-kwin-menu-smoke.sh so kwin-mcp is supplied by uvx.
"""

from __future__ import annotations

import argparse
import contextlib
import ctypes
import json
import os
import re
import shutil
import signal
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path
from typing import Any

from kwin_mcp import input as kwin_input  # type: ignore[import-not-found]
from kwin_mcp.core import AutomationEngine  # type: ignore[import-not-found]


SCREENSHOT_RE = re.compile(r"Screenshot saved: (?P<path>\S+) \((?P<size>[^)]+)\)")


def patch_libei_variadic_bind() -> None:
    """Work around libei/ctypes variadic crashes seen on the Runner host.

    kwin-mcp currently calls ei_seat_bind_capabilities as a variadic function.
    On this host, passing Python ints/c_uint for the variadic args can segfault
    in libei. Explicit c_int args plus a c_void_p(NULL) sentinel match the C ABI
    expected by this libei build and restore KWin EIS input injection.
    """

    def patched_bind(self: object, event: int) -> None:
        seat = kwin_input._libei.ei_event_get_seat(event)
        bind_list: list[int] = []
        for cap in [
            kwin_input._EI_CAP_POINTER,
            kwin_input._EI_CAP_POINTER_ABSOLUTE,
            kwin_input._EI_CAP_KEYBOARD,
            kwin_input._EI_CAP_TOUCH,
            kwin_input._EI_CAP_BUTTON,
            kwin_input._EI_CAP_SCROLL,
        ]:
            if kwin_input._libei.ei_seat_has_capability(seat, cap):
                bind_list.append(cap)

        func = kwin_input._libei.ei_seat_bind_capabilities
        func.restype = None
        args: list[ctypes.c_int | ctypes.c_void_p] = [ctypes.c_int(c) for c in bind_list]
        args.append(ctypes.c_void_p(None))
        func(ctypes.c_void_p(seat), *args)

    kwin_input.EISClient._bind_seat_capabilities = patched_bind  # type: ignore[method-assign]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run live KWin Electron Reference-menu smoke.")
    parser.add_argument("--electron-dir", type=Path, default=Path.cwd(), help="Electron project directory (default: cwd).")
    parser.add_argument("--dbus-address", default=os.environ.get("DBUS_SESSION_BUS_ADDRESS", "unix:path=/run/user/1001/bus"))
    parser.add_argument("--wayland-display", default=os.environ.get("WAYLAND_DISPLAY", "wayland-0"))
    parser.add_argument("--display", default=os.environ.get("DISPLAY", ":0"))
    parser.add_argument("--xdg-runtime-dir", default=os.environ.get("XDG_RUNTIME_DIR", "/run/user/1001"))
    parser.add_argument("--output-dir", type=Path, default=None, help="Evidence directory (default: ./artifacts/live-kwin-menu-smoke/<timestamp>).")
    parser.add_argument("--reference-path", type=Path, default=None, help="Reference model path to type into the prompt. Default creates a tiny OBJ in the evidence dir.")
    parser.add_argument("--no-build", action="store_true", help="Skip npm run build before launching Electron.")
    parser.add_argument("--startup-timeout", type=float, default=45.0)
    parser.add_argument("--post-action-wait", type=float, default=3.0)
    parser.add_argument("--focus-x", type=int, default=640, help="Coordinate to click to focus the Electron window before menu driving.")
    parser.add_argument("--focus-y", type=int, default=400)
    parser.add_argument("--menu-mode", choices=["keyboard", "coordinate"], default="keyboard", help="Use Alt+R/Enter or fixed coordinates for Reference -> first item.")
    parser.add_argument("--reference-menu-x", type=int, default=134, help="Coordinate fallback: Reference top-level menu x.")
    parser.add_argument("--reference-menu-y", type=int, default=34, help="Coordinate fallback: Reference top-level menu y.")
    parser.add_argument("--load-item-x", type=int, default=172, help="Coordinate fallback: Load Reference Model item x.")
    parser.add_argument("--load-item-y", type=int, default=64, help="Coordinate fallback: Load Reference Model item y.")
    return parser.parse_args()


def run_build(electron_dir: Path) -> None:
    print("[live-kwin-smoke] npm run build", flush=True)
    subprocess.run(["npm", "run", "build"], cwd=electron_dir, check=True)


def find_electron(electron_dir: Path) -> Path:
    candidates = [
        electron_dir / "node_modules" / ".bin" / "electron",
        electron_dir / "node_modules" / "electron" / "dist" / "electron",
    ]
    for candidate in candidates:
        if candidate.exists():
            return candidate.resolve()
    raise FileNotFoundError("Electron binary not found; run npm install in electron/ first.")


def create_tiny_obj(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        "# live KWin menu smoke reference\n"
        "o LiveKwinSmokeTriangle\n"
        "v 0 0 0\n"
        "v 1 0 0\n"
        "v 0 1 0\n"
        "f 1 2 3\n",
        encoding="utf-8",
    )


def wait_for_log(log_path: Path, needles: list[str], proc: subprocess.Popen[bytes], timeout: float) -> bool:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if log_path.exists():
            text = log_path.read_text(encoding="utf-8", errors="replace")
            if all(needle in text for needle in needles):
                return True
        if proc.poll() is not None:
            return False
        time.sleep(0.25)
    return False


def copy_screenshot(result: str, output_dir: Path, label: str, screenshots: dict[str, str]) -> str:
    match = SCREENSHOT_RE.search(result)
    if not match:
        raise RuntimeError(f"Could not parse screenshot path from kwin-mcp result: {result}")
    src = Path(match.group("path"))
    dst = output_dir / f"{label}.png"
    shutil.copy2(src, dst)
    screenshots[label] = str(dst)
    print(f"[live-kwin-smoke] screenshot {label}: {dst}", flush=True)
    return str(dst)


def read_tail(path: Path, max_chars: int = 20000) -> str:
    if not path.exists():
        return ""
    text = path.read_text(encoding="utf-8", errors="replace")
    return text[-max_chars:]


def launch_electron(electron_dir: Path, electron_bin: Path, env: dict[str, str], app_log: Path) -> subprocess.Popen[bytes]:
    app_log.parent.mkdir(parents=True, exist_ok=True)
    log_file = app_log.open("ab")
    cmd = [
        str(electron_bin),
        ".",
        "--enable-logging",
        "--forward-renderer-console",
        "--ozone-platform=wayland",
    ]
    print(f"[live-kwin-smoke] launch: {' '.join(cmd)}", flush=True)
    proc = subprocess.Popen(
        cmd,
        cwd=electron_dir,
        env=env,
        stdout=log_file,
        stderr=subprocess.STDOUT,
        start_new_session=True,
    )
    log_file.close()
    return proc


def open_reference_load_prompt(engine: AutomationEngine, args: argparse.Namespace) -> list[dict[str, Any]]:
    actions: list[dict[str, Any]] = []

    actions.append({"action": "focus-click", "x": args.focus_x, "y": args.focus_y})
    print(engine.mouse_click(args.focus_x, args.focus_y), flush=True)
    time.sleep(0.4)

    if args.menu_mode == "keyboard":
        actions.append({"action": "keyboard", "keys": ["alt+r", "enter"], "menu_path": "Reference > Load Reference Model..."})
        print(engine.keyboard_key("alt+r"), flush=True)
        time.sleep(0.5)
        print(engine.keyboard_key("enter"), flush=True)
    else:
        actions.append(
            {
                "action": "coordinate-menu-clicks",
                "menu_path": "Reference > Load Reference Model...",
                "reference_menu": {"x": args.reference_menu_x, "y": args.reference_menu_y},
                "load_item": {"x": args.load_item_x, "y": args.load_item_y},
            }
        )
        print(engine.mouse_click(args.reference_menu_x, args.reference_menu_y), flush=True)
        time.sleep(0.5)
        print(engine.mouse_click(args.load_item_x, args.load_item_y), flush=True)

    time.sleep(0.8)
    return actions


def submit_reference_path(engine: AutomationEngine, reference_path: Path) -> dict[str, Any]:
    action = {"action": "type-reference-path", "path": str(reference_path)}
    print(engine.keyboard_type(str(reference_path)), flush=True)
    time.sleep(0.2)
    print(engine.keyboard_key("enter"), flush=True)
    return action


def main() -> int:
    args = parse_args()
    electron_dir = args.electron_dir.resolve()
    if not (electron_dir / "package.json").exists():
        raise FileNotFoundError(f"{electron_dir} does not look like the electron/ project directory")

    timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    output_dir = (args.output_dir or (electron_dir.parent / "artifacts" / "live-kwin-menu-smoke" / timestamp)).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    app_log = output_dir / "electron-live-kwin.log"
    summary_path = output_dir / "summary.json"
    screenshots: dict[str, str] = {}

    reference_path = (args.reference_path or (output_dir / "live-kwin-smoke-reference.obj")).resolve()
    if args.reference_path is None:
        create_tiny_obj(reference_path)

    if not args.no_build:
        run_build(electron_dir)

    patch_libei_variadic_bind()
    engine = AutomationEngine()
    proc: subprocess.Popen[bytes] | None = None
    actions: list[dict[str, Any]] = []
    passed = False
    error: str | None = None

    try:
        connect_result = engine.session_connect(args.dbus_address, args.wayland_display, keep_screenshots=True)
        print(connect_result, flush=True)
        if "Input backend: KWin EIS" not in connect_result:
            raise RuntimeError(f"KWin EIS input backend unavailable: {connect_result}")

        electron_bin = find_electron(electron_dir)
        env = {
            **os.environ,
            "DBUS_SESSION_BUS_ADDRESS": args.dbus_address,
            "WAYLAND_DISPLAY": args.wayland_display,
            "DISPLAY": args.display,
            "XDG_RUNTIME_DIR": args.xdg_runtime_dir,
            "ELECTRON_OZONE_PLATFORM_HINT": "wayland",
            "ELECTRON_ENABLE_LOGGING": "1",
            "VOXELFORGE_FORWARD_RENDERER_CONSOLE": "1",
            "QT_QPA_PLATFORM": "wayland",
        }
        proc = launch_electron(electron_dir, electron_bin, env, app_log)
        ready = wait_for_log(app_log, ["[electron] Sidecar ready"], proc, args.startup_timeout)
        if not ready:
            raise RuntimeError(
                f"Electron did not report sidecar readiness within {args.startup_timeout:.1f}s; see {app_log}"
            )

        time.sleep(1.0)
        copy_screenshot(engine.screenshot(include_cursor=True), output_dir, "01-baseline", screenshots)
        actions = open_reference_load_prompt(engine, args)
        copy_screenshot(engine.screenshot(include_cursor=True), output_dir, "02-prompt-visible", screenshots)
        actions.append(submit_reference_path(engine, reference_path))
        time.sleep(0.5)
        copy_screenshot(engine.screenshot(include_cursor=True), output_dir, "03-after-reference-load-submit", screenshots)
        time.sleep(args.post_action_wait)
        copy_screenshot(engine.screenshot(include_cursor=True), output_dir, "04-final", screenshots)

        log_tail = read_tail(app_log)
        required_signals = {
            "renderer_menu_event": "[renderer] Menu event received: menu:reference-model-load" in log_tail,
            "renderer_path_accepted": "[renderer] Accepted path:" in log_tail,
            "bridge_refload_request": "bridge:myra-command-execute request: refload" in log_tail,
        }
        passed = all(required_signals.values())
        if not passed:
            missing = [name for name, ok in required_signals.items() if not ok]
            raise RuntimeError(f"Missing expected live menu signals in app log: {', '.join(missing)}")

        print("[live-kwin-smoke] PASS: Reference -> Load Reference Model reached renderer and bridge request", flush=True)
        return 0
    except Exception as exc:  # noqa: BLE001 - failure evidence is the point of this script
        error = str(exc)
        print(f"[live-kwin-smoke] FAIL: {error}", file=sys.stderr, flush=True)
        try:
            copy_screenshot(engine.screenshot(include_cursor=True), output_dir, "failure", screenshots)
        except Exception as screenshot_exc:  # noqa: BLE001
            print(f"[live-kwin-smoke] could not capture failure screenshot: {screenshot_exc}", file=sys.stderr, flush=True)
        return 1
    finally:
        log_tail = read_tail(app_log)
        summary = {
            "passed": passed,
            "error": error,
            "output_dir": str(output_dir),
            "app_log": str(app_log),
            "screenshots": screenshots,
            "reference_path": str(reference_path),
            "menu_path": "Reference > Load Reference Model...",
            "actions": actions,
            "kwin": {
                "dbus_address": args.dbus_address,
                "wayland_display": args.wayland_display,
                "display": args.display,
                "input_backend": "KWin EIS",
                "libei_variadic_patch": "ctypes.c_int args + ctypes.c_void_p(NULL) sentinel",
            },
            "evidence_signals": {
                "renderer_menu_event": "[renderer] Menu event received: menu:reference-model-load" in log_tail,
                "renderer_path_accepted": "[renderer] Accepted path:" in log_tail,
                "bridge_refload_request": "bridge:myra-command-execute request: refload" in log_tail,
                "bridge_refload_response": "bridge:myra-command-execute response:" in log_tail,
                "bridge_refload_failed": "bridge:myra-command-execute failed:" in log_tail,
            },
            "log_tail": log_tail[-4000:],
        }
        summary_path.write_text(json.dumps(summary, indent=2), encoding="utf-8")
        print(f"[live-kwin-smoke] summary: {summary_path}", flush=True)

        if proc is not None and proc.poll() is None:
            try:
                os.killpg(proc.pid, signal.SIGTERM)
                proc.wait(timeout=5)
            except Exception:
                with contextlib.suppress(ProcessLookupError):
                    os.killpg(proc.pid, signal.SIGKILL)
        with contextlib.suppress(Exception):
            engine.session_stop()


if __name__ == "__main__":
    raise SystemExit(main())
