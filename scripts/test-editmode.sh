#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# Windows Python cannot read Git Bash's /c/... paths.
if command -v cygpath >/dev/null 2>&1; then
    script_dir="$(cygpath -w "$script_dir")"
fi
exec python "$script_dir/test_editmode.py" "$@"
