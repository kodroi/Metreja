# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Metreja is a .NET Call-Path Profiler: a native C++ DLL implementing ICorProfilerCallback3 with ELT3 hooks, paired with a C# CLI (`metreja`) for session management and trace analysis. Output is NDJSON (newline-delimited JSON).

## Build Commands

```bash
# Build CLI (all platforms)
dotnet build src/Metreja.Tool/Metreja.Tool.csproj -c Release

# Build profiler — Windows (x64)
msbuild src/Metreja.Profiler/Metreja.Profiler.vcxproj /p:Configuration=Release /p:Platform=x64 "/p:SolutionDir=%CD%\\"

# Build profiler — macOS (ARM64, via CMake)
scripts/build-macos.sh
# Or manually:
cd src/Metreja.Profiler && cmake -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build

# Run integration tests
dotnet test test/Metreja.IntegrationTests/Metreja.IntegrationTests.csproj -c Release

# Run a single test
dotnet test test/Metreja.IntegrationTests/Metreja.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~SyncCallPathTests"

# Format C++ code
scripts/format-cpp.bat   # Windows
scripts/format-cpp.sh    # macOS/Linux
```

Build outputs:
- **Windows:** CLI at `src/Metreja.Tool/bin/Release/net10.0/metreja.exe`, profiler DLL at `bin/Release/Metreja.Profiler.dll`
- **macOS:** CLI at `src/Metreja.Tool/bin/Release/net10.0/metreja`, profiler dylib at `bin/Release/libMetreja.Profiler.dylib`

## Architecture

**Two-component system:**

1. **Metreja.Profiler** (`src/Metreja.Profiler/`) — Native C++ library (vcxproj on Windows x64 / CMake on macOS ARM64, C++20). Windows uses MASM assembly helpers (`amd64/asmhelpers.asm`), macOS uses GAS (`arm64/asmhelpers.S`). Platform abstraction via `platform/pal*.h` headers. Implements COM class factory with CLSID `{7C8F944B-4810-4999-BF98-6A3361185FC2}`. Attaches to .NET runtime via `COR_PROFILER` environment variables, hooks method enter/leave via ELT3, writes NDJSON traces.

2. **Metreja.Tool** (`src/Metreja.Tool/`) — C# CLI targeting net8.0/net9.0/net10.0. Uses System.CommandLine 2.0.3. Session-based: configs stored in `.metreja/sessions/{sessionId}.json`. Commands split across `Commands/` (session management) and `Analysis/` (hotspots, calltree, callers, memory, diff).

**Data flow:** CLI creates session config → `generate-env` sets profiler env vars → profiled app loads DLL → DLL writes NDJSON (one file per PID via `{pid}` token) → `run` command auto-merges multi-PID files on exit → CLI analysis commands read NDJSON.

**C++ global context:** The profiler uses a global `g_ctx` (`ProfilerContext*`) because ELT3 callbacks are bare function pointers with no `this`. `ProfilerContext` owns `MethodCache`, `CallStackManager`, `NdjsonWriter`, and `StatsAggregator`. Published atomically in `Profiler::Initialize()`.

**Config delivery to the DLL:** The CLI writes session JSON to `.metreja/sessions/{id}.json`, then `generate-env` sets `METREJA_CONFIG` env var pointing to that file. `ConfigReader` in the DLL reads this on `Initialize()`.

**Tests** (`test/Metreja.IntegrationTests/`) — XUnit + Verify.Xunit snapshot testing. `ProfilerRunner` spawns `Metreja.TestApp` under the profiler, captures NDJSON, normalizes events via `TraceNormalizer` (maps real thread IDs → `Thread-N`), verifies against `Snapshots/*.verified.txt` files. `Metreja.Tool` has `InternalsVisibleTo: Metreja.IntegrationTests` for analysis command testing.

## NDJSON Event Types

Events written by the profiler DLL (controlled by `set events` command):

- `session_metadata` — emitted once at start (scenario, sessionId, pid)
- `enter` / `leave` — method entry/exit with tsNs, tid, depth, deltaNs (on leave). Async method leave events include `wallTimeNs` (wall-clock time including awaits)
- `exception` — exception thrown (exType, method info)
- `gc_start` / `gc_end` — GC lifecycle events by generation. `gc_end` includes `durationNs` and may include `heapSizeBytes` when available (from GetGenerationBounds)
- `gc_heap_stats` — per-generation heap sizes, promoted bytes, finalization queue, pinned objects (via EventPipe GCHeapStats_V2, .NET 5+)
- `alloc_by_class` — per-type allocation counts with optional call-site attribution (`allocM`, `allocNs`, `allocCls`)
- `contention_start` / `contention_end` — lock contention events via EventPipe (tid, tsNs)
- `method_stats` / `exception_stats` — periodic aggregated statistics

**maxEvents behavior:** `session_metadata`, `gc_start`, `gc_end`, `gc_heap_stats`, `method_stats`, and `exception_stats` bypass the maxEvents cap. Only `enter`, `leave`, `exception`, `alloc_by_class`, `contention_start`, and `contention_end` count against it.

## Code Style

**C#:** Warnings as errors, `AnalysisLevel: latest-recommended`, nullable enabled, file-scoped namespaces, Allman braces, 4-space indent. Private fields: `_camelCase`. Private methods go after public ones.

**C++:** Microsoft-based style via `.clang-format` (Allman braces, 120 col limit, 4-space indent). Member vars: `m_` prefix, statics: `s_`, globals: `g_`, constants: `UPPER_CASE`. Pre-commit hook enforces clang-format on staged `.cpp`/`.h` files.

## Pre-commit Hooks & CI

**Husky.Net** manages git hooks (`.husky/`). Auto-installed on `dotnet restore` via `Directory.Build.targets`. The pre-commit hook runs `.husky/clang-format-check.sh` on staged `*.cpp`/`*.h` files — fails the commit if formatting differs from `.clang-format`.

**CI** (`.github/workflows/build-and-test.yml`): two jobs — `build-windows` (msbuild + clang-tidy + tests) and `build-macos` (CMake + tests, `continue-on-error: true`). Set `HUSKY=0` to skip hook installation in CI.

## Snapshot Testing Workflow

Tests use Verify.Xunit: actual output compared against `*.verified.txt` files in `test/Metreja.IntegrationTests/Snapshots/`. When test behavior changes intentionally:

1. Run the failing test — Verify writes `*.received.txt` next to the verified file
2. Diff the received vs verified output
3. If the change is correct, replace the `.verified.txt` content with the `.received.txt` content
4. Commit the updated snapshot

## Versioning

GitVersion (ContinuousDeployment mode) drives semver. Bump via commit messages: `+semver: major|minor|patch|none`. `Directory.Build.props` version is fallback only.

## Terminology

- **session** (not "run") — `sessionId` in NDJSON events
- **methodId** (not functionId) — consistent with `methodName`/`className`
- **tsNs** suffix — all timestamps in nanoseconds (`enterTsNs`, `leaveTsNs`, `g_gcStartNs`)

## Known Pitfalls

- `COR_PRF_ENABLE_FRAME_INFO` must be in event mask for `SetEnterLeaveFunctionHooks3WithInfo`
- corprof.h requires corhdr.h + cor.h included first (use `include/profiling.h` wrapper)
- `_CRT_SECURE_NO_WARNINGS` needed in vcxproj for fopen/snprintf
- vcxproj OutDir must use `$(SolutionDir)\bin\` (backslash) to avoid path concatenation bug
- System.CommandLine 2.0.3 stable API: `Subcommands.Add`, `SetAction`, `ParseResult.InvokeAsync`
- C++ async state machine detection: `MethodCache` detects `MoveNext` on `IAsyncStateMachine` implementors and unwraps to the original method name
- Per-thread call stacks use TLS — `CallStackManager` maintains deferred unwind state for exception handling

## Adding a New Event Type

When adding a new NDJSON event type, update these files:

1. `src/Metreja.Profiler/ConfigReader.h` — add to `EventType` enum
2. `src/Metreja.Profiler/ConfigReader.cpp` — add event name parsing
3. `src/Metreja.Profiler/Profiler.cpp` — event mask setup in `Initialize()`
4. `src/Metreja.Profiler/NdjsonWriter.h/.cpp` — add `Write*` method
5. `src/Metreja.Tool/Commands/SetCommand.cs` — add to `ValidEventTypes`
6. `test/Metreja.IntegrationTests/Infrastructure/TraceEvent.cs` — add record type
7. `test/Metreja.IntegrationTests/Infrastructure/TraceParser.cs` — add parsing case

## Documentation

When changing user-facing features (new commands, new event types, changed CLI options, changed output formats), update:

1. `README.md` — event types table, command descriptions, architecture notes
2. `src/Metreja.Tool/README.md` — full CLI reference with options
3. `CLAUDE.md` — NDJSON event types, architecture, known pitfalls

When all changes are complete, verify tests pass before committing.

## Pull Request Reviews

Always respond to every review comment on PRs with an inline reply:
- **Fixed items:** State what was fixed and in which commit (e.g., "Fixed in abc1234 — added braces.")
- **Won't fix items:** Explain why with technical reasoning (e.g., clang-format output, verified correct behavior)
- Never leave review comments without a response

## Prerequisites

**Windows:**
- Windows 10/11
- Visual Studio 2022 Build Tools with "Desktop development with C++" workload
- .NET 10 SDK (multi-targets 8/9/10)

**macOS:**
- macOS 14+ (Apple Silicon / ARM64)
- Xcode Command Line Tools (provides clang++)
- CMake (`brew install cmake`)
- .NET 10 SDK (multi-targets 8/9/10)
