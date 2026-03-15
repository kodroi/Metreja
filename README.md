# Metreja

**A .NET call-path profiler for AI agents and scripts.** Measure, analyze, and compare .NET performance — no GUI, no detours, no human in the seat.

[![Build](https://github.com/kodroi/Metreja/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/kodroi/Metreja/actions/workflows/build-and-test.yml)
[![NuGet](https://img.shields.io/nuget/v/Metreja.Tool)](https://www.nuget.org/packages/Metreja.Tool)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Metreja.Tool)](https://www.nuget.org/packages/Metreja.Tool)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

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

## Quick Start

Install the [metreja-profiler](https://github.com/kodroi/metreja-profiler) skill for Claude Code and ask a question:

```
/plugin marketplace add kodroi/metreja-profiler-marketplace
/plugin install metreja-profiler@metreja-profiler-marketplace
```

```
You: "This endpoint takes 3 seconds, find out why"

Agent: Setting up profiling session...
       ...
```

### Manual CLI Usage

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

| Command | What it does |
|---------|-------------|
| `metreja hotspots` | Rank methods by self time, inclusive time, call count, or allocations |
| `metreja calltree` | Expand a slow method into its full call tree with timing at every level |
| `metreja callers` | Find who calls a method and how much time each caller contributes |
| `metreja memory` | GC counts by generation, pause times, per-type allocation hotspots |
| `metreja analyze-diff` | Compare two traces to verify a fix actually improved performance |
| `metreja summary` | Trace overview: event counts, wall-clock duration, threads, methods |
| `metreja exceptions` | Rank exception types by frequency with throw-site methods |
| `metreja timeline` | Chronological event listing with tid, event type, and method filtering |
| `metreja threads` | Per-thread breakdown: call counts, root time, activity windows |
| `metreja trend` | Method performance trend across periodic stats flush intervals |
| `metreja check` | CI regression gate: compare two traces, exit non-zero on regression |
| `metreja run` | Launch an executable with profiler attached (`--detach` for GUI apps) |
| `metreja flush` | Trigger manual stats flush on a running profiled process |
| `metreja list` | List existing profiling sessions |
| `metreja merge` | Combine multiple trace files into one sorted by timestamp |
| `metreja export` | Convert traces to speedscope or CSV format for visualization/analysis |

All analysis commands support `--format json` for structured machine-readable output and return proper exit codes (0=success, 1=non-success such as error or regression).

For the full CLI reference with all options, see [src/Metreja.Tool/README.md](src/Metreja.Tool/README.md).

### CI Integration

Gate performance regressions in your pipeline. `metreja check` compares two traces and exits non-zero when any method exceeds the threshold:

```bash
metreja check baseline.ndjson pr-build.ndjson --threshold 10
# Exit 0 = no regressions, Exit 1 = regression detected
```

### Structured Output for Agents

All analysis commands support `--format json` for machine-readable output:

```bash
metreja hotspots trace.ndjson --format json
metreja summary trace.ndjson --format json
metreja check baseline.ndjson compare.ndjson --format json
```

Export traces to CSV for spreadsheet analysis:

```bash
metreja export trace.ndjson --format csv
```

## Design Philosophy

Metreja is a data layer, not an intelligence layer. The CLI profiles, captures, and aggregates — it never interprets what the data means for your codebase. That intelligence belongs to the consumer: an AI agent, a skill, or a custom script.

Read the full design philosophy in [`docs/design-philosophy.md`](docs/design-philosophy.md).

## Building from Source

**Windows prerequisites:**
- Windows 10/11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (multi-targets .NET 8/9/10)
- Visual Studio 2022 Build Tools with "Desktop development with C++" workload

**macOS prerequisites:**
- macOS 14+ (Apple Silicon / ARM64)
- Xcode Command Line Tools
- CMake (`brew install cmake`)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (multi-targets .NET 8/9/10)

```bash
# Build CLI (all platforms)
dotnet build src/Metreja.Tool/Metreja.Tool.csproj -c Release

# Build profiler — Windows
msbuild src/Metreja.Profiler/Metreja.Profiler.vcxproj /p:Configuration=Release /p:Platform=x64 "/p:SolutionDir=%CD%\\"

# Build profiler — macOS
scripts/build-macos.sh

# Run integration tests
dotnet test test/Metreja.IntegrationTests/Metreja.IntegrationTests.csproj -c Release

# Format C++ code
scripts/format-cpp.bat   # Windows
scripts/format-cpp.sh    # macOS/Linux
```

Build outputs:
- **Windows:** CLI at `src/Metreja.Tool/bin/Release/net10.0/metreja.exe`, profiler DLL at `bin/Release/Metreja.Profiler.dll`
- **macOS:** CLI at `src/Metreja.Tool/bin/Release/net10.0/metreja`, profiler dylib at `bin/Release/libMetreja.Profiler.dylib`

## Architecture

Metreja is a two-component system:

1. **Metreja.Profiler** — Native C++ library (DLL on Windows, dylib on macOS) implementing `ICorProfilerCallback10` with ELT3 hooks. Attaches to the .NET runtime via `COR_PROFILER` environment variables, hooks method enter/leave, and writes NDJSON traces.

2. **Metreja.Tool** — C# CLI for session management and trace analysis. Creates session configs, generates profiler environment scripts, and provides 11 analysis commands (hotspots, calltree, callers, memory, exceptions, timeline, threads, trend, summary, analyze-diff, check) plus utilities (run, flush, list, merge, export). All analysis commands support `--format json`.

**Data flow:** CLI creates session config → `generate-env` sets profiler env vars → profiled app loads the profiler → profiler writes NDJSON → CLI analysis commands read NDJSON.

## Reporting Issues

Report bugs or feature requests from the CLI or directly on GitHub:

**From the CLI:**

```bash
metreja report --title "Bug: crash on empty trace" --description "Running hotspots on an empty NDJSON file causes an unhandled exception."
```

Requires the [GitHub CLI (`gh`)](https://cli.github.com/) to be installed and authenticated.

**On GitHub:**

Open an issue manually at [kodroi/Metreja/issues](https://github.com/kodroi/Metreja/issues).

## License

[MIT](LICENSE) — Copyright (c) 2026 Iiro Rahkonen
