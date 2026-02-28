# Metreja CLI

.NET Call-Path Profiler CLI — configure profiling sessions, attach the native profiler to .NET applications, and analyze trace output.

## Quick Start

```bash
# 1. Initialize a session
metreja init --scenario "baseline"
# Output: Session created: a1b2c3

# 2. Add filter rules (instrument only your code)
metreja add include -s a1b2c3 --assembly MyApp --namespace "MyApp.Services"

# 3. Generate environment script
metreja generate-env -s a1b2c3

# 4. Run your app with the generated env vars, then analyze
metreja hotspots .metreja/output/e4f5a6b7_12345.ndjson
metreja calltree .metreja/output/e4f5a6b7_12345.ndjson --method DoWork
```

## Session Management Commands

### `init`

Initialize a new profiling session. Creates a config file at `.metreja/sessions/{sessionId}.json` with a random 6-hex-char session ID and an auto-generated 8-char run ID.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--scenario` | string | — | Optional scenario name for this profiling session |

```bash
metreja init
metreja init --scenario "before-refactor"
```

### `add include` / `add exclude`

Add filter rules that control which methods get instrumented.

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
| `run-id` | string | Run ID |

```bash
metreja set metadata -s a1b2c3 "after-refactor"
metreja set metadata -s a1b2c3 "after-refactor" "run002"
```

#### `set output`

| Option / Argument | Type | Description |
|-------------------|------|-------------|
| `-s`, `--session` | string | **Required.** Session ID |
| `path` | string | **Required.** Output file path pattern |

```bash
metreja set output -s a1b2c3 ".metreja/output/{runId}_{pid}.ndjson"
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

Validate session configuration. Checks for missing run ID, missing output path, and verifies the output directory can be created. Exits with code 1 on errors.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-s`, `--session` | string | — | **Required.** Session ID |

```bash
metreja validate -s a1b2c3
```

### `generate-env`

Generate an environment variable script to attach the profiler to a .NET application. Sets `CORECLR_ENABLE_PROFILING`, `CORECLR_PROFILER`, `CORECLR_PROFILER_PATH`, and `METREJA_CONFIG`.

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

## Analysis Commands

All analysis commands read NDJSON trace files produced by the profiler.

### `hotspots`

Show per-method timing hotspots with self time. Self time is inclusive time minus time spent in child calls. Supports filtering by method, class, or namespace name.

| Option / Argument | Type | Default | Description |
|-------------------|------|---------|-------------|
| `file` | string | — | **Required.** NDJSON trace file path |
| `--top` | int | `20` | Number of methods to show |
| `--min-ms` | double | `0.0` | Minimum time threshold in milliseconds |
| `--sort` | string | `self` | Sort by: `self` or `inclusive` |
| `--filter` | string[] | — | Include only methods matching pattern(s) |

```bash
metreja hotspots trace.ndjson
metreja hotspots trace.ndjson --top 50 --sort inclusive
metreja hotspots trace.ndjson --filter "MyService" --min-ms 10
```

### `calltree`

Show the call tree for a specific method invocation. Finds all invocations matching the pattern, ranked by duration (slowest first). Displays the complete subtree with indentation, timing, `[async]` tags, and exception info.

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

### `callers`

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

### `memory`

Show GC summary and allocation hotspots by class. Displays generation counts, pause durations, and top allocating types.

| Option / Argument | Type | Default | Description |
|-------------------|------|---------|-------------|
| `file` | string | — | **Required.** NDJSON trace file path |
| `--top` | int | `20` | Number of allocation types to show |
| `--filter` | string[] | — | Include only class names matching pattern(s) |

```bash
metreja memory trace.ndjson
metreja memory trace.ndjson --top 50 --filter "System.String"
```

### `analyze-diff`

Compare two NDJSON profiling outputs. Shows a side-by-side table of per-method timing (base, compare, delta) for every method appearing in either file.

| Argument | Type | Description |
|----------|------|-------------|
| `base` | string | **Required.** Base NDJSON file path |
| `compare` | string | **Required.** Comparison NDJSON file path |

```bash
metreja analyze-diff baseline.ndjson optimized.ndjson
```

## Config File Format

Session configs are stored at `.metreja/sessions/{sessionId}.json`:

```json
{
  "sessionId": "a1b2c3",
  "metadata": {
    "scenario": "baseline",
    "runId": "e4f5a6b7"
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
    "path": ".metreja/output/{runId}_{pid}.ndjson",
    "format": "ndjson"
  }
}
```

## Output Path Tokens

The output path supports two token placeholders expanded at runtime by the native profiler:

| Token | Replaced With |
|-------|---------------|
| `{runId}` | The `metadata.runId` value from the session config |
| `{pid}` | The process ID of the profiled application |

Default output path: `.metreja/output/{runId}_{pid}.ndjson`
