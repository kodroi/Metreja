# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

## [1.0.21] — 2026-04-07

### Added
- Auto-merge multi-PID output files after `metreja run` exits — produces a single timestamp-sorted file, deletes per-PID originals; skipped in `--detach` mode
- Multi-metric percentage comparison in `analyze-diff` — shows self-time, inclusive-time, and call count deltas with percentages
- `--top`, `--sort` (inclusive/self/calls/percent), and `--filter` options for `analyze-diff`
- Reusable `NdjsonMerger` helper extracted from `merge` command

### Fixed
- `metreja run` now resolves bare executable names (e.g. `dotnet`) from PATH instead of only looking relative to CWD
- Differentiated `Win32Exception` error codes in `run` for clearer error messages (file not found vs access denied)

## [1.0.9] — 2026-03-24

### Added
- Anonymous usage analytics with PostHog (opt-out via `METREJA_TELEMETRY_OPT_OUT` env var)

### Dependencies
- Bump `microsoft/setup-msbuild` from 2 to 3
- Bump `dawidd6/action-download-artifact` from 18 to 19
- Bump `gittools/actions` from 4.3.3 to 4.4.2
- Bump `GitVersion.MsBuild` from 6.6.1 to 6.6.2

## [1.0.6] — 2026-03-16

### Fixed
- Async enter/leave test made resilient to non-deterministic continuations

## [1.0.1] — 2026-03-15

### Added
- JSON output format (`--format json`) for all analysis commands
- CSV export via `export` command
- Explicit exit codes from command handlers
- Comprehensive README with run, flush, event types, and JSON format docs

### Fixed
- Preserve GC gen/duration in timeline JSON output
- Fix `durationNs` field name in timeline events
- Make `ContentionEvent` and `AllocCallSite` tests resilient when no events are emitted

## [1.0.0] — 2026-03-15

### Added

#### Analysis Commands
- `summary` command: trace overview with event counts, duration, threads, methods
- `hotspots` command: per-method timing hotspots with self time, inclusive time, and sorting
- `calltree` command: call tree for a specific method invocation with timing
- `callers` command: show which methods call a specific method
- `memory` command: GC summary and allocation hotspots by class
- `exceptions` command: rank exception types by frequency with throw-site methods
- `timeline` command: chronological event listing with tid/event/method filtering
- `threads` command: per-thread breakdown with call counts, timing, activity windows
- `trend` command: method performance trend across periodic stats flushes
- `diff` command: compare two NDJSON profiling outputs side-by-side
- `check` command: CI regression gate comparing two traces with exit codes

#### CLI Utilities
- `run` command: launch an executable with profiler env vars attached (`--detach` for GUI apps)
- `list` command: list existing profiling sessions
- `merge` command: combine multiple NDJSON trace files sorted by timestamp
- `export` command: convert traces to speedscope or CSV format for visualization and analysis
- `set events` command: configure which event types to capture
- `set stats-flush-interval` command: set periodic stats flush interval

#### Profiler Enhancements
- Upgrade to ICorProfilerCallback10 (CB4-CB10 stubs)
- Async wall-time tracking via per-thread nesting maps (`wallTimeNs` on async leave events)
- Lock contention events via EventPipe (`contention_start`/`contention_end`)
- Allocation call-site attribution with method-level detail (`allocM`, `allocNs`, `allocCls`)
- Tailcall detection annotated on leave events
- macOS ARM64 platform support via CMake and GAS assembly

#### Other
- Tailcall and exception count columns in hotspots output
- Exception event markers in calltree output
- Assembly-level and namespace-level include/exclude filtering
- Method-level filters across analysis commands
- `method_stats` and `exception_stats` periodic aggregated statistics events
- JSON output: all analysis commands support `--format json` for structured output
- Snapshot testing with Verify.Xunit for trace validation

## [0.3.10] — 2026-03-13

### Added
- macOS ARM64 platform support via CMake and GAS assembly
- Cross-platform manual flush via POSIX named semaphores

## [0.3.9] — 2026-03-13

### Added
- `flush` CLI command for manual stats flush via named event IPC

## [0.3.8] — 2026-03-13

### Added
- Periodic stats flush to prevent data loss on force-kill

## [0.3.7] — 2026-03-11

### Added
- `report` command for creating GitHub issues via `gh` CLI

## [0.3.6] — 2026-03-09

### Fixed
- Exception-unwind double-pop: use deferred-pop strategy for catcher identification

## [0.3.5] — 2026-03-08

### Added
- `method_stats` event support in `hotspots` and `analyze-diff` commands

## [0.3.4] — 2026-03-08

### Added
- Shell format for `generate-env` command
- Explicit exit codes from command handlers

## [0.3.3] — 2026-03-05

### Added
- Event type system with `set events` command and in-profiler aggregation

### Removed
- `trackMemory` field (replaced by `events`)

## [0.3.1] — 2026-03-02

### Changed
- Simplified filter rules to single-level model with default framework excludes

## [0.3.0] — 2026-03-02

### Added
- `run` command to launch profiled executables with env vars attached

## [0.2.4] — 2026-03-01

### Removed
- Dead CLI flags (`--log-lines`, `set mode`, `output.format`)

## [0.2.3] — 2026-03-01

### Removed
- `--dll-path` option (profiler DLL auto-discovered from NuGet package)

## [0.2.2] — 2026-03-01

### Fixed
- Native profiler DLL missing from NuGet tool package

## [0.2.0] — 2026-02-28

### Changed
- Multi-target net8.0/net9.0/net10.0

## [0.1.0] — 2026-02-28

### Added
- Initial release — .NET call-path profiler with ELT3 hooks
- C# CLI for session management (`init`, `add`, `set`, `validate`, `generate-env`, `clear`)
- GC events and allocation-by-class memory profiling
- Analysis commands (`hotspots`, `calltree`, `callers`, `memory`, `diff`)
- NDJSON output format
- NuGet packaging as .NET global tool
