# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Metreja is a .NET Call-Path Profiler: a native C++ DLL implementing ICorProfilerCallback3 with ELT3 hooks, paired with a C# CLI (`metreja`) for session management and trace analysis. Output is NDJSON (newline-delimited JSON).

## Build Commands

```bash
# Full build (CLI + native DLL + install as global tool)
build.bat

# Individual projects
dotnet build src/Metreja.Tool/Metreja.Tool.csproj -c Release
msbuild src/Metreja.Profiler/Metreja.Profiler.vcxproj /p:Configuration=Release /p:Platform=x64 "/p:SolutionDir=%CD%\\"

# Run integration tests
dotnet test test/Metreja.IntegrationTests/Metreja.IntegrationTests.csproj -c Release

# Run a single test
dotnet test test/Metreja.IntegrationTests/Metreja.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~SyncCallPathTests"

# Format C++ code
scripts/format-cpp.bat
```

Build outputs: CLI at `src/Metreja.Tool/bin/Release/net10.0/metreja.exe`, profiler DLL at `bin/Release/Metreja.Profiler.dll`.

## Architecture

**Two-component system:**

1. **Metreja.Profiler** (`src/Metreja.Profiler/`) — Native C++ DLL (vcxproj, v143 toolset, C++20, x64 only, MASM assembly helpers). Implements COM class factory with CLSID `{7C8F944B-4810-4999-BF98-6A3361185FC2}`. Attaches to .NET runtime via `COR_PROFILER` environment variables, hooks method enter/leave via ELT3, writes NDJSON traces.

2. **Metreja.Tool** (`src/Metreja.Tool/`) — C# CLI targeting net8.0/net9.0/net10.0. Uses System.CommandLine 2.0.3. Session-based: configs stored in `.metreja/sessions/{sessionId}.json`. Commands split across `Commands/` (session management) and `Analysis/` (hotspots, calltree, callers, memory, diff).

**Data flow:** CLI creates session config → `generate-env` sets profiler env vars → profiled app loads DLL → DLL writes NDJSON → CLI analysis commands read NDJSON.

**Tests** (`test/Metreja.IntegrationTests/`) — XUnit + Verify.Xunit snapshot testing. `ProfilerRunner` spawns `Metreja.TestApp` under the profiler, captures NDJSON, normalizes events, verifies against `Snapshots/` files.

## Code Style

**C#:** Warnings as errors, `AnalysisLevel: latest-recommended`, nullable enabled, file-scoped namespaces, Allman braces, 4-space indent. Private fields: `_camelCase`. Private methods go after public ones.

**C++:** Microsoft-based style via `.clang-format` (Allman braces, 120 col limit, 4-space indent). Member vars: `m_` prefix, statics: `s_`, globals: `g_`, constants: `UPPER_CASE`. Pre-commit hook enforces clang-format on staged `.cpp`/`.h` files.

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

## Prerequisites

- Windows 10/11 (profiler is x64/Windows-only)
- Visual Studio 2022 Build Tools with "Desktop development with C++" workload
- .NET 10 SDK (multi-targets 8/9/10)
