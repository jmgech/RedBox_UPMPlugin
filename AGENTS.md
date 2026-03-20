# REDbox_Plugin — Release Agent Rules

This repository is the distributable Unity package (`com.redbox.unity`).

## Canonical Source

- Runtime and Editor code source of truth is:
  - `../REDbox_Project/Packages/com.redbox.unity`
- Plugin repo must stay in parity with that source except explicit allowed differences.

## Mandatory Release Flow

1. Sync source package changes into this repo.
2. Run parity check:
   - `bash tools/check_source_parity.sh ../REDbox_Project/Packages/com.redbox.unity`
3. Run script GUID stability check:
   - `bash tools/check_guid_stability.sh`
4. Bump `package.json` version and update `CHANGELOG.md`.
5. Commit and tag a milestone/stable tag.

## Allowed Intentional Differences

- Differences intentionally kept in plugin are listed in:
  - `tools/parity-ignore-runtime.txt`
  - `tools/parity-ignore-editor.txt`

Do not add entries casually. Every new ignore must be justified in commit message and changelog.

## Script GUID Safety (Critical)

Unity scene/prefab references rely on script GUIDs. Never regenerate or change GUIDs for stable runtime scripts.

Expected GUID mapping is tracked in:
- `tools/stable-script-guids.txt`

If a GUID change is intentional (rare), migration impact must be documented and approved.
