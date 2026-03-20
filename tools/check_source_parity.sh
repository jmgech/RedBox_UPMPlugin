#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE_DIR="${1:-${REDBOX_SOURCE_PACKAGE_DIR:-}}"

if [[ -z "$SOURCE_DIR" ]]; then
  # Common local mono-workspace layout used by this project.
  FALLBACK="$ROOT_DIR/../REDbox_Project/Packages/com.redbox.unity"
  if [[ -d "$FALLBACK" ]]; then
    SOURCE_DIR="$FALLBACK"
  fi
fi

if [[ -z "$SOURCE_DIR" ]]; then
  echo "[parity] ERROR: source package directory not provided."
  echo "[parity] Usage: tools/check_source_parity.sh <path-to-com.redbox.unity>"
  echo "[parity] Or set REDBOX_SOURCE_PACKAGE_DIR env var."
  exit 2
fi

if [[ ! -d "$SOURCE_DIR" ]]; then
  echo "[parity] ERROR: source package directory does not exist: $SOURCE_DIR"
  exit 2
fi

PLUGIN_RUNTIME="$ROOT_DIR/Runtime/"
PLUGIN_EDITOR="$ROOT_DIR/Editor/"
SOURCE_RUNTIME="$SOURCE_DIR/Runtime/"
SOURCE_EDITOR="$SOURCE_DIR/Editor/"

if [[ ! -d "$SOURCE_RUNTIME" || ! -d "$SOURCE_EDITOR" ]]; then
  echo "[parity] ERROR: source package is missing Runtime/ or Editor/: $SOURCE_DIR"
  exit 2
fi

compare_tree() {
  local src="$1"
  local dst="$2"
  local ignore_file="$3"
  rsync -anic --delete \
    --include='*/' \
    --exclude-from="$ignore_file" \
    --include='*.cs' \
    --include='*.meta' \
    --include='*.asmdef' \
    --exclude='*' \
    "$src" "$dst"
}

runtime_ignore="$ROOT_DIR/tools/parity-ignore-runtime.txt"
editor_ignore="$ROOT_DIR/tools/parity-ignore-editor.txt"

if [[ ! -f "$runtime_ignore" || ! -f "$editor_ignore" ]]; then
  echo "[parity] ERROR: parity ignore files are missing under tools/."
  exit 2
fi

runtime_diff="$(compare_tree "$SOURCE_RUNTIME" "$PLUGIN_RUNTIME" "$runtime_ignore")"
editor_diff="$(compare_tree "$SOURCE_EDITOR" "$PLUGIN_EDITOR" "$editor_ignore")"

if [[ -n "$runtime_diff" || -n "$editor_diff" ]]; then
  echo "[parity] FAIL: plugin differs from source package."
  if [[ -n "$runtime_diff" ]]; then
    echo "[parity] Runtime drift:"
    echo "$runtime_diff"
  fi
  if [[ -n "$editor_diff" ]]; then
    echo "[parity] Editor drift:"
    echo "$editor_diff"
  fi
  exit 1
fi

echo "[parity] OK: Runtime/ and Editor/ are in parity with source package."
