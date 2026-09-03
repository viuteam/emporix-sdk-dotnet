#!/usr/bin/env bash
# Moves the recorded public API from Unshipped to Shipped.
#
# Microsoft.CodeAnalysis.PublicApiAnalyzers treats Shipped as the published
# baseline: from the moment a symbol is listed there, removing or changing it is
# reported as a break (RS0017 and friends). Unshipped is the staging area that
# scripts/update-public-api.sh fills as new symbols appear.
#
# This runs at release time — automatically, on the release pull request. Doing it
# by hand is the sort of step that is remembered for the first two releases and
# then never again, and its absence is invisible: everything keeps building, and
# breaking changes simply stop being detected.
#
# Idempotent. Running it on an already-promoted tree changes nothing.
set -euo pipefail

cd "$(dirname "$0")/.."

shipped="src/Viu.Emporix/PublicAPI.Shipped.txt"
unshipped="src/Viu.Emporix/PublicAPI.Unshipped.txt"
header="#nullable enable"

for file in "$shipped" "$unshipped"; do
  if [[ ! -f "$file" ]]; then
    echo "Missing $file." >&2
    exit 1
  fi
done

# Everything but the header, from both files. Sorting the union is what the
# analyzer's own code fix produces, and it keeps the diff readable.
#
# A «*REMOVED*<signature>» line in Unshipped retires a symbol that was already
# shipped — the analyzer's own way of recording a deliberate break. Promoting it
# means dropping the signature from Shipped and dropping the marker with it;
# carrying either forward would leave Shipped describing an API that no longer
# exists, and the next build would fail on RS0017 during a release.
removed="$(grep -h '^\*REMOVED\*' "$unshipped" 2>/dev/null | sed 's/^\*REMOVED\*//' || true)"

entries="$(cat "$shipped" "$unshipped" \
  | grep -vxF "$header" \
  | grep -v '^[[:space:]]*$' \
  | grep -v '^\*REMOVED\*' \
  | sort -u || true)"

if [[ -n "$removed" ]]; then
  entries="$(printf '%s\n' "$entries" | grep -vxF "$removed" || true)"
  retired="$(printf '%s\n' "$removed" | wc -l | tr -d ' ')"
  echo "Retiring $retired shipped symbol(s) marked *REMOVED*."
fi

if [[ -z "$entries" ]]; then
  echo "Nothing recorded in either file — leaving both alone."
  exit 0
fi

before="$(grep -cvxF "$header" "$unshipped" || true)"

{
  printf '%s\n' "$header"
  printf '%s\n' "$entries"
} > "$shipped"

printf '%s\n' "$header" > "$unshipped"

count="$(printf '%s\n' "$entries" | wc -l | tr -d ' ')"

if [[ "$before" -eq 0 ]]; then
  echo "Already promoted: $count symbol(s) in Shipped, Unshipped empty."
else
  echo "Promoted $before symbol(s). Shipped now holds $count."
fi
