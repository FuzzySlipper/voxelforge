#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

PORT="${VOXELFORGE_MCP_PORT:-5201}"
PROJECT_DIR="${VOXELFORGE_MCP_PROJECT_DIR:-content}"
TMP_DIR="${VOXELFORGE_TMP_DIR:-/tmp/voxelforge}"
PID_FILE="${VOXELFORGE_MCP_PID_FILE:-$TMP_DIR/mcp-web.pid}"
LOG_FILE="${VOXELFORGE_MCP_LOG_FILE:-$TMP_DIR/mcp-web.log}"
LOCK_FILE="${VOXELFORGE_MCP_LOCK_FILE:-$TMP_DIR/mcp-web.lock}"
HEALTH_URL="${VOXELFORGE_MCP_HEALTH_URL:-http://localhost:$PORT/health}"
VIEWER_URL="${VOXELFORGE_MCP_VIEWER_URL:-http://localhost:$PORT/viewer}"
START_TIMEOUT_SECONDS="${VOXELFORGE_MCP_START_TIMEOUT_SECONDS:-60}"

mkdir -p "$TMP_DIR"
exec 9>"$LOCK_FILE"
flock 9

is_running() {
  local pid="$1"
  [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null
}

health_ok() {
  curl -fsS --max-time 2 "$HEALTH_URL" >/dev/null 2>&1
}

listener_pid() {
  ss -ltnp "sport = :$PORT" 2>/dev/null \
    | sed -n 's/.*pid=\([0-9][0-9]*\).*/\1/p' \
    | head -n 1
}

cmdline_for_pid() {
  local pid="$1"
  tr '\0' ' ' < "/proc/$pid/cmdline" 2>/dev/null || true
}

root_pid_for_listener() {
  local pid="$1"
  local current="$pid"
  local parent=""
  local parent_cmd=""

  while true; do
    parent="$(awk '/^PPid:/ {print $2}' "/proc/$current/status" 2>/dev/null || true)"
    if [[ -z "$parent" || "$parent" == "0" || "$parent" == "1" ]] || ! is_running "$parent"; then
      break
    fi

    parent_cmd="$(cmdline_for_pid "$parent")"
    if [[ "$parent_cmd" == *"dotnet run --project"*"VoxelForge.Mcp"* ]]; then
      printf '%s\n' "$parent"
      return 0
    fi

    current="$parent"
  done

  printf '%s\n' "$pid"
}

adopt_running_server() {
  local lpid root
  lpid="$(listener_pid || true)"
  printf 'VoxelForge MCP web server already healthy.\n'
  if [[ -n "$lpid" ]] && is_running "$lpid"; then
    root="$(root_pid_for_listener "$lpid")"
    printf '%s\n' "$root" > "$PID_FILE"
    printf 'PID: %s\n' "$root"
  else
    printf 'PID: unavailable (healthy endpoint responded, but listener PID was not discoverable)\n'
  fi
  printf 'Health: %s\n' "$HEALTH_URL"
  printf 'Viewer: %s\n' "$VIEWER_URL"
  printf 'Log: %s\n' "$LOG_FILE"
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

cleanup_stale_pid_file() {
  if [[ ! -f "$PID_FILE" ]]; then
    return 0
  fi

  local pid
  pid="$(cat "$PID_FILE" 2>/dev/null || true)"
  if ! is_running "$pid"; then
    rm -f "$PID_FILE"
    return 0
  fi

  if health_ok; then
    return 0
  fi

  local cmd
  cmd="$(cmdline_for_pid "$pid")"
  if [[ "$cmd" == *"VoxelForge.Mcp"* || "$cmd" == *"dotnet run --project"*"VoxelForge.Mcp"* ]]; then
    printf 'Stopping stale VoxelForge MCP process from %s: %s\n' "$PID_FILE" "$pid"
    stop_pid_tree "$pid"
    rm -f "$PID_FILE"
  else
    printf 'Refusing to stop non-VoxelForge process from %s: %s (%s)\n' "$PID_FILE" "$pid" "$cmd" >&2
    exit 1
  fi
}

cleanup_stale_listener() {
  local lpid cmd root
  lpid="$(listener_pid || true)"
  [[ -n "$lpid" ]] || return 0

  if health_ok; then
    return 0
  fi

  cmd="$(cmdline_for_pid "$lpid")"
  if [[ "$cmd" != *"VoxelForge.Mcp"* ]]; then
    printf 'Port %s is occupied by a non-VoxelForge process: %s (%s)\n' "$PORT" "$lpid" "$cmd" >&2
    exit 1
  fi

  root="$(root_pid_for_listener "$lpid")"
  printf 'Stopping stale VoxelForge MCP listener on port %s: %s\n' "$PORT" "$root"
  stop_pid_tree "$root"
}

if health_ok; then
  adopt_running_server
  exit 0
fi

cleanup_stale_pid_file
cleanup_stale_listener

printf 'Starting VoxelForge MCP web server on port %s...\n' "$PORT"
: > "$LOG_FILE"
(
  # Do not let the long-running server inherit the ensure-script lock;
  # otherwise later ensure/stop invocations would block until the server exits.
  exec 9>&-
  cd "$REPO_ROOT"
  exec dotnet run --project src/VoxelForge.Mcp -- --port "$PORT" --project-dir "$PROJECT_DIR"
) > "$LOG_FILE" 2>&1 &
server_pid="$!"
printf '%s\n' "$server_pid" > "$PID_FILE"

for _ in $(seq 1 "$START_TIMEOUT_SECONDS"); do
  if health_ok; then
    printf 'VoxelForge MCP web server started.\n'
    printf 'PID: %s\n' "$server_pid"
    printf 'Health: %s\n' "$HEALTH_URL"
    printf 'Viewer: %s\n' "$VIEWER_URL"
    printf 'Log: %s\n' "$LOG_FILE"
    exit 0
  fi
  if ! is_running "$server_pid"; then
    printf 'VoxelForge MCP web server exited before becoming healthy. Log follows:\n' >&2
    tail -n 80 "$LOG_FILE" >&2 || true
    exit 1
  fi
  sleep 1
done

printf 'Timed out waiting for VoxelForge MCP web server health at %s. Log follows:\n' "$HEALTH_URL" >&2
tail -n 80 "$LOG_FILE" >&2 || true
exit 1
