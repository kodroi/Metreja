---
description: Use when profiling .NET applications for performance bottlenecks, slow methods, execution tracing, or debugging exceptions through call-path analysis instead of manual logging
---

# Metreja .NET Call-Path Profiler

## Tool Installation

| Asset | Details |
|-------|---------|
| CLI | `metreja` (installed as .NET global tool) |
| Package | `Metreja` on NuGet |
| CLSID | `{7C8F944B-4810-4999-BF98-6A3361185FC2}` |

The native profiler DLL is bundled inside the tool package and auto-discovered at runtime — no manual path configuration needed.

## Use-Case Detection

Activate this skill when the user's request matches either category:

```
User request
  |
  |-- Contains performance keywords? --> Performance profiling
  |     (slow, optimize, bottleneck, performance, latency, profile)
  |
  |-- Contains debugging keywords? --> Execution tracing
        (trace, exception, bug, flow, call path, what's happening, logging)
```

## Phase 0: Prerequisites

Verify the tool is installed:

```bash
dotnet tool list -g | grep -i metreja
```

If missing, install it:

```bash
dotnet tool install -g Metreja
```

## Phase 1: Target Analysis & Filter Design

1. **Identify the target project** — find the `.csproj` file and extract the assembly name (usually `<AssemblyName>` or the project file name without extension)
2. **Choose a filter strategy** based on the use case:

| Use Case | Include Filters | Exclude Filters | max-events |
|----------|----------------|-----------------|------------|
| Broad performance scan | `--assembly "AppName"` | `System.*`, `Microsoft.*` | 50000 |
| Focused method perf | `--assembly "AppName" --class "ClassName"` | `System.*`, `Microsoft.*` | 50000 |
| Debug specific flow | `--assembly "AppName" --namespace "Ns"` | (none) | 100000 |
| Debug exception | `--assembly "AppName"` | (none) | 100000 |

**Filter patterns** support `*` as wildcard (e.g., `System.*` matches `System.IO`, `System.Linq`, etc.).

## Phase 2: Session Setup

Run these commands sequentially. Each depends on the session ID from step 1.

```bash
# 1. Create session — capture the printed session ID
SESSION=$(metreja init --scenario "perf-investigation")

# 2. Add include filter (assembly name from Phase 1)
metreja add include -s $SESSION --assembly "MyApp"

# 3. Add exclude filters (for perf use cases)
metreja add exclude -s $SESSION --assembly "System.*"
metreja add exclude -s $SESSION --assembly "Microsoft.*"

# 4. Set output path (use tokens for unique filenames)
metreja set output -s $SESSION "trace-{runId}-{pid}.ndjson"

# 5. Enable delta timing (required for performance analysis)
metreja set compute-deltas -s $SESSION true

# 6. Set max events cap
metreja set max-events -s $SESSION 50000

# 7. Validate the session configuration
metreja validate -s $SESSION
```

Validation checks: `runId` exists, `output.path` set, at least one include rule, output directory writable. Fix any reported errors before proceeding.

## Phase 3: Profiled Execution

### Strategy A: Short-lived apps (console apps, tests)

Generate the env script then source it and run inline:

```bash
# Generate env vars as batch script (DLL path is auto-discovered)
metreja generate-env -s $SESSION --format batch > env.bat

# Source and run:
cmd //c "env.bat && dotnet run --project <target-project-path> -c Release"
```

The session config JSON is at `.metreja/sessions/<SESSION>.json` relative to the working directory.

### Strategy B: Long-running apps (web servers, services)

For apps that don't exit on their own:

1. Generate the env script: `metreja generate-env -s $SESSION --format powershell`
2. Print the env vars to the user with instructions to paste into their terminal
3. Tell the user to run their app manually
4. Wait for the user to signal that the app has been exercised and stopped
5. Proceed to Phase 4 with the generated NDJSON file

### Required Environment Variables

| Variable | Value |
|----------|-------|
| `CORECLR_ENABLE_PROFILING` | `1` |
| `CORECLR_PROFILER` | `{7C8F944B-4810-4999-BF98-6A3361185FC2}` |
| `CORECLR_PROFILER_PATH` | Absolute path to `Metreja.Profiler.dll` (auto-resolved by `generate-env`) |
| `METREJA_CONFIG` | Absolute path to session JSON (`.metreja/sessions/<id>.json`) |

## Phase 4: Analysis

Reference [ndjson-reference.md](ndjson-reference.md) for event schemas and field meanings.

### Pass 1: Statistical Summary

Quick overview of the trace file:

```bash
# Total events
wc -l < trace.ndjson

# Events by type
grep -c '"event":"enter"' trace.ndjson
grep -c '"event":"leave"' trace.ndjson
grep -c '"event":"exception"' trace.ndjson

# File size
ls -lh trace.ndjson
```

### Pass 2: Targeted Analysis

#### Performance hotspots

Extract leave events, sort by deltaNs descending, identify the top-20 slowest method calls:

```bash
grep '"event":"leave"' trace.ndjson | \
  python3 -c "
import sys, json
events = [json.loads(l) for l in sys.stdin]
events.sort(key=lambda e: e['deltaNs'], reverse=True)
for e in events[:20]:
    ms = e['deltaNs'] / 1_000_000
    print(f\"{ms:>10.2f} ms  {e['ns']}.{e['cls']}.{e['m']}  (async={e['async']})\")
"
```

#### Exception tracing

Find exception events and read surrounding context to reconstruct the call stack:

```bash
# List all exceptions
grep '"event":"exception"' trace.ndjson | python3 -c "
import sys, json
for l in sys.stdin:
    e = json.loads(l)
    print(f\"  {e['exType']}  in  {e['ns']}.{e['cls']}.{e['m']}\")
"

# For each exception, read the 50 preceding lines to see the call stack leading to it
grep -n '"event":"exception"' trace.ndjson  # get line numbers, then read context
```

#### Call tree reconstruction

Filter by thread ID and use the `depth` field to build an indented call tree:

```bash
grep '"tid":1234' trace.ndjson | grep '"event":"enter"' | python3 -c "
import sys, json
for l in sys.stdin:
    e = json.loads(l)
    indent = '  ' * e['depth']
    print(f\"{indent}{e['ns']}.{e['cls']}.{e['m']}\")
"
```

## Phase 5: Findings & Cleanup

1. **Present actionable insights** — link trace data back to source code:
   - For each hotspot method, locate it in the codebase and suggest optimizations
   - For exceptions, show the call stack and the source of the exception

2. **Before/after comparison** (optional, if user optimizes and re-profiles):
   ```bash
   metreja analyze-diff base-trace.ndjson optimized-trace.ndjson
   ```
   This outputs a table comparing total time per method between the two runs.

3. **Cleanup** — delete the session when done:
   ```bash
   metreja clear -s $SESSION
   ```

## CLI Quick Reference

| Command | Syntax | Purpose |
|---------|--------|---------|
| `init` | `metreja init [--scenario NAME]` | Create session, prints session ID |
| `add include` | `metreja add include -s ID [--assembly P] [--namespace P] [--class P] [--method P]` | Add include filter |
| `add exclude` | `metreja add exclude -s ID [--assembly P] [--namespace P] [--class P] [--method P]` | Add exclude filter |
| `set output` | `metreja set output -s ID PATH` | Set output path (supports `{runId}`, `{pid}` tokens) |
| `set compute-deltas` | `metreja set compute-deltas -s ID true\|false` | Enable/disable delta timing |
| `set max-events` | `metreja set max-events -s ID N` | Cap event count (0 = unlimited) |
| `set metadata` | `metreja set metadata -s ID [--scenario S] [--run-id R]` | Update scenario/runId |
| `set mode` | `metreja set mode -s ID MODE` | Set instrumentation mode (`elt3`) |
| `validate` | `metreja validate -s ID` | Validate session config |
| `generate-env` | `metreja generate-env -s ID [--dll-path P] [--format batch\|powershell]` | Generate env var script (DLL path auto-detected) |
| `analyze-diff` | `metreja analyze-diff BASE COMPARE` | Compare two NDJSON traces |
| `clear` | `metreja clear -s ID \| --all` | Delete session(s) |

## Common Pitfalls

- **Shell state doesn't persist between Bash tool calls.** Always set env vars inline on the same command line or use `generate-env` to create a batch script that gets sourced.
- **`COR_PRF_ENABLE_FRAME_INFO`** is already set by the DLL in its event mask — no user action needed.
- **Large traces blow up context.** Always set `max-events` (50k for perf, 100k for debugging). Never read an entire large NDJSON file — use grep/python to extract relevant events.
- **Async methods** appear as `<MethodName>d__N` state machine classes. The profiler resolves these: the `m` field shows the original method name, and `async` is `true`. Continuations may appear on different thread IDs than the initial call.
- **Output path must be writable.** The directory in `set output` path must exist or the profiler will fail silently. Create it before running.
- **Session config location.** Config JSON lives at `.metreja/sessions/<session-id>.json` relative to the working directory where you ran `metreja init`.
