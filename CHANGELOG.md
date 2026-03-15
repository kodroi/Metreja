# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
- `export` command: convert traces to speedscope format for visualization
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
- Snapshot testing with Verify.Xunit for trace validation
