#!/usr/bin/env bash
# Packs the template, installs the package into a throwaway hive, then instantiates option combinations
# and builds + unit-tests each one. Your real `dotnet new` configuration is never touched and Docker is
# not needed (integration tests, images and charts are covered by CI).
#
#   eng/verify-combinations.sh                 # every combination
#   eng/verify-combinations.sh both            # only combinations whose arguments contain "both"
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
filter=${1:-}
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

echo "Packing template..."
dotnet pack "$root/src/GoldenPath.Templates" -c Release -o "$work/pkg" --nologo -v quiet
dotnet new install "$work"/pkg/GoldenPath.Templates.*.nupkg --debug:custom-hive "$work/hive" >/dev/null

combinations=()
for observability in otlp appinsights both; do
  combinations+=("--observability $observability")
done

passed=0
failed=()
for combination in "${combinations[@]}"; do
  [[ -n "$filter" && "$combination" != *"$filter"* ]] && continue
  dir="$work/svc-$((passed + ${#failed[@]}))"
  echo "==> $combination"
  # shellcheck disable=SC2086
  if dotnet new goldenpath-api --debug:custom-hive "$work/hive" -n Orders -o "$dir" $combination >/dev/null \
    && dotnet build "$dir" -c Release --nologo -v quiet \
    && dotnet test "$dir" -c Release --no-build --nologo --filter "Category!=Integration"; then
    passed=$((passed + 1))
  else
    failed+=("$combination")
  fi
done

echo
echo "Passed: $passed, failed: ${#failed[@]}"
for combination in "${failed[@]}"; do echo "  FAILED: $combination"; done
[[ ${#failed[@]} -eq 0 ]]
