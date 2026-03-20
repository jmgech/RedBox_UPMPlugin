#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GUID_MAP_FILE="$ROOT_DIR/tools/stable-script-guids.txt"

if [[ ! -f "$GUID_MAP_FILE" ]]; then
  echo "[guid] ERROR: GUID map file not found: $GUID_MAP_FILE"
  exit 2
fi

failures=0
while IFS='=' read -r relative_path expected_guid; do
  [[ -z "$relative_path" ]] && continue
  [[ "$relative_path" =~ ^# ]] && continue

  target="$ROOT_DIR/$relative_path"
  if [[ ! -f "$target" ]]; then
    echo "[guid] FAIL: file missing: $relative_path"
    failures=$((failures + 1))
    continue
  fi

  actual_guid="$(sed -n 's/^guid: //p' "$target" | head -n1)"
  if [[ -z "$actual_guid" ]]; then
    echo "[guid] FAIL: no guid found in $relative_path"
    failures=$((failures + 1))
    continue
  fi

  if [[ "$actual_guid" != "$expected_guid" ]]; then
    echo "[guid] FAIL: GUID mismatch in $relative_path"
    echo "[guid]   expected: $expected_guid"
    echo "[guid]   actual  : $actual_guid"
    failures=$((failures + 1))
  fi
done < "$GUID_MAP_FILE"

if [[ "$failures" -gt 0 ]]; then
  echo "[guid] FAIL: $failures GUID mismatch(es) detected."
  exit 1
fi

echo "[guid] OK: stable script GUIDs are unchanged."
