#!/usr/bin/env bash
# Runs the test suites mapped by uat-coverage.json and writes uat-status.json (item ->
# passed/failed/total + a UTC stamp) parsed from the per-run .trx files. The UAT console reads the
# result to render each automated-coverage item as a live ✓/✗ with counts and a last-run time. The
# browser never triggers tests; this is the explicit, out-of-band refresh step.
#
# Usage:  scripts/write-uat-status.sh
# Run from the repo root (the directory containing the .sln).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COVERAGE="$ROOT/src/DfE.CheckPerformanceData.Web/wwwroot/uat/uat-coverage.json"
STATUS="$ROOT/src/DfE.CheckPerformanceData.Web/wwwroot/uat/uat-status.json"
WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

if [[ ! -f "$COVERAGE" ]]; then
  echo "uat-coverage.json not found at $COVERAGE" >&2
  exit 1
fi

command -v jq >/dev/null 2>&1 || { echo "jq is required" >&2; exit 1; }

NOW="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

# Run each distinct (project, filter) once, capturing a trx per item. Counts are read back from the
# trx ResultSummary. A failed run still produces a trx, so a red item shows real counts not a gap.
ITEMS_JSON="[]"

count_items="$(jq '.items | length' "$COVERAGE")"
for i in $(seq 0 $((count_items - 1))); do
  id="$(jq -r ".items[$i].id" "$COVERAGE")"
  project="$(jq -r ".items[$i].project" "$COVERAGE")"
  filter="$(jq -r ".items[$i].filter" "$COVERAGE")"
  trx="$WORKDIR/$id.trx"

  echo "==> $id  ($project)  --filter \"$filter\""
  set +e
  dotnet test "$ROOT/tests/$project" \
    --filter "$filter" \
    --logger "trx;LogFileName=$trx" \
    --nologo --verbosity quiet >/dev/null 2>&1
  set -e

  passed=0; failed=0; total=0
  if [[ -f "$trx" ]]; then
    # The ResultSummary/Counters element carries the totals as attributes.
    total="$(grep -oE 'total="[0-9]+"' "$trx" | head -1 | grep -oE '[0-9]+' || echo 0)"
    passed="$(grep -oE 'passed="[0-9]+"' "$trx" | head -1 | grep -oE '[0-9]+' || echo 0)"
    failed="$(grep -oE 'failed="[0-9]+"' "$trx" | head -1 | grep -oE '[0-9]+' || echo 0)"
  fi

  ITEMS_JSON="$(jq \
    --arg id "$id" --argjson passed "${passed:-0}" --argjson failed "${failed:-0}" --argjson total "${total:-0}" \
    '. + [{id: $id, passed: $passed, failed: $failed, total: $total}]' <<<"$ITEMS_JSON")"
done

jq -n --arg now "$NOW" --argjson items "$ITEMS_JSON" \
  '{generatedAtUtc: $now, items: $items}' >"$STATUS"

echo "Wrote $STATUS"
