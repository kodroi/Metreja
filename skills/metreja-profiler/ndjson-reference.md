# Metreja NDJSON Output Reference

Metreja writes **Newline-Delimited JSON** — one event per line. The first event is always `run_metadata`, followed by `enter`, `leave`, and `exception` events.

## Event Type Schemas

### `run_metadata` (first event in file)

| Field | Type | Description |
|-------|------|-------------|
| `event` | string | Always `"run_metadata"` |
| `tsNs` | long | Timestamp in nanoseconds |
| `pid` | int | Process ID |
| `runId` | string | Run identifier (8-char hex) |
| `scenario` | string | Scenario name from session config |

```json
{"event":"run_metadata","tsNs":123456789,"pid":1234,"runId":"a1b2c3d4","scenario":"perf-test"}
```

### `enter` (method entry)

| Field | Type | Description |
|-------|------|-------------|
| `event` | string | Always `"enter"` |
| `tsNs` | long | Monotonic nanosecond timestamp (QPC-based) |
| `pid` | int | Process ID |
| `runId` | string | Run identifier |
| `tid` | int | OS thread ID |
| `depth` | int | Call stack depth (0 = top-level) |
| `asm` | string | Assembly name |
| `ns` | string | Namespace |
| `cls` | string | Class name |
| `m` | string | Method name (resolved for async) |
| `async` | bool | `true` if async state machine MoveNext |

```json
{"event":"enter","tsNs":123456789,"pid":1234,"runId":"a1b2c3d4","tid":5678,"depth":2,"asm":"MyApp","ns":"MyApp.Services","cls":"OrderService","m":"ProcessOrder","async":false}
```

### `leave` (method exit)

Same fields as `enter` plus:

| Field | Type | Description |
|-------|------|-------------|
| `deltaNs` | long | Elapsed nanoseconds from enter to leave |

```json
{"event":"leave","tsNs":123556789,"pid":1234,"runId":"a1b2c3d4","tid":5678,"depth":2,"asm":"MyApp","ns":"MyApp.Services","cls":"OrderService","m":"ProcessOrder","async":false,"deltaNs":100000}
```

### `exception` (exception thrown)

| Field | Type | Description |
|-------|------|-------------|
| `event` | string | Always `"exception"` |
| `tsNs` | long | Timestamp in nanoseconds |
| `pid` | int | Process ID |
| `runId` | string | Run identifier |
| `tid` | int | OS thread ID |
| `asm` | string | Assembly name |
| `ns` | string | Namespace |
| `cls` | string | Class name |
| `m` | string | Method name |
| `exType` | string | Exception type (e.g., `System.NullReferenceException`) |

Note: exception events do **not** have `depth` or `async` fields.

```json
{"event":"exception","tsNs":123656789,"pid":1234,"runId":"a1b2c3d4","tid":5678,"asm":"MyApp","ns":"MyApp.Services","cls":"OrderService","m":"ProcessOrder","exType":"System.InvalidOperationException"}
```

## Field Semantics

| Field | Meaning |
|-------|---------|
| `tsNs` | Monotonic nanosecond timestamp from `QueryPerformanceCounter`. Monotonically increasing within a run. |
| `deltaNs` | Wall-clock nanoseconds from method enter to leave. Always non-negative. Includes time spent in child calls. |
| `depth` | Call stack depth. 0 = top-level entry point. Increases by 1 for each nested call. |
| `tid` | OS thread ID. Use to separate interleaved events from concurrent threads. |
| `async` | `true` when the method is an async state machine `MoveNext`. The `m` field contains the original method name (not `MoveNext`). |
| `runId` | Unique per profiled execution. Matches the `run_metadata` event. |

## Async Method Interpretation

Async methods in .NET compile to state machine classes named `<MethodName>d__N`. The profiler resolves these automatically:

- The `m` field shows the **original method name** (e.g., `ProcessOrderAsync`), not `MoveNext`
- The `cls` field may show the state machine class name (e.g., `<ProcessOrderAsync>d__5`)
- `async` is `true` for these events
- **Continuations may appear on different thread IDs** than the initial call — filter by method name, not just tid, when tracing async flows
- A single async method call may produce multiple enter/leave pairs (one per state machine step)

## Analysis Algorithms

### Performance Hotspot Detection

1. Extract all `leave` events: `grep '"event":"leave"' trace.ndjson`
2. Parse JSON, sort by `deltaNs` descending
3. Take top-20 entries — these are the slowest individual method calls
4. Group by `asm.ns.cls.m` to find methods that are consistently slow vs. one-off spikes

### Exception Tracing

1. Find all `exception` events: `grep '"event":"exception"' trace.ndjson`
2. For each exception, note the line number in the file
3. Read 50 preceding lines to see the call stack leading to the exception
4. Use `tid` to filter — only events on the same thread are part of that call stack

### Call Tree Reconstruction

1. Filter events by a single `tid`
2. Process in order: `enter` increases depth, `leave` decreases depth
3. Use `depth` to build indentation: `depth * 2 spaces` per level
4. `exception` events mark where the normal flow broke

### Method Aggregation (for `analyze-diff`)

1. Create a key from `asm + "." + ns + "." + cls + "." + m`
2. Sum all `deltaNs` values for that key across the trace
3. Compare totals between base and comparison traces

## Timing Thresholds

| deltaNs Range | Human-Readable | Assessment |
|---------------|---------------|------------|
| < 1,000 ns | < 1 us | Trivial — ignore |
| 1,000 - 1,000,000 ns | 1 us - 1 ms | Normal method execution |
| 1,000,000 - 100,000,000 ns | 1 ms - 100 ms | Worth investigating |
| > 100,000,000 ns | > 100 ms | Critical hotspot — prioritize |

### Context for Thresholds

- **API endpoint latency budget**: typically 50-200 ms total
- **UI frame budget**: 16.7 ms (60 fps)
- **Database query**: 1-50 ms is typical; > 100 ms is slow
- **Serialization/deserialization**: usually < 5 ms unless large payloads

## Validation Rules

The NDJSON output follows these invariants (enforced by `test/validate.py`):

1. First event must be `run_metadata`
2. Each line must be valid JSON
3. All required fields must be present for each event type
4. Timestamps must be monotonically non-decreasing
5. `deltaNs` values must be non-negative
6. Enter/leave events should balance per thread (accounting for exceptions breaking the flow)
