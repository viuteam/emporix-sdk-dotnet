#!/usr/bin/env bash
# Records the public API surface in PublicAPI.Unshipped.txt.
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
build_output="$(dotnet build --nologo 2>&1 || true)"

symbols="$(printf '%s\n' "$build_output" \
  | grep -o "RS0016: Symbol '[^']*'" \
  | sed "s/RS0016: Symbol '//; s/'$//" \
  | sort -u || true)"

if [[ -z "$symbols" ]]; then
  echo "No missing entries — PublicAPI.Unshipped.txt is up to date."
  exit 0
fi

{
  printf '#nullable enable\n'
  # Keep existing entries: Unshipped accumulates until the next release.
  tail -n +2 "$api_file" 2>/dev/null || true
  printf '%s\n' "$symbols"
} | awk 'NR==1 || (!seen[$0]++ && $0 != "#nullable enable")' > "$api_file.tmp"

mv "$api_file.tmp" "$api_file"
echo "$(($(wc -l < "$api_file") - 1)) symbols in $api_file."
