# Metreja

**A .NET profiler built for AI coding agents.** Tell your agent "find why this is slow" or "where am I wasting memory?" and get an answer backed by real profiling data — no detours, no GUIs, no context switching.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D6)](https://github.com/kodroi/metreja)

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

| Command | What it does |
|---------|-------------|
| `metreja hotspots` | Rank methods by self time, inclusive time, call count, or allocations |
| `metreja calltree` | Expand a slow method into its full call tree with timing at every level |
| `metreja callers` | Find who calls a method and how much time each caller contributes |
| `metreja memory` | GC counts by generation, pause times, per-type allocation hotspots |
| `metreja analyze-diff` | Compare two traces to verify a fix actually improved performance |

For the full CLI reference with all options, see [src/Metreja.Tool/README.md](src/Metreja.Tool/README.md).

## Claude Code Plugin

Install the [metreja-profiler](https://github.com/kodroi/metreja-profiler) plugin and Claude handles everything automatically — session setup, profiling, analysis, and fix suggestions. Just ask a question.

```
/plugin marketplace add kodroi/metreja-profiler-marketplace
/plugin install metreja-profiler@metreja-profiler-marketplace
```

Or install directly:

```
/plugin install kodroi/metreja-profiler
```

## Building from Source

**Prerequisites:**
- Windows 10/11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (multi-targets .NET 8/9/10)
- Visual Studio 2022 Build Tools with "Desktop development with C++" workload

```bash
# Full build: CLI + native profiler DLL + install as global tool
build.bat

# Or build components individually
dotnet build src/Metreja.Tool/Metreja.Tool.csproj -c Release
msbuild src/Metreja.Profiler/Metreja.Profiler.vcxproj /p:Configuration=Release /p:Platform=x64

# Run integration tests
dotnet test test/Metreja.IntegrationTests/Metreja.IntegrationTests.csproj -c Release

# Format C++ code
scripts/format-cpp.bat
```

Build outputs:
- CLI: `src/Metreja.Tool/bin/Release/net10.0/metreja.exe`
- Profiler DLL: `bin/Release/Metreja.Profiler.dll`

## Architecture

Metreja is a two-component system:

1. **Metreja.Profiler** — Native C++ DLL implementing `ICorProfilerCallback3` with ELT3 hooks. Attaches to the .NET runtime via `COR_PROFILER` environment variables, hooks method enter/leave, and writes NDJSON traces.

2. **Metreja.Tool** — C# CLI for session management and trace analysis. Creates session configs, generates profiler environment scripts, and provides analysis commands (hotspots, calltree, callers, memory, diff).

**Data flow:** CLI creates session config → `generate-env` sets profiler env vars → profiled app loads DLL → DLL writes NDJSON → CLI analysis commands read NDJSON.

## License

[MIT](LICENSE) — Copyright (c) 2026 Iiro Rahkonen
