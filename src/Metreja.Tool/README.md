# Metreja

**A .NET call-path profiler for AI agents and scripts.** Measure, analyze, and compare .NET performance — no GUI, no detours, no human in the seat.

Every operation is a CLI command. Every output is structured for machines. Your agent provides the intelligence — Metreja provides the data.

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

Existing .NET profilers require a human in the seat — launching GUIs, clicking through views, interpreting results manually. Metreja removes the human from the loop.

- **Data layer for agents** — the CLI captures, aggregates, and surfaces profiling data. Your agent or script decides what it means.
- **Structured output, not visualizations** — every command returns machine-readable results that agents consume directly. No interactive sessions.
- **Finds the real bottleneck** — self-time analysis pinpoints the method that's actually slow, not the one that calls it.
- **Proves the fix worked** — diff two traces. See the numbers change. No guessing whether your change helped.
- **Lightweight enough for daily use** — fast to set up, fast to run, fast to analyze.
- **Traces only your code** — filter by assembly, namespace, or class. Framework noise stays out, overhead stays low.

## Installation

**Prerequisites:** .NET 8 SDK or later. Windows 10/11 (x64) or macOS 14+ (Apple Silicon).

```bash
dotnet tool install -g Metreja.Tool
```

## Claude Code Skill

Install the [metreja-profiler](https://github.com/kodroi/metreja-profiler) skill for Claude Code and ask a question:

```
/plugin marketplace add kodroi/metreja-profiler-marketplace
/plugin install metreja-profiler@metreja-profiler-marketplace
```

## Quick Start

Five commands from zero to hotspot data:

```bash
# 1. Create a session
SESSION=$(metreja init --scenario "baseline")

# 2. Tell it what to trace (your code, not the framework)
metreja add include -s $SESSION --assembly MyApp
metreja add exclude -s $SESSION --assembly "System.*"
metreja add exclude -s $SESSION --assembly "Microsoft.*"

# 3. Generate the profiler environment and run your app
metreja generate-env -s $SESSION --format shell > env.sh
source env.sh && dotnet run --project src/MyApp -c Release

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
metreja add include -s a1b2c3 --namespace "MyApp.Core"
metreja add include -s a1b2c3 --namespace "MyApp.Services"
metreja add exclude -s a1b2c3 --class "*Generated*"
```

> **Note:** Only one level option (`--assembly`, `--namespace`, `--class`, or `--method`) can be used per `add`/`remove` command. To filter multiple levels, use separate commands. Multiple patterns per level are supported in `add` (e.g., `--namespace "A" --namespace "B"`).

#### How Filters Work

A method is traced if it matches **any** include rule AND does **not** match **any** exclude rule.

- **Include-first, then exclude:** If includes are defined, only methods matching at least one include rule are candidates. Excludes then remove specific methods from that set.
- **One level per command:** Each `add include`/`add exclude` command accepts a single level (`--assembly`, `--namespace`, `--class`, or `--method`) but can take multiple patterns at that level.
- **Zero ELT3 overhead for excluded methods:** Filters are evaluated at JIT time via `FunctionIDMapper2`. Excluded methods never get ELT3 hooks installed — there is no per-call cost.

**Example — trace an assembly but exclude generated code:**

```bash
metreja add include -s a1b2c3 --assembly MyApp
metreja add exclude -s a1b2c3 --namespace "MyApp.Generated"
metreja add exclude -s a1b2c3 --class "*ApiClient"
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
metreja remove include -s a1b2c3 --namespace "MyApp.Core"
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
| `set events` | `-s ID enter leave method_stats` | Set enabled event types |
| `set stats-flush-interval` | `-s ID 30` | Periodic stats flush interval in seconds (0 = disabled, default 30) |

> **Tip:** All subcommands support `--help` for detailed usage (e.g., `metreja set events --help`).

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
| `--format` | string | `batch` | `batch`, `powershell`, or `shell` |
| `--force` | bool | `false` | Generate even if profiler DLL is not found |

```bash
metreja generate-env -s a1b2c3
metreja generate-env -s a1b2c3 --format powershell
metreja generate-env -s a1b2c3 --format shell
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

#### `report`

Report an issue to the GitHub repository. Requires the [GitHub CLI (`gh`)](https://cli.github.com/) installed and authenticated.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-t`, `--title` | string | — | **Required.** Issue title |
| `-d`, `--description` | string | — | **Required.** Issue body/description |

```bash
metreja report --title "Bug: crash on empty trace" --description "Detailed description of the issue."
```

**Exit codes:**
- `0` — Issue created successfully
- `1` — Issue creation failed (prints GitHub error)
- `2` — GitHub CLI not installed
- `3` — GitHub CLI not authenticated

### Session Config Format

Stored at `.metreja/sessions/{sessionId}.json`:

```json
{
  "sessionId": "a1b2c3",
  "metadata": { "scenario": "baseline" },
  "instrumentation": {
    "maxEvents": 0,
    "computeDeltas": true,
    "statsFlushIntervalSeconds": 30,
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
| `statsFlushIntervalSeconds` | int | `30` | Periodic stats flush interval (0 = disabled). Protects against data loss when the profiled process is force-killed. |

### Output Path Tokens

| Token | Replaced With |
|-------|---------------|
| `{sessionId}` | Session ID from config |
| `{pid}` | Process ID of the profiled app |
