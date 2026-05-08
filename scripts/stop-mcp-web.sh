#!/usr/bin/env bash
set -euo pipefail

PORT="${VOXELFORGE_MCP_PORT:-5201}"
TMP_DIR="${VOXELFORGE_TMP_DIR:-/tmp/voxelforge}"
PID_FILE="${VOXELFORGE_MCP_PID_FILE:-$TMP_DIR/mcp-web.pid}"
LOCK_FILE="${VOXELFORGE_MCP_LOCK_FILE:-$TMP_DIR/mcp-web.lock}"
HEALTH_URL="${VOXELFORGE_MCP_HEALTH_URL:-http://localhost:$PORT/health}"

mkdir -p "$TMP_DIR"
exec 9>"$LOCK_FILE"
flock 9

is_running() {
  local pid="$1"
  [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null
}

cmdline_for_pid() {
  local pid="$1"
  tr '\0' ' ' < "/proc/$pid/cmdline" 2>/dev/null || true
}

listener_pid() {
  ss -ltnp "sport = :$PORT" 2>/dev/null \
    | sed -n 's/.*pid=\([0-9][0-9]*\).*/\1/p' \
    | head -n 1
}

root_pid_for_listener() {
  local pid="$1"
  local parent=""
  parent="$(awk '/^PPid:/ {print $2}' "/proc/$pid/status" 2>/dev/null || true)"
  if [[ -n "$parent" ]] && is_running "$parent"; then
    local parent_cmd
    parent_cmd="$(cmdline_for_pid "$parent")"
    if [[ "$parent_cmd" == *"dotnet run --project"*"VoxelForge.Mcp"* ]]; then
      printf '%s\n' "$parent"
      return 0
    fi
  fi
  printf '%s\n' "$pid"
}

collect_tree() {
  local pid="$1"
  local child
  for child in $(pgrep -P "$pid" 2>/dev/null || true); do
    collect_tree "$child"
  done
  printf '%s\n' "$pid"
}

stop_pid_tree() {
  local pid="$1"
  is_running "$pid" || return 0

  local pids
  pids="$(collect_tree "$pid" | awk 'NF' | tac | tr '\n' ' ')"
  # shellcheck disable=SC2086
  kill $pids 2>/dev/null || true

  for _ in $(seq 1 20); do
    is_running "$pid" || return 0
    sleep 0.25
  done

  # shellcheck disable=SC2086
  kill -9 $pids 2>/dev/null || true
}

stop_if_voxelforge() {
  local pid="$1"
  local cmd
  if ! is_running "$pid"; then
    return 1
  fi
  cmd="$(cmdline_for_pid "$pid")"
  if [[ "$cmd" != *"VoxelForge.Mcp"* && "$cmd" != *"dotnet run --project"*"VoxelForge.Mcp"* ]]; then
    printf 'Refusing to stop non-VoxelForge process: %s (%s)\n' "$pid" "$cmd" >&2
    exit 1
  fi
  printf 'Stopping VoxelForge MCP web server process tree rooted at PID %s...\n' "$pid"
  stop_pid_tree "$pid"
  return 0
}

stopped=false
if [[ -f "$PID_FILE" ]]; then
  pid="$(cat "$PID_FILE" 2>/dev/null || true)"
  if [[ -n "$pid" ]] && is_running "$pid"; then
    stop_if_voxelforge "$pid"
    stopped=true
  fi
  rm -f "$PID_FILE"
fi

lpid="$(listener_pid || true)"
if [[ -n "$lpid" ]] && is_running "$lpid"; then
  root="$(root_pid_for_listener "$lpid")"
  stop_if_voxelforge "$root"
  stopped=true
fi

if [[ "$stopped" == true ]]; then
  printf 'Stopped VoxelForge MCP web server on port %s.\n' "$PORT"
else
  printf 'No VoxelForge MCP web server found for port %s.\n' "$PORT"
fi

if curl -fsS --max-time 2 "$HEALTH_URL" >/dev/null 2>&1; then
  printf 'Warning: health endpoint still responds at %s.\n' "$HEALTH_URL" >&2
  exit 1
fi
