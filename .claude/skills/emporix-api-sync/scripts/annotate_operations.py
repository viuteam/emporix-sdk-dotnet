#!/usr/bin/env python3
"""Annotate Emporix spec operations with their operationId and OAuth scopes.

SpecPathTests decides *which* operations lack a facade — that logic lives in the
test and is not repeated here. This script answers the next question: for a given
operation, what is it called upstream and what scope does it need? The scope is
what picks `Defaults.Service` over `Defaults.Anonymous`, so it is needed for
every gap that gets filled.

    # annotate the list SpecPathTests printed as `Actual:`
    annotate_operations.py "PATCH /media/{}/assets/{}" "HEAD /order-v2/{}/salesorders"

    # run the test, take its uncovered list, annotate that
    annotate_operations.py --from-test

    # everything one spec declares
    annotate_operations.py --spec media

Paths are normalised the way the test normalises them — every `{param}` becomes
`{}` — so a key from its output can be pasted in unchanged.

`--from-test` runs the coverage test itself rather than reading a log, so the
build has to be current: run `dotnet build` first or it measures stale binaries.
When the test passes it says so and exits 0 — «no operation is uncovered beyond
the known gaps», which is not the same as «nothing is missing». The known gaps
are in `SpecPathTests.cs` and are frequently what someone is asking about; pass
them as arguments to see what they are.

No YAML dependency on purpose: the repo's own sync tool reads `info.version`
with a regex rather than taking one, and this needs no more than that.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

VERBS = ("get", "put", "post", "patch", "delete", "head", "options", "trace")


def repository_root(start: Path) -> Path:
    for candidate in [start, *start.parents]:
        if (candidate / "specs").is_dir() and (candidate / "src").is_dir():
            return candidate
    sys.exit("not inside the SDK repository — no specs/ and src/ above this file")


def normalise(path: str) -> str:
    return re.sub(r"/+", "/", re.sub(r"\{[^}]*\}", "{}", path))


def parse_spec(text: str) -> list[dict]:
    """Every operation in one specification.

    Indentation carries the structure: `paths:` at column 0, a path at two
    spaces, a verb at four, and the operation's own keys below that. Anything
    deeper than the verb belongs to the operation, which is enough to find an
    operationId, a security block and a deprecation without understanding YAML.
    """
    operations: list[dict] = []
    in_paths = False
    path = None
    current: dict | None = None
    in_security = False

    for raw in text.splitlines():
        if not raw.strip() or raw.lstrip().startswith("#"):
            continue

        indent = len(raw) - len(raw.lstrip())
        stripped = raw.strip()

        if indent == 0:
            in_paths = stripped == "paths:"
            path = None
            current = None
            continue

        if not in_paths:
            continue

        if indent == 2 and stripped.endswith(":"):
            path = stripped[:-1].strip().strip("'\"")
            current = None
            in_security = False
            continue

        if indent == 4 and path is not None:
            verb = stripped[:-1].strip().lower() if stripped.endswith(":") else None
            in_security = False
            if verb in VERBS:
                current = {
                    "method": verb.upper(),
                    "path": path,
                    "key": f"{verb.upper()} {normalise(path)}",
                    "operationId": None,
                    "summary": None,
                    "scopes": [],
                    "deprecated": False,
                }
                operations.append(current)
            else:
                current = None
            continue

        if current is None:
            continue

        if indent == 6:
            in_security = stripped == "security:"
            if stripped.startswith("operationId:"):
                current["operationId"] = stripped.split(":", 1)[1].strip().strip("'\"")
            elif stripped.startswith("summary:"):
                current["summary"] = stripped.split(":", 1)[1].strip().strip("'\"")
            elif stripped.startswith("deprecated:"):
                current["deprecated"] = stripped.split(":", 1)[1].strip() == "true"
            continue

        # A scope is a list item under security's provider, at any depth below
        # it. Stopping at indent 6 above means anything reaching here while
        # in_security is a scope or the provider name itself.
        if in_security and stripped.startswith("- ") and ":" not in stripped:
            current["scopes"].append(stripped[2:].strip())

    return operations


def load(root: Path, only: str | None) -> list[dict]:
    files = sorted((root / "specs").glob("*.yml"))
    if only:
        files = [f for f in files if f.stem == only]
        if not files:
            sys.exit(f"no specs/{only}.yml")

    found: list[dict] = []
    for f in files:
        for op in parse_spec(f.read_text(encoding="utf-8")):
            op["spec"] = f.stem
            found.append(op)
    return found


def uncovered_from_test(root: Path) -> list[str]:
    """Run the coverage test and read the uncovered set out of its failure."""
    result = subprocess.run(
        [
            "dotnet", "test", "--nologo", "--verbosity", "normal",
            "--filter", "Every_operation_a_specification_declares_has_a_facade",
        ],
        cwd=root, capture_output=True, text=True, check=False,
    )
    output = result.stdout + result.stderr

    if "Actual:" not in output:
        if result.returncode == 0:
            print("The coverage test passes — no operation is uncovered beyond "
                  "the known gaps.", file=sys.stderr)
            return []
        sys.exit(f"could not read the test output:\n{output[-2000:]}")

    actual = output.split("Actual:", 1)[1]
    # The list is printed as ["A", "B", …] and may be elided with ··· when long.
    keys = re.findall(r'"([A-Z]+ /[^"]*)"', actual)
    if "···" in actual.split("]", 1)[0]:
        print("The test elided part of its list. Run it directly and pass the "
              "keys as arguments to see them all.", file=sys.stderr)
    return keys


def render(ops: list[dict]) -> None:
    for op in sorted(ops, key=lambda o: (o["spec"], o["key"])):
        flag = "  [deprecated]" if op["deprecated"] else ""
        print(f"{op['key']}{flag}")
        print(f"    spec         {op['spec']}")
        print(f"    operationId  {op['operationId'] or '—'}")
        print(f"    scopes       {', '.join(op['scopes']) or '— (none declared)'}")
        if op["summary"]:
            print(f"    summary      {op['summary']}")
        print()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("operations", nargs="*",
                        help='keys such as "PATCH /media/{}/assets/{}"')
    parser.add_argument("--spec", help="list everything one specification declares")
    parser.add_argument("--from-test", action="store_true",
                        help="run SpecPathTests and annotate the operations it reports")
    parser.add_argument("--json", action="store_true", help="machine-readable output")
    args = parser.parse_args()

    root = repository_root(Path(__file__).resolve().parent)
    catalogue = load(root, args.spec)

    wanted = list(args.operations)
    if args.from_test:
        wanted += uncovered_from_test(root)
        if not wanted:
            # The test passing is the good outcome, not a usage error.
            return 0

    if wanted:
        index: dict[str, list[dict]] = {}
        for op in catalogue:
            index.setdefault(op["key"], []).append(op)

        selected: list[dict] = []
        for key in dict.fromkeys(wanted):
            matches = index.get(key.strip())
            if not matches:
                print(f"{key}\n    NOT FOUND in any specification — the facade may "
                      f"call something upstream removed\n", file=sys.stderr)
                continue
            if len(matches) > 1:
                print(f"note: {key} is declared by {len(matches)} specs "
                      f"({', '.join(m['spec'] for m in matches)})", file=sys.stderr)
            selected.extend(matches)
        catalogue = selected
    elif not args.spec:
        parser.error("pass operations, --spec, or --from-test")

    if args.json:
        json.dump(catalogue, sys.stdout, indent=2)
        print()
    else:
        render(catalogue)

    return 0


if __name__ == "__main__":
    sys.exit(main())
