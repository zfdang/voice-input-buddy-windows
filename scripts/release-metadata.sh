#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Full history is necessary: a shallow checkout gives an incorrect commit count.
if [[ "$(git rev-parse --is-shallow-repository 2>/dev/null || echo false)" == "true" ]]; then
    echo "Warning: Shallow checkout detected, commit count may be incomplete." >&2
fi

count="$(git rev-list --count HEAD 2>/dev/null || echo 1)"
commit_time="$(git show -s --format=%ct HEAD 2>/dev/null || date +%s)"
date_part="$(python3 - "$commit_time" <<'PY'
from datetime import datetime
from zoneinfo import ZoneInfo
import sys
try:
    ts = int(sys.argv[1])
    print(datetime.fromtimestamp(ts, ZoneInfo('Asia/Singapore')).strftime('%y-%m-%d'))
except Exception:
    print(datetime.now().strftime('%y-%m-%d'))
PY
)"

printf 'RELEASE_TAG=v%s.%03d\n' "$date_part" "$((count % 1000))"
printf 'RELEASE_BUNDLE_VERSION=%s\n' "${date_part//-/.}"
printf 'RELEASE_COMMIT_COUNT=%s\n' "$count"
printf 'RELEASE_PACKAGE_VERSION=%s.%s\n' "${date_part//-/.}" "$count"
