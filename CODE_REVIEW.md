# Code Review: POLA, DRY, and SOLID Patterns

## Summary

The Metreja codebase is well-structured overall. The two-component architecture (native C++ profiler + C# CLI) has clean separation of concerns and the streaming NDJSON design is solid. Below are specific findings organized by principle.

---

## DRY (Don't Repeat Yourself)

### 1. `--session` option duplicated across 8 commands (HIGH)

**Files:** All files in `src/Metreja.Tool/Commands/`

Every command that needs a session re-declares the identical option:

```csharp
var sessionOption = new Option<string>("--session", "-s")
{
    Description = "Session ID",
    Required = true
};
```

This appears verbatim in `AddCommand`, `SetCommand`, `RunCommand`, `GenerateEnvCommand`, `ClearCommand`, `ClearFiltersCommand`, `RemoveCommand`, and `ValidateCommand`. If the session option behavior ever changes (e.g., adding validation, defaulting to last session, renaming), 8 files must be updated.

**Recommendation:** Extract to a shared factory:
```csharp
// In a CommandOptions or SharedOptions static class
public static Option<string> SessionOption() => new("--session", "-s")
{
    Description = "Session ID",
    Required = true
};
```

### 2. `new ConfigManager()` instantiated per command invocation (MEDIUM)

**Files:** 9 occurrences across `src/Metreja.Tool/Commands/`

Every command handler creates `new ConfigManager()` independently. `ConfigManager` is stateless beyond `_sessionsDir` which always resolves to the same path. This is not a performance issue but a maintenance concern — if `ConfigManager` gains constructor parameters or becomes an interface for testing, every call site needs updating.

**Recommendation:** Consider a shared factory method or a single static/singleton accessor, or pass `ConfigManager` as a parameter from `Program.cs`.

### 3. `FindLastPathSeparator` duplicated in C++ (LOW)

**Files:** `src/Metreja.Profiler/NdjsonWriter.cpp:7` and `src/Metreja.Profiler/MethodCache.cpp:4`

Identical 6-line static function defined in both files. Trivial, but still a DRY violation.

**Recommendation:** Move to a shared utility header (e.g., `StringUtils.h` or a `pal_path.h`).

### 4. NDJSON line parsing boilerplate repeated across all analyzers (MEDIUM)

**Files:** `HotspotsAnalyzer.cs`, `CallTreeAnalyzer.cs`, `CallersAnalyzer.cs`, `MemoryAnalyzer.cs`, `DiffAnalyzer.cs`

Every analyzer repeats this exact pattern:
```csharp
await foreach (var line in File.ReadLinesAsync(filePath))
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    try
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (!root.TryGetProperty("event", out var eventProp))
            continue;
        var eventType = eventProp.GetString();
        // ... process events
    }
    catch (JsonException) { }
}
```

This is ~10 lines of identical scaffolding in every analyzer.

**Recommendation:** Extract to `AnalyzerHelpers` as a streaming event iterator:
```csharp
public static async IAsyncEnumerable<(string EventType, JsonElement Root)> StreamEventsAsync(string path) { ... }
```

### 5. `LeaveStub` and `TailcallStub` are nearly identical (LOW)

**File:** `src/Metreja.Profiler/Profiler.cpp:617-671`

These two functions share ~15 identical lines (Pop, compute inclusive/self, CreditParent, RecordMethod, WriteLeave). The only difference is the `true` tailcall flag in `TailcallStub`.

**Recommendation:** Extract the shared body into a `ProcessLeave(ctx, info, funcId, tsNs, tid, isTailcall)` helper. This is a hot path though, so verify the compiler inlines it.

### 6. `NdjsonWriter::WriteEnter` / `WriteLeave` method name resolution (LOW)

**File:** `src/Metreja.Profiler/NdjsonWriter.cpp:103-105, 125-127, 226-228`

The async method name resolution logic is repeated 3 times:
```cpp
const char* methodName = info.isAsyncStateMachine && !info.originalMethodName.empty()
    ? info.originalMethodName.c_str()
    : info.methodName.c_str();
```

**Recommendation:** Add `const char* GetDisplayName() const` to `MethodInfo`.

### 7. `MatchesAnyFilter` exists with different signatures (LOW)

**Files:** `HotspotsAnalyzer.cs:162` and `MemoryAnalyzer.cs:134`

Two private `MatchesAnyFilter` methods with different signatures — one takes `(filters, ns, cls, m, fullKey)`, the other takes `(filters, className)`. While the logic differs, the concept is the same and could live in `AnalyzerHelpers` with overloads.

---

## SOLID Principles

### Single Responsibility (SRP)

**Overall:** Good. Each component has a clear, focused responsibility.

**Finding 1: Analyzers mix data aggregation with console output (MEDIUM)**

**Files:** All analyzers in `src/Metreja.Tool/Analysis/`

Each analyzer (e.g., `HotspotsAnalyzer`) combines three responsibilities:
1. File validation
2. Data aggregation (streaming JSON parsing)
3. Console formatting and output

The `AnalyzeAsync` methods write directly to `Console.WriteLine`. This couples analysis logic to a specific output format, making it hard to:
- Unit test the aggregation without capturing console output
- Reuse the aggregation for different output formats (JSON, CSV, HTML)

**Recommendation:** Split into aggregation (returns data) and presentation (formats and prints). The aggregation methods (`AggregateAsync`) are already private — make them return results that a separate formatter consumes.

### Open/Closed Principle (OCP)

**Finding 2: Event type dispatch uses string matching (LOW)**

**Files:** All analyzers, `ConfigReader.cpp`

Event types are dispatched via string comparison (`eventType == "enter"`, `eventType == "leave"`, etc.). Adding a new event type requires modifying every analyzer's switch/if chain.

This is acceptable for the current scope (8 event types, 5 analyzers) and the streaming JSON nature of the data makes an enum-based approach awkward. Noting it for awareness.

**Finding 3: `SetCommand` subcommands are manually registered (LOW)**

**File:** `src/Metreja.Tool/Commands/SetCommand.cs`

Each settable property requires a dedicated `CreateXxxCommand` method. Adding a new config property means writing a new method and registering it in `Create()`. The `SetConfigPropertyAsync` helper already generalizes the save pattern, which is good. The per-property boilerplate is inherent to System.CommandLine's design.

### Liskov Substitution (LSP)

No violations found. The codebase uses records and static classes extensively rather than inheritance hierarchies, which naturally avoids LSP issues.

### Interface Segregation (ISP)

**Finding 4: `ICorProfilerCallback3` forces ~70 no-op stub methods (INHERENT)**

**File:** `src/Metreja.Profiler/Profiler.cpp:233-299+`

The profiler must implement the full `ICorProfilerCallback3` interface even though only ~5 methods are active. This is imposed by the .NET profiling API and is unavoidable. The stub implementations are correctly minimal (`return S_OK;`).

### Dependency Inversion (DIP)

**Finding 5: Analyzers and commands depend on concrete `ConfigManager` (MEDIUM)**

**Files:** All command files in `src/Metreja.Tool/Commands/`

Commands directly instantiate `new ConfigManager()` inside their action handlers. This makes it impossible to inject a test double or alternative implementation. The analyzers are slightly better — they take a file path string rather than depending on `ConfigManager`.

**Recommendation:** Either:
- Accept `ConfigManager` as a constructor parameter or delegate parameter
- Register it in a simple DI container or pass through `ParseResult` middleware
- At minimum, extract an `IConfigManager` interface

**Finding 6: `Console.WriteLine` used directly throughout (LOW)**

**Files:** All analyzers and commands

Direct `Console` dependency means output can't be redirected programmatically (the tests work around this with `Console.SetOut`). For a CLI tool this is idiomatic, but it does limit testability.

---

## POLA (Principle of Least Astonishment)

### Finding 7: `HotspotsAnalyzer.MatchesAnyFilter` differs from `AnalyzerHelpers.MatchesPattern` (MEDIUM)

**Files:** `HotspotsAnalyzer.cs:162` vs `AnalyzerHelpers.cs:23`

Both do pattern matching against method components, but with different semantics:
- `AnalyzerHelpers.MatchesPattern` — matches method name, `cls.m`, `ns.cls.m`, or substring of full name
- `HotspotsAnalyzer.MatchesAnyFilter` — matches full key via `Contains`, or exact match on `m`, `cls`, or `ns`

A user filtering with the same pattern string may get different results from `hotspots --filter Foo` vs `calltree Foo`. This is surprising.

**Recommendation:** Unify the matching logic into `AnalyzerHelpers.MatchesPattern` and use it consistently across all analyzers.

### Finding 8: `GenerateEnvCommand` and `RunCommand` resolve profiler path differently (LOW)

**Files:** `GenerateEnvCommand.cs:43-44` vs `RunCommand.cs:47-48`

Both resolve the profiler DLL path, but:
- `GenerateEnvCommand` calls `ProfilerLocator.GetDefaultProfilerPath()` and mentions "Searched adjacent to CLI assembly and bin/Release/"
- `RunCommand` calls the same method but says "Ensure Metreja.Profiler.dll is adjacent to the CLI assembly"

The error messages describe different search behavior, which could confuse users about where to place the DLL.

**Recommendation:** Unify error messages. Consider moving the validation into `ProfilerLocator` itself.

### Finding 9: `DiffAnalyzer` silently aggregates timings by addition (LOW)

**File:** `src/Metreja.Tool/Analysis/DiffAnalyzer.cs:71`

```csharp
timings[key] = timings.GetValueOrDefault(key, 0) + timing.Value;
```

If a method appears multiple times (e.g., called 100 times), the "timing" shown is the sum of all invocations, not the average. For `method_stats` events, `totalInclusiveNs` is already an aggregate, so adding multiple `method_stats` events is correct. But for `leave` events, summing all invocations may not be what users expect when comparing two runs with different call counts.

**Recommendation:** Document this behavior or consider showing per-call averages alongside totals.

### Finding 10: `WriteLeave` tailcall format differs silently (LOW)

**File:** `src/Metreja.Profiler/NdjsonWriter.cpp:129-148`

The tailcall variant of `WriteLeave` appends `,"tailcall":true` to the JSON. The non-tailcall variant omits the field entirely (rather than writing `"tailcall":false`). This means consumers must treat missing `tailcall` as `false`. While this is a valid sparse-JSON approach, it differs from the `"async"` field which is always present (as `true` or `false`).

**Recommendation:** Either always emit `tailcall` (matching `async`'s behavior) or never emit `async:false` (matching `tailcall`'s sparse approach). Pick one convention.

---

## Positive Patterns Worth Noting

1. **Immutable config records** — `ProfilerConfig` uses C# records with `init` properties, preventing accidental mutation
2. **Double-checked locking in MethodCache** — Correct use of `shared_lock` for reads, `unique_lock` for writes
3. **JIT-time filtering** — `FunctionIDMapper2` prevents instrumentation of excluded methods, avoiding runtime overhead
4. **RAII throughout C++** — `unique_ptr` ownership, lock guards, automatic cleanup
5. **`PrepareStubContext` extraction** — Common validation already factored out of ELT stubs
6. **Per-thread TLS** — Both `CallStackManager` and `StatsAggregator` avoid global locks on hot paths
7. **`AnalyzerHelpers`** — Common utilities already centralized (FormatNs, BuildMethodKey, ExtractMethodInfo)
8. **Streaming analysis** — All analyzers process NDJSON line-by-line without loading entire files into memory
