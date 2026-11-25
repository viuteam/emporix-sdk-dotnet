#!/usr/bin/env bash
# Records the public API surface in PublicAPI.Unshipped.txt — additions and removals.
#
# Microsoft.CodeAnalysis.PublicApiAnalyzers reports every public symbol that is
# not yet declared as RS0016. IDEs offer a code fix for this; on the command line
# this script does the job.
#
# At release time the contents of Unshipped move to Shipped — from then on the
# analyzer reports any change to an already-published symbol as a break.
set -euo pipefail

cd "$(dirname "$0")/.."
api_file="src/Viu.Emporix/PublicAPI.Unshipped.txt"

# Pin the output language, otherwise the pattern depends on the machine locale.
export DOTNET_CLI_UI_LANGUAGE=en

# The build fails while entries are missing — which is the normal case here.
# Arguments go to dotnet build: the spec-sync sandbox passes -m:1, because it
# refuses the sockets MSBuild's worker nodes need.
build_status=0
build_output="$(dotnet build --nologo "$@" 2>&1)" || build_status=$?

symbols="$(printf '%s\n' "$build_output" \
  | grep -o "RS0016: Symbol '[^']*'" \
  | sed "s/RS0016: Symbol '//; s/'$//" \
  | sort -u || true)"

# The other direction. A symbol that the baseline declares and the assembly no
# longer has is RS0017, and it is recorded by a «*REMOVED*» line rather than by
# deleting the entry — promote-public-api.sh reads those when moving Unshipped
# into Shipped, so a deletion would leave the shipped entry standing forever.
#
# This half was missing until the first change that removed a public symbol.
# Every earlier change only added, so the script only ever needed RS0016, and
# the gap looked like a script that had stopped working.
removed="$(printf '%s\n' "$build_output" \
  | grep -o "RS0017: Symbol '[^']*'" \
  | sed "s/RS0017: Symbol '/*REMOVED*/; s/'$//" \
  | sort -u || true)"

if [[ -z "$symbols" && -z "$removed" ]]; then
  # A build that failed for another reason reports no RS0016 either, and
  # calling that «up to date» hid an MSBuild failure in the spec-sync sandbox.
  if [[ $build_status -ne 0 ]]; then
    printf '%s\n' "$build_output" | tail -n 20 >&2
    echo "The build failed without a missing API entry — fix the build first." >&2
    exit 1
  fi
  echo "No missing entries — PublicAPI.Unshipped.txt is up to date."
  exit 0
fi

{
  printf '#nullable enable\n'
  # Keep existing entries: Unshipped accumulates until the next release.
  tail -n +2 "$api_file" 2>/dev/null || true
  # «test && printf» would abort the whole group under «set -e» whenever the
  # variable is empty, which is the normal case for one of the two. An if does
  # not, and this script writes the file that gates every build.
  if [[ -n "$symbols" ]]; then printf '%s\n' "$symbols"; fi
  if [[ -n "$removed" ]]; then printf '%s\n' "$removed"; fi
} | awk 'NR==1 || (!seen[$0]++ && $0 != "#nullable enable")' > "$api_file.tmp"

mv "$api_file.tmp" "$api_file"
echo "$(($(wc -l < "$api_file") - 1)) entries in $api_file."

if [[ -n "$removed" ]]; then
  echo "$(printf '%s\n' "$removed" | wc -l | tr -d ' ') of them record a removed symbol."
fi
