# Metreja

A .NET profiling tool designed for AI coding assistants — Claude Code, Codex, and Cursor can automatically profile your app, identify bottlenecks, and optimize code.

## What It Does

AI coding tools can run Metreja automatically to profile your .NET application, analyze the results, and write optimized code — all in one loop. Point your agent at a slow endpoint, and it will measure what's slow and fix it without you switching context.

Windows only. The profiler ships alongside the tool.

## How It Works

- **Fully CLI-driven** — configuration and analysis are all commands. Sessions, filters, hotspots, call trees, diffs — no GUI or config files to hand-edit. Works in any terminal or agent loop.
- **Session-based** — your agent creates a session, configures what to trace, then runs your app. Everything is scoped to a session, so multiple profiling runs stay isolated and reproducible.
- **File-based output** — configs and traces are plain text files that both humans and AI agents can read, diff, and process with standard tools.

## Installation

**Prerequisites:** .NET 10 SDK, Windows

```bash
dotnet tool install -g Metreja.Tool
```

After installation, the `metreja` command is available globally.

## Workflow

Your agent profiles a .NET application in five steps: create a session, pick what to trace, set up the environment, run the app, and analyze the results.

### 1. Initialize a Session

A session is a named configuration that tells the profiler what to measure and where to save results. Each session gets a random 6-hex-char ID.

```bash
metreja init --scenario "baseline"
# Output: Session created: a1b2c3
```

The session config is stored at `.metreja/sessions/a1b2c3.json`.

### 2. Add Filter Rules

By default, nothing is traced. You add include rules to specify which parts of your code to trace — by assembly, namespace, class, or method. This keeps overhead low — you trace only your code, not the entire .NET framework.

```bash
metreja add include -s a1b2c3 --assembly MyApp --namespace "MyApp.Services"
```

You can also add exclude rules to skip specific classes or methods within your includes (e.g., generated code).

### 3. Generate the Environment Script

Your app needs a few environment variables so the profiler can attach. This command generates a script that sets them.

```bash
metreja generate-env -s a1b2c3
```

### 4. Run Your Application

Run the generated script to set the environment variables, then launch your .NET app as usual. The profiler attaches automatically and writes timing data to the output file.

### 5. Analyze the Output

Once your app has run, use the analysis commands to explore the results:

```bash
# Find the slowest methods
metreja hotspots .metreja/output/e4f5a6b7_12345.ndjson

# Inspect the call tree of a specific method
metreja calltree .metreja/output/e4f5a6b7_12345.ndjson --method DoWork

# See who calls a method
metreja callers .metreja/output/e4f5a6b7_12345.ndjson --method SaveChanges

# View memory and allocation summary
metreja memory .metreja/output/e4f5a6b7_12345.ndjson

# Compare two runs
metreja analyze-diff baseline.ndjson optimized.ndjson
```

## CLI Reference

### Session Management Commands

### `init`

Initialize a new profiling session. Creates a config file at `.metreja/sessions/{sessionId}.json` with a random 6-hex-char session ID.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--scenario` | string | — | Optional scenario name for this profiling session |

```bash
metreja init
metreja init --scenario "before-refactor"
```

### `add include` / `add exclude`

Add filter rules that control which methods get traced.

Supports multi-value expansion on a single dimension — e.g. `--assembly A --assembly B` creates two separate rules.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-s`, `--session` | string | — | **Required.** Session ID |
| `--assembly` | string[] | `*` | Assembly name pattern |
| `--namespace` | string[] | `*` | Namespace pattern |
| `--class` | string[] | `*` | Class name pattern |
| `--method` | string[] | `*` | Method name pattern |
| `--log-lines` | bool | `false` | Enable line-level logging |

```bash
metreja add include -s a1b2c3 --assembly MyApp
metreja add include -s a1b2c3 --assembly MyApp --namespace "MyApp.Core" --namespace "MyApp.Services"
metreja add exclude -s a1b2c3 --class "*Generated*"
```

### `remove include` / `remove exclude`

Remove a specific filter rule by exact match.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-s`, `--session` | string | — | **Required.** Session ID |
| `--assembly` | string | `*` | Assembly name pattern |
| `--namespace` | string | `*` | Namespace pattern |
| `--class` | string | `*` | Class name pattern |
| `--method` | string | `*` | Method name pattern |
| `--log-lines` | bool | `false` | Enable line-level logging |

```bash
metreja remove include -s a1b2c3 --assembly MyApp --namespace "MyApp.Core"
```

### `clear-filters`

Clear all filter rules from a session.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-s`, `--session` | string | — | **Required.** Session ID |
| `--type` | string | — | Filter type to clear: `include` or `exclude` (omit to clear both) |

```bash
metreja clear-filters -s a1b2c3
metreja clear-filters -s a1b2c3 --type include
```

### `set`

Set session configuration values via subcommands.

#### `set metadata`

| Option / Argument | Type | Description |
|-------------------|------|-------------|
| `-s`, `--session` | string | **Required.** Session ID |
| `scenario` | string | Scenario name |

```bash
metreja set metadata -s a1b2c3 "after-refactor"
```

#### `set output`

| Option / Argument | Type | Description |
|-------------------|------|-------------|
| `-s`, `--session` | string | **Required.** Session ID |
| `path` | string | **Required.** Output file path pattern |

```bash
metreja set output -s a1b2c3 ".metreja/output/{sessionId}_{pid}.ndjson"
```

#### `set mode`

| Option / Argument | Type | Description |
|-------------------|------|-------------|
| `-s`, `--session` | string | **Required.** Session ID |
| `mode` | string | **Required.** Instrumentation mode (`elt3`) |

```bash
metreja set mode -s a1b2c3 elt3
```

#### `set max-events`

| Option / Argument | Type | Description |
|-------------------|------|-------------|
| `-s`, `--session` | string | **Required.** Session ID |
| `value` | int | **Required.** Maximum events (0 = unlimited) |

```bash
metreja set max-events -s a1b2c3 100000
```

#### `set compute-deltas`

| Option / Argument | Type | Description |
|-------------------|------|-------------|
| `-s`, `--session` | string | **Required.** Session ID |
| `value` | bool | **Required.** Enable delta timing |

```bash
metreja set compute-deltas -s a1b2c3 true
```

#### `set track-memory`

| Option / Argument | Type | Description |
|-------------------|------|-------------|
| `-s`, `--session` | string | **Required.** Session ID |
| `value` | bool | **Required.** Enable GC and allocation tracking |

```bash
metreja set track-memory -s a1b2c3 true
```

### `validate`

Validate session configuration. Checks for missing output path and verifies the output directory can be created. Exits with code 1 on errors.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-s`, `--session` | string | — | **Required.** Session ID |

```bash
metreja validate -s a1b2c3
```

### `generate-env`

Generate a script that sets the environment variables needed to attach the profiler.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-s`, `--session` | string | — | **Required.** Session ID |
| `--dll-path` | string | Auto-detected | Path to Metreja.Profiler.dll |
| `--format` | string | `batch` | Output format: `batch` or `powershell` |
| `--force` | bool | `false` | Generate script even if profiler DLL is not found |

```bash
metreja generate-env -s a1b2c3
metreja generate-env -s a1b2c3 --format powershell
metreja generate-env -s a1b2c3 --dll-path "C:\tools\Metreja.Profiler.dll"
```

### `clear`

Delete profiling session(s).

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-s`, `--session` | string | — | Session ID to delete |
| `--all` | bool | `false` | Delete all sessions |

One of `--session` or `--all` is required.

```bash
metreja clear -s a1b2c3
metreja clear --all
```

### Analysis Commands

All analysis commands read the trace files produced by the profiler.

#### `hotspots`

Show per-method timing hotspots with self time and memory allocation counts. Self time is time spent in the method itself, excluding methods it calls. When `track-memory` is enabled, the `Allocs` column shows allocations attributed to each method. Supports filtering by method, class, or namespace name.

| Option / Argument | Type | Default | Description |
|-------------------|------|---------|-------------|
| `file` | string | — | **Required.** NDJSON trace file path |
| `--top` | int | `20` | Number of methods to show |
| `--min-ms` | double | `0.0` | Minimum time threshold in milliseconds |
| `--sort` | string | `self` | Sort by: `self`, `inclusive`, `calls`, or `allocs` |
| `--filter` | string[] | — | Include only methods matching pattern(s) |

```bash
metreja hotspots trace.ndjson
metreja hotspots trace.ndjson --top 50 --sort inclusive
metreja hotspots trace.ndjson --filter "MyService" --min-ms 10
```

#### `calltree`

Show the call tree for a specific method call. Finds all calls matching the pattern, ranked by duration (slowest first). Displays the complete subtree with indentation, timing, `[async]` tags, and exception info.

| Option / Argument | Type | Default | Description |
|-------------------|------|---------|-------------|
| `file` | string | — | **Required.** NDJSON trace file path |
| `--method` | string | — | **Required.** Method name or pattern to match |
| `--tid` | long | — | Filter by thread ID |
| `--occurrence` | int | `1` | Which occurrence to show (1 = slowest) |

```bash
metreja calltree trace.ndjson --method DoWork
metreja calltree trace.ndjson --method "ProcessOrder" --occurrence 2
metreja calltree trace.ndjson --method DoWork --tid 12345
```

#### `callers`

Show which methods call a specific method. Aggregates caller info including call count, total time, and max time per caller.

| Option / Argument | Type | Default | Description |
|-------------------|------|---------|-------------|
| `file` | string | — | **Required.** NDJSON trace file path |
| `--method` | string | — | **Required.** Method name or pattern to match |
| `--top` | int | `20` | Number of callers to show |

```bash
metreja callers trace.ndjson --method SaveChanges
metreja callers trace.ndjson --method "Execute" --top 10
```

#### `memory`

Memory summary: garbage collection stats and top allocating types. Displays generation counts, pause durations, and allocation counts by type.

| Option / Argument | Type | Default | Description |
|-------------------|------|---------|-------------|
| `file` | string | — | **Required.** NDJSON trace file path |
| `--top` | int | `20` | Number of allocation types to show |
| `--filter` | string[] | — | Include only class names matching pattern(s) |

```bash
metreja memory trace.ndjson
metreja memory trace.ndjson --top 50 --filter "System.String"
```

#### `analyze-diff`

Compare two trace files side by side. Shows a table of per-method timing (base, compare, delta) for every method appearing in either file.

| Argument | Type | Description |
|----------|------|-------------|
| `base` | string | **Required.** Base NDJSON file path |
| `compare` | string | **Required.** Comparison NDJSON file path |

```bash
metreja analyze-diff baseline.ndjson optimized.ndjson
```

### Config File Format

Session configs are stored at `.metreja/sessions/{sessionId}.json`:

```json
{
  "sessionId": "a1b2c3",
  "metadata": {
    "scenario": "baseline"
  },
  "instrumentation": {
    "mode": "elt3",
    "maxEvents": 0,
    "computeDeltas": true,
    "trackMemory": false,
    "includes": [
      {
        "assembly": "MyApp",
        "namespace": "*",
        "class": "*",
        "method": "*",
        "logLines": false
      }
    ],
    "excludes": []
  },
  "output": {
    "path": ".metreja/output/{sessionId}_{pid}.ndjson",
    "format": "ndjson"
  }
}
```

### Output Path Tokens

The output path supports two placeholders replaced automatically when the trace file is created:

| Token | Replaced With |
|-------|---------------|
| `{sessionId}` | The `sessionId` value from the session config |
| `{pid}` | The process ID of the profiled application |

Default output path: `.metreja/output/{sessionId}_{pid}.ndjson`
