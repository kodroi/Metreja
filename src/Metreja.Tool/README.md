# Metreja

**A .NET profiler built for AI coding agents.** Tell your agent "find why this is slow" or "where am I wasting memory?" and get an answer backed by real profiling data — no detours, no GUIs, no context switching.

Every operation is a CLI command. Every output is machine-readable. The full measure-analyze-fix loop runs without human intervention.

```
You: "This endpoint takes 3 seconds, find out why"

Agent: Setting up profiling session...
       Running with profiler attached...
       Analyzing trace (42,381 events captured)...

       Top hotspots (by self time):
       ┌──────────────────────────────────┬───────┬────────────┬───────────┐
       │ Method                           │ Calls │ Self (ms)  │ Incl (ms) │
       ├──────────────────────────────────┼───────┼────────────┼───────────┤
       │ OrderService.ValidateInventory   │    47 │    1,842.3 │   2,104.1 │
       │ DbContext.SaveChangesInternal    │    12 │      891.7 │     891.7 │
       │ PricingEngine.CalculateDiscount  │   312 │      203.4 │     247.8 │
       └──────────────────────────────────┴───────┴────────────┴───────────┘

       ValidateInventory is your bottleneck — 1.8s of self time across
       47 calls. It queries inventory per-item instead of batching.
       Here's the fix...
```

## Why Metreja

- **Profiling without the detour** — no separate tool, no manual setup. Your agent profiles as part of fixing the problem.
- **Finds the real bottleneck** — self-time analysis pinpoints the method that's actually slow, not the one that calls it.
- **Catches wastefulness** — excessive allocations, GC thrashing, memory pressure. See which types allocate the most and which methods trigger gen2 collections.
- **Proves the fix worked** — diff two traces. See the numbers. No guessing whether your change helped.
- **Traces only your code** — filter by assembly, namespace, or class. Framework noise stays out, overhead stays low.
- **Reproducible** — session configs are isolated files. Re-run the same investigation anytime.

## Installation

**Prerequisites:** .NET 8 SDK or later, Windows 10/11 (x64)

```bash
dotnet tool install -g Metreja.Tool
```

After installation, the `metreja` command is available globally.

## Claude Code Plugin

Install the [metreja-profiler](https://github.com/kodroi/metreja-profiler) plugin and Claude handles everything automatically — session setup, profiling, analysis, and fix suggestions. Just ask a question.

```
/plugin marketplace add kodroi/metreja-profiler-marketplace
/plugin install metreja-profiler@metreja-profiler-marketplace
```

## Quick Start

Five commands from zero to actionable hotspot data:

```bash
# 1. Create a session
SESSION=$(metreja init --scenario "baseline")

# 2. Tell it what to trace (your code, not the framework)
metreja add include -s $SESSION --assembly MyApp
metreja add exclude -s $SESSION --assembly "System.*"
metreja add exclude -s $SESSION --assembly "Microsoft.*"

# 3. Generate the profiler environment and run your app
metreja generate-env -s $SESSION --format batch > env.bat
cmd //c "env.bat && dotnet run --project src/MyApp -c Release"

# 4. Find the bottleneck
metreja hotspots .metreja/output/*.ndjson --top 10

# 5. Drill into the slowest method
metreja calltree .metreja/output/*.ndjson --method "ValidateInventory"
```

## What You Can Measure

**Performance hotspots** — Find the slowest methods ranked by self time, inclusive time, call count, or allocation count.

```bash
metreja hotspots trace.ndjson --top 10 --sort self
metreja hotspots trace.ndjson --filter "MyApp.Services" --min-ms 1
```

**Call trees** — See exactly what a slow method does internally, with timing at every level.

```bash
metreja calltree trace.ndjson --method "ProcessOrder"
metreja calltree trace.ndjson --method "ProcessOrder" --occurrence 2  # 2nd slowest call
```

**Caller analysis** — Find out who calls a method and how much time each caller contributes.

```bash
metreja callers trace.ndjson --method "SaveChanges" --top 10
```

**Memory and GC pressure** — GC counts by generation, pause times, and per-type allocation hotspots.

```bash
metreja memory trace.ndjson --top 20
metreja memory trace.ndjson --filter "System.String"
```

**Before/after comparison** — Diff two traces to verify a fix actually improved performance.

```bash
metreja analyze-diff baseline.ndjson optimized.ndjson
```

---

## CLI Reference

### Session Management

#### `init`

Create a new profiling session. Returns a random 6-hex-char session ID.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--scenario` | string | — | Optional scenario name |

```bash
metreja init
metreja init --scenario "before-refactor"
```

#### `add include` / `add exclude`

Add filter rules controlling which methods get traced. Supports wildcards (`*`).

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-s`, `--session` | string | — | **Required.** Session ID |
| `--assembly` | string[] | `*` | Assembly name pattern |
| `--namespace` | string[] | `*` | Namespace pattern |
| `--class` | string[] | `*` | Class name pattern |
| `--method` | string[] | `*` | Method name pattern |

```bash
metreja add include -s a1b2c3 --assembly MyApp
metreja add include -s a1b2c3 --assembly MyApp --namespace "MyApp.Core" --namespace "MyApp.Services"
metreja add exclude -s a1b2c3 --class "*Generated*"
```

#### `remove include` / `remove exclude`

Remove a filter rule by exact match.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-s`, `--session` | string | — | **Required.** Session ID |
| `--assembly` | string | `*` | Assembly name pattern |
| `--namespace` | string | `*` | Namespace pattern |
| `--class` | string | `*` | Class name pattern |
| `--method` | string | `*` | Method name pattern |

```bash
metreja remove include -s a1b2c3 --assembly MyApp --namespace "MyApp.Core"
```

#### `clear-filters`

Clear all filter rules from a session.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-s`, `--session` | string | — | **Required.** Session ID |
| `--type` | string | — | `include`, `exclude`, or omit for both |

```bash
metreja clear-filters -s a1b2c3
metreja clear-filters -s a1b2c3 --type include
```

#### `set`

Configure session settings via subcommands.

| Subcommand | Arguments | Description |
|------------|-----------|-------------|
| `set metadata` | `-s ID "scenario-name"` | Update scenario name |
| `set output` | `-s ID "path/pattern.ndjson"` | Set output path (supports `{sessionId}`, `{pid}` tokens) |
| `set max-events` | `-s ID 50000` | Cap event count (0 = unlimited) |
| `set compute-deltas` | `-s ID true` | Enable delta timing for performance analysis |

#### `validate`

Check session configuration for errors. Exits with code 1 on failure.

```bash
metreja validate -s a1b2c3
```

#### `generate-env`

Generate a script that sets the environment variables to attach the profiler.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-s`, `--session` | string | — | **Required.** Session ID |
| `--format` | string | `batch` | `batch` or `powershell` |
| `--force` | bool | `false` | Generate even if profiler DLL is not found |

```bash
metreja generate-env -s a1b2c3
metreja generate-env -s a1b2c3 --format powershell
```

#### `clear`

Delete profiling sessions.

```bash
metreja clear -s a1b2c3
metreja clear --all
```

### Analysis Commands

#### `hotspots`

Per-method timing ranked by self time, inclusive time, call count, or allocations.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `file` | string | — | **Required.** NDJSON trace file |
| `--top` | int | `20` | Number of methods to show |
| `--min-ms` | double | `0.0` | Minimum time threshold (ms) |
| `--sort` | string | `self` | `self`, `inclusive`, `calls`, or `allocs` |
| `--filter` | string[] | — | Filter by method/class/namespace pattern |

#### `calltree`

Call tree for a specific method invocation, slowest first.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `file` | string | — | **Required.** NDJSON trace file |
| `--method` | string | — | **Required.** Method name or pattern |
| `--tid` | long | — | Filter by thread ID |
| `--occurrence` | int | `1` | Which invocation (1 = slowest) |

#### `callers`

Who calls a method, with call count and timing per caller.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `file` | string | — | **Required.** NDJSON trace file |
| `--method` | string | — | **Required.** Method name or pattern |
| `--top` | int | `20` | Number of callers to show |

#### `memory`

GC summary (generation counts, pause times) and per-type allocation hotspots.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `file` | string | — | **Required.** NDJSON trace file |
| `--top` | int | `20` | Number of allocation types |
| `--filter` | string[] | — | Filter by class name pattern |

#### `analyze-diff`

Compare two traces. Shows per-method timing delta (base vs. compare).

| Argument | Type | Description |
|----------|------|-------------|
| `base` | string | **Required.** Base NDJSON file |
| `compare` | string | **Required.** Comparison NDJSON file |

### Session Config Format

Stored at `.metreja/sessions/{sessionId}.json`:

```json
{
  "sessionId": "a1b2c3",
  "metadata": { "scenario": "baseline" },
  "instrumentation": {
    "maxEvents": 0,
    "computeDeltas": true,
    "includes": [
      { "assembly": "MyApp", "namespace": "*", "class": "*", "method": "*" }
    ],
    "excludes": []
  },
  "output": {
    "path": ".metreja/output/{sessionId}_{pid}.ndjson"
  }
}
```

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `maxEvents` | int | `0` | Event cap per session (0 = unlimited) |
| `computeDeltas` | bool | `true` | Include delta timing on `leave` events |

### Output Path Tokens

| Token | Replaced With |
|-------|---------------|
| `{sessionId}` | Session ID from config |
| `{pid}` | Process ID of the profiled app |
