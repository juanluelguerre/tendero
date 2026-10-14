#!/usr/bin/env bash
# A phase close's Stryker runs, one project at a time (docs/testing.md, Level 2).
#
#   bash .claude/skills/phase-close/stryker-run.sh <plan file> <output folder>
#
# The plan has one line a project: its folder under src/ and, when the phase
# touched it in a few files only, the files to mutate.
#
#   Pricing
#   SharedKernel -m "Refusals.cs" -m "WireMoney.cs"
#   Ordering -m "OrderPlacement.cs" -m "Features/Checkout/Checkout.cs"
#
# Two lessons are built in:
# - A configuration that names Integration.Tests is run with its other test
#   projects only. With the Postgres suite in the run, Testcontainers starts a
#   database for every mutant, and phase 13b's Ordering run hung for eight hours.
# - Every project has a ceiling (STRYKER_MINUTES, 120 by default). A run that
#   reaches it is stopped and said to be, never waited on overnight.
set -u
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
PLAN="$1"
OUT="$2"
MINUTES="${STRYKER_MINUTES:-120}"
mkdir -p "$OUT"
SUMMARY="$OUT/stryker-summary.txt"

( cd "$ROOT" && dotnet tool restore > /dev/null )

while IFS= read -r line || [ -n "$line" ]; do
  line="${line%%#*}"
  [ -z "${line// }" ] && continue
  project="${line%% *}"
  rest=""
  [ "$line" != "$project" ] && rest="${line#* }"
  dir="$ROOT/src/$project"
  config="$dir/stryker-config.json"
  if [ ! -f "$config" ]; then
    echo "== $project: no stryker-config.json, skipped (prove its phase code by hand mutations)" | tee -a "$SUMMARY"
    continue
  fi

  # The unit suites only, when the configuration names the integration suite.
  units=""
  if grep -q "Integration.Tests" "$config"; then
    for tp in $(grep -oE '"[^"]*\.Tests\.csproj"' "$config" | tr -d '"' | grep -v "Integration.Tests"); do
      units="$units --test-project $tp"
    done
  fi

  log="$OUT/stryker-$project.log"
  echo "== $project $(date +%H:%M:%S) :: $rest${units:+ (units only:$units)}" | tee -a "$SUMMARY"
  if [ -n "${STRYKER_DRY:-}" ]; then
    # What would run, and nothing else: to check a plan before the hours it costs.
    echo "   (dry) cd src/$project && timeout ${MINUTES}m dotnet tool run dotnet-stryker --config-file stryker-config.json --skip-version-check$units $rest" | tee -a "$SUMMARY"
    continue
  fi
  # shellcheck disable=SC2086 # the plan's -m arguments and the test projects are meant to split
  ( cd "$dir" && eval timeout "${MINUTES}m" dotnet tool run dotnet-stryker --config-file stryker-config.json --skip-version-check $units $rest ) > "$log" 2>&1
  code=$?
  if [ $code -eq 124 ]; then
    echo "   STOPPED after $MINUTES minutes: classify what it reached, or run it scoped" >> "$SUMMARY"
  else
    echo "   exit $code $(date +%H:%M:%S)" >> "$SUMMARY"
  fi
  sed 's/\x1b\[[0-9;]*m//g' "$log" | grep -E "mutation score|mutants (created|tested)|Unable|error" | tail -6 >> "$SUMMARY"
done < "$PLAN"

echo "== done $(date +%H:%M:%S)" >> "$SUMMARY"
