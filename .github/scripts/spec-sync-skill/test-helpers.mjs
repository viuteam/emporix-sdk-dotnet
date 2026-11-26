/** Test-only: a `run` that answers the first entry whose key prefixes the command line and records every call. */
export function fakeRunner(table) {
  const run = (cmd, args) => {
    const line = [cmd, ...args].join(" ");
    run.calls.push(line);
    const hit = table.find(([key]) => line.startsWith(key));
    if (!hit) throw new Error(`unexpected command: ${line}`);
    return typeof hit[1] === "function" ? hit[1](line) : hit[1];
  };
  run.calls = [];
  return run;
}
