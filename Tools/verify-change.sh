#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

python3 Tools/verify-architecture.py

if [[ "${1:-}" == "--architecture-only" ]]; then exit 0; fi

base="${VERIFY_BASE:-}"
if [[ -z "$base" && -n "${GITHUB_BASE_REF:-}" ]]; then base="origin/$GITHUB_BASE_REF"; fi
if [[ -z "$base" ]] && git rev-parse --verify origin/main >/dev/null 2>&1; then base="origin/main"; fi
if [[ -z "$base" ]]; then base="HEAD~1"; fi

git diff --check "$base"...HEAD

changed="$(git diff --name-only "$base"...HEAD || true)"
if [[ -n "$changed" ]] && ! grep -Eq '^(Packages/|Tools/Harness/|Tools/RemoteCli/)' <<<"$changed"; then
  echo "No package/harness/CLI code changed; architecture verification is sufficient."
  exit 0
fi

Tools/Harness/run.sh
