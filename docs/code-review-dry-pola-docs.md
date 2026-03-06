# Code Review: DRY, POLA & Documentation Consistency

**Date:** 2026-03-06
**Scope:** Full codebase — Metreja.Profiler (C++), Metreja.Tool (C#), IntegrationTests

---

## Executive Summary

| Category | C++ Profiler | C# CLI | Tests | Total |
|----------|-------------|--------|-------|-------|
| DRY violations | 8 | 7 | 8 | **23** |
| POLA violations | 8 | 10 | 6 | **24** |
| Documentation gaps | 4 | 3 | 6 | **13** |

The most impactful findings are:
1. **NDJSON parsing loop** copy-pasted 6 times across C# analyzers
2. **Leave/tailcall/unwind logic** triplicated in the C++ profiler
3. **Test helper methods** (`WriteTraceToTempFileAsync`, `EventToNdjson`, `CaptureConsoleOutputAsync`) copy-pasted across 3 test classes
4. **Zero XML doc comments** on any public C# type or method
5. **`SetCommand` missing exit code** on validation error — scripts cannot detect failure

---

## 1. C++ Profiler (`src/Metreja.Profiler/`)

### 1.1 DRY Violations

#### D-CPP-1: Leave/Tailcall/ExceptionUnwind logic triplicated
**Files:** `Profiler.cpp` — `LeaveStub` (515-530), `TailcallStub` (543-558), `ExceptionUnwindFunctionEnter` (356-372)

The same 12-line leave-path block (pop call entry, compute inclusive/self time, credit parent, conditionally record stats + write leave event) is copy-pasted three times. Only `TailcallStub` passes `true` for the tailcall parameter.

**Fix:** Extract `HandleLeave(ctx, info, funcId, tsNs, tid, bool isTailcall)`.

#### D-CPP-2: `FindLastPathSeparator` duplicated across files
**Files:** `MethodCache.cpp` (3-9), `NdjsonWriter.cpp` (7-13)

Identical static function in both files.

**Fix:** Move to a shared `StringUtils.h`.

#### D-CPP-3: Duplicated `IMetaDataImport` acquisition pattern
**File:** `MethodCache.cpp` — `ResolveAndCache` (53-72) and `ResolveClassName` (163-172)

The `GetModuleMetaData` -> `QueryInterface` -> error check -> `Release` sequence appears twice.

**Fix:** Extract a private helper returning `IMetaDataImport*`.

#### D-CPP-4: Triplicated cache-miss bail-out
**File:** `MethodCache.cpp` (42-46, 58-60, 68-70)

Three identical blocks: set `isIncluded = false`, acquire lock, emplace in cache, return.

#### D-CPP-5: `snprintf` + length-check + write boilerplate in all 9 NdjsonWriter methods
**File:** `NdjsonWriter.cpp`

Every `Write*` method repeats the same `char line[2048]; int len = snprintf(...); if (len > 0 && ...)` pattern.

**Fix:** A templated or macro-based `FormatAndWrite(fmt, ...)` helper.

#### D-CPP-6: `"pid":%lu,"sessionId":"%s"` repeated in all 9 write methods
**File:** `NdjsonWriter.cpp` (lines 88, 108, 130, 139, 157, 171, 188, 208, 222, 239)

#### D-CPP-7: Async method name resolution repeated 3 times
**File:** `NdjsonWriter.cpp` (105, 124, 219)

```cpp
const char* methodName = info.isAsyncStateMachine ? info.originalMethodName.c_str() : info.methodName.c_str();
```

**Fix:** Add a `displayName()` accessor on `MethodInfo`.

#### D-CPP-8: `needElt` condition duplicated from event mask logic
**File:** `Profiler.cpp` (90-93 vs 114-115)

Same compound condition computed independently. If one changes without the other, behavior diverges silently.

### 1.2 POLA Violations

#### P-CPP-1: `g_ctx` is a non-atomic global pointer accessed across threads
**File:** `Profiler.cpp` (line 10)

Written in `Initialize()`/`Shutdown()`, read from ELT stubs and GC callbacks on arbitrary threads. Formally a data race under the C++ memory model.

**Fix:** Use `std::atomic<ProfilerContext*>`.

#### P-CPP-2: Inconsistent event write paths in NdjsonWriter
**File:** `NdjsonWriter.cpp`

Some methods use `WriteLockedEvent` (acquires mutex + increments `m_eventCount`). Others manually acquire the mutex and call `AppendToBuffer` directly, bypassing `m_eventCount`. A reader would expect all write methods to go through the same code path.

#### P-CPP-3: `WriteSessionMetadata` silently bypasses event counting
**File:** `NdjsonWriter.cpp` (84-97)

Other bypass methods have comments explaining why. This one has no such comment.

#### P-CPP-4: `WriteLeave` duplicates entire format string for tailcall vs non-tailcall
**File:** `NdjsonWriter.cpp` (117-147)

The full JSON format string is duplicated in an if/else branch, differing only by `,"tailcall":true`. Could conditionally append the suffix instead.

#### P-CPP-5: `ThreadCallStack::exceptionCatcherFunctionId` is dead code
**File:** `CallStack.h` (line 18)

Declared but never read or written anywhere.

#### P-CPP-6: Inconsistent TLS access patterns in CallStack
**File:** `CallStack.cpp`

`GetDepth()` and `CreditParent()` use `TlsGetValue` directly, while `Push()`/`Pop()` go through `GetOrCreateStack()`.

#### P-CPP-7: Magic numbers for placeholder replacement lengths
**File:** `ConfigReader.cpp`

```cpp
result.replace(pos, 11, sessionId);  // 11 = length of "{sessionId}"
result.replace(pos, 5, pidStr);      // 5 = length of "{pid}"
```

**Fix:** Use `strlen("{sessionId}")` or a named constant.

#### P-CPP-8: `"Unknown"` magic string returned from 4 error paths
**File:** `MethodCache.cpp` (161, 165, 172, 182)

**Fix:** Define `constexpr char UNKNOWN_CLASS[] = "Unknown";`

### 1.3 Documentation / Terminology

#### DOC-CPP-1: `CallEntry::functionId` should be `methodId`
**File:** `CallStack.h` (line 10)

Per CLAUDE.md: "methodId (not functionId)". The struct field is named `functionId`.

#### DOC-CPP-2: Multiple internal parameters named `functionId` instead of `methodId`
**Files:** `CallStack.h` (32), `MethodCache.h` (34-36), `MethodCache.cpp` (17, 148), `StatsAggregator.h` (46)

The `FunctionID` type is from the CoreCLR API and cannot change, but parameter/variable names could be `methodId`.

#### DOC-CPP-3: `ProfilerContext` members lack `m_` prefix
**File:** `ProfilerContext.h`

All members (`config`, `sessionId`, `methodCache`, etc.) lack the `m_` prefix mandated by the coding style. Borderline since it is a plain struct with all-public members, but inconsistent with every other class.

#### DOC-CPP-4: No doc comments on public methods of core classes
**Files:** `NdjsonWriter.h`, `StatsAggregator.h`, `CallStack.h`, `MethodCache.h`

Notable gaps:
- `CallStackManager::CreditParent` — purpose unclear from name alone
- `NdjsonWriter::CheckEventLimit` — whether it is thread-safe
- `StatsAggregator::Flush` — that it must be called single-threaded at shutdown
- `MethodCache::ShouldHook` — that it resolves and caches as a side effect

---

## 2. C# CLI (`src/Metreja.Tool/`)

### 2.1 DRY Violations

#### D-CS-1: NDJSON file-reading boilerplate duplicated 6 times across 5 analyzers
**Files:** `CallersAnalyzer.cs` (47-101), `HotspotsAnalyzer.cs` (54-131), `CallTreeAnalyzer.cs` (51-105 and 117-188), `MemoryAnalyzer.cs` (26-77), `DiffAnalyzer.cs` (40-68)

Every analyzer contains the same `ReadLinesAsync` → skip blank → `JsonDocument.Parse` → get `"event"` property → `catch JsonException` loop.

**Fix:** Add `AnalyzerHelpers.EnumerateEventsAsync(string filePath)` returning `IAsyncEnumerable<(JsonElement root, string eventType)>`.

#### D-CS-2: Thread-stack management pattern duplicated 3 times
**Files:** `CallersAnalyzer.cs` (61-68), `HotspotsAnalyzer.cs` (68-75), `CallTreeAnalyzer.cs` (64-73)

All three maintain a `Dictionary<long, Stack<T>>` with identical get-or-create logic.

**Fix:** Generic `GetOrCreateStack<T>()` helper.

#### D-CS-3: `--session` / `-s` option defined identically in 7 commands
**Files:** `AddCommand.cs`, `RemoveCommand.cs`, `ClearFiltersCommand.cs`, `GenerateEnvCommand.cs`, `SetCommand.cs`, `ValidateCommand.cs`, `RunCommand.cs` (all lines 10-14)

**Fix:** `CommandHelpers.CreateSessionOption(bool required = true)`.

#### D-CS-4: `new ConfigManager()` instantiated independently in 9 commands

If the constructor or base directory logic changes, all nine sites must be updated.

#### D-CS-5: Filter validation logic duplicated between AddCommand and RemoveCommand
**Files:** `AddCommand.cs` (68-82), `RemoveCommand.cs` (52-66)

Identical "exactly one of --assembly/--namespace/--class/--method" check with identical error messages.

#### D-CS-6: Three different filter-matching implementations
- `AnalyzerHelpers.MatchesPattern` — matches method name, class.method, or full qualified name
- `HotspotsAnalyzer.MatchesAnyFilter` — matches on fullKey, method, class, or namespace individually
- `MemoryAnalyzer.MatchesAnyFilter` — simple `Contains` on class name only

These do the same conceptual thing ("does this method match a filter?") with diverging semantics.

#### D-CS-7: Method-key construction diverges between DiffAnalyzer and everywhere else
`AnalyzerHelpers.BuildMethodKey` produces `"{ns}.{cls}.{m}"`, but `DiffAnalyzer` builds keys as `"{asm}.{ns}.{cls}.{m}"` including the assembly name. Same method gets different keys depending on which analyzer produced them.

### 2.2 POLA Violations

#### P-CS-1: `SetCommand` does not set exit code on validation error
**File:** `SetCommand.cs` (136-142)

Every other command sets `Environment.ExitCode = 1` on failure. This one silently returns exit code 0 after printing an error. Scripts checking `$?` will not detect the failure.

#### P-CS-2: `DiffAnalyzer` prints raw nanoseconds while all others use human-readable formatting
All other analyzers use `AnalyzerHelpers.FormatNs()` to display "1.23ms" or "45.67us". `DiffAnalyzer` prints raw numeric values with column headers like `"Base (ns)"`.

#### P-CS-3: `DefaultFilters.Excludes` allocates a new list on every access
**File:** `ProfilerConfig.cs` (57-64)

Expression-bodied property creates new `List` + `FilterRule` objects per access. Callers expect a property to return the same reference.

**Fix:** Cache in a static field or change to a method.

#### P-CS-4: `GenerateEnvCommand` emits output with empty profiler path after error
**File:** `GenerateEnvCommand.cs` (44-53)

When `--force` is used and the DLL is missing, the error is printed but the script is still generated with an empty `CORECLR_PROFILER_PATH`. User sees both an error AND valid-looking output.

#### P-CS-5: `RunCommand` does not handle spaces in arguments
**File:** `RunCommand.cs` (line 80)

`Arguments = string.Join(' ', extraArgs)` — if any element contains spaces (e.g., `"C:\Program Files\app.exe"`), the naive join splits it into multiple arguments.

#### P-CS-6: `Truncate` keeps the *end* of the string (opposite of typical truncation)
**File:** `AnalyzerHelpers.cs` (18-21)

Name `Truncate` implies keeping the beginning. This keeps the end and prepends `"..."`.

**Fix:** Rename to `TruncateStart` or `EllipsisLeft`.

#### P-CS-7: `CallTreeAnalyzer` `--occurrence 1` means "slowest", not "first"
**File:** `CallTreeAnalyzer.cs` (line 22)

The word "occurrence" strongly implies temporal ordering. While the help text says "(1 = slowest)", the parameter name itself is misleading.

#### P-CS-8: `ClearCommand` action is `async` but performs no async work
**File:** `ClearCommand.cs` (line 24)

Generates a compiler warning and misleads readers.

#### P-CS-9: `UpdateChecker` stores cache in `~/.metreja/` while sessions are in `./.metreja/`
**File:** `UpdateChecker.cs` (7-8)

Split-brain storage is undocumented. A user inspecting `.metreja/` in their project won't find the update cache.

#### P-CS-10: `UpdateChecker` assumes NuGet versions array is non-empty
**File:** `UpdateChecker.cs` (line 57)

`versions[versions.GetArrayLength() - 1]` throws `IndexOutOfRangeException` on empty array. Catch-all prevents crash but silently swallows the error.

### 2.3 Documentation

#### DOC-CS-1: Zero XML doc comments across the entire codebase
None of the 26 C# files contain any `/// <summary>` doc comments. All public types and methods are undocumented, especially the config records (`ProfilerConfig`, `MetadataConfig`, `InstrumentationConfig`, `OutputConfig`, `FilterRule`) that define the user-facing JSON schema.

#### DOC-CS-2: Inconsistent visibility modifiers
`AnalyzerHelpers` is `internal static class` but all five analyzers are `public static class`. No consistent policy — everything could safely be `internal` since this is a CLI tool with no library consumers.

#### DOC-CS-3: No terminology violations
The C# codebase correctly follows CLAUDE.md terminology: "session" (not "run"), "methodId" (not "functionId"), `tsNs` suffix.

---

## 3. Tests (`test/Metreja.IntegrationTests/`)

### 3.1 DRY Violations

#### D-TEST-1: `WriteTraceToTempFileAsync()` copy-pasted across 3 test classes
**Files:** `CallTreeAnalysisTests.cs` (68-74), `CallersAnalysisTests.cs` (60-66), `HotspotsAnalysisTests.cs` (67-73)

Character-for-character identical.

#### D-TEST-2: `EventToNdjson()` copy-pasted across the same 3 classes
**Files:** `CallTreeAnalysisTests.cs` (76-88), `CallersAnalysisTests.cs` (68-80), `HotspotsAnalysisTests.cs` (75-87)

Maintenance hazard — if a new field is added to a `TraceEvent` subtype, all three copies must be updated.

#### D-TEST-3: `CaptureConsoleOutputAsync()` copy-pasted across 3 classes
**Files:** `CallTreeAnalysisTests.cs` (90-104), `CallersAnalysisTests.cs` (82-96), `HotspotsAnalysisTests.cs` (89-103)

#### D-TEST-4: `CaptureConsoleErrorAsync()` copy-pasted across 2 classes
**Files:** `CallTreeAnalysisTests.cs` (106-120), `CallersAnalysisTests.cs` (98-112)

**Fix for D-TEST-1 through D-TEST-4:** Extract all four methods to a shared `AnalysisTestHelpers` class in `Infrastructure/`.

#### D-TEST-5: Duplicated event-filtering pattern with inconsistent implementations
**Files:** `SyncCallPathTests.cs` (20-27) uses ternary, `DeepRecursionTests.cs` (17-24) and `ExceptionPathTests.cs` (20-28) use if/else if. Same logic, different code.

**Fix:** Shared `FilterByMethods(events, methodSet)` utility.

#### D-TEST-6: `GetSolutionRoot()` duplicated in MethodStatsTests
**File:** `MethodStatsTests.cs` (7-14)

Duplicates logic already in `ProfilerSessionFixture.InitializeAsync()`.

#### D-TEST-7: Duplicated enter/leave counting pattern
**Files:** `StructuralValidationTests.cs` (115, 126), `MethodStatsTests.cs` (125-126)

#### D-TEST-8: Magic numbers 1000 and 21 repeated without shared constants
Both `StructuralValidationTests` and `MethodStatsTests` assert the same expected call counts (1000 for LoopBody, 21 for Recurse) without a shared constant.

### 3.2 POLA Violations

#### P-TEST-1: `MethodStatsTests` does NOT use the shared fixture
All other test classes use `[Collection("ProfilerSession")]` and share a single profiler run. `MethodStatsTests` has no `[Collection]` attribute and spawns a separate profiler process for every `[Fact]` (5 tests). No comment explains why.

#### P-TEST-2: `HotspotsAnalysisTests` lacks error-path test unlike siblings
`CallTreeAnalysisTests` and `CallersAnalysisTests` both test `NonExistentMethod_ShowsError`. `HotspotsAnalysisTests` has no error-path test at all.

#### P-TEST-3: `AsyncCallPathTests` uses assertions while sibling tests use snapshots
`SyncCallPathTests`, `ExceptionPathTests`, and `DeepRecursionTests` all use `_MatchesSnapshot`. `AsyncCallPathTests` uses assertion-based tests with no snapshot. No comment explains the inconsistency.

#### P-TEST-4: `LeaveEvent` inherits from `EnterEvent` — semantically wrong
**File:** `TraceEvent.cs` (line 27)

Forces awkward `e is EnterEvent and not LeaveEvent` guards everywhere (5 locations). A leave event "is-a" enter event is semantically incorrect. Both share fields but a common base type (`MethodEvent`) would be less surprising.

#### P-TEST-5: Temp files from `WriteTraceToTempFileAsync` are never cleaned up
All three analysis test classes create temp files but never delete them. By contrast, `ProfilerRunner` properly implements `IAsyncDisposable`.

#### P-TEST-6: `TraceNormalizer.Normalize` allocates `StringBuilder` at top but uses it 20 lines later
**File:** `TraceNormalizer.cs`

`StringBuilder sb` is created at line 9, lines are accumulated into a `List<string>`, then finally at lines 32-33 `sb.AppendLine` is called. The early allocation is misleading.

### 3.3 Documentation

#### DOC-TEST-1: `asyncMethods` set duplicated within the same file with no shared constant
**File:** `AsyncCallPathTests.cs` (lines 15 and 44)

#### DOC-TEST-2: TestApp prints "All tests completed" but it runs profiling scenarios, not tests
**File:** `test/Metreja.TestApp/Program.cs` (line 20)

#### DOC-TEST-3: Misleading comment in `StructuralValidationTests`
**File:** `StructuralValidationTests.cs` (line 106)

Comment says "Enter count >= leave count" but the assertion also accepts negative balance when exceptions exist.

#### DOC-TEST-4: Inconsistent error messages for same situation
`CallTreeAnalysisTests` checks for `"No invocations found"`, `CallersAnalysisTests` checks for `"No calls found"`. Same conceptual error, different wording.

#### DOC-TEST-5: `ExceptionEvent` lacks `Depth` field unlike siblings — no comment explaining why
**File:** `TraceEvent.cs`

#### DOC-TEST-6: `TraceParser` handles `method_stats`/`exception_stats` but `TraceNormalizer` does not — no comment
**File:** `TraceNormalizer.cs`

These event types fall through to `"unknown event"` with no documented intent.

---

## Prioritized Recommendations

### High Priority (correctness / behavioral bugs)

| ID | Finding | Impact |
|----|---------|--------|
| P-CS-1 | `SetCommand` missing exit code on error | Scripts silently see success on failure |
| P-CS-5 | `RunCommand` doesn't quote spaced arguments | Profiled apps with spaces in paths will fail |
| P-CPP-1 | `g_ctx` non-atomic global pointer | Formal data race under C++ memory model |
| P-CS-4 | `GenerateEnvCommand` emits output after error | User gets broken env script with no clear signal |
| D-CS-7 | `DiffAnalyzer` method-key divergence | Cross-analyzer results are incomparable |

### Medium Priority (maintainability / DRY)

| ID | Finding | Impact |
|----|---------|--------|
| D-CS-1 | NDJSON parsing loop x6 | Any parsing change requires 6 edits |
| D-CPP-1 | Leave/tailcall/unwind logic x3 | Any leave-path change requires 3 edits |
| D-TEST-1..4 | Test helpers x3 each | Any trace format change requires 3 edits |
| D-CS-3 | `--session` option x7 | Any option change requires 7 edits |
| D-CS-6 | Three different filter-matching implementations | Filter behavior is inconsistent between commands |

### Low Priority (style / documentation)

| ID | Finding | Impact |
|----|---------|--------|
| DOC-CS-1 | Zero XML doc comments | Hard to onboard contributors |
| DOC-CPP-1..2 | `functionId` vs `methodId` naming | Terminology inconsistency with CLAUDE.md |
| P-CS-6 | `Truncate` name misleading | Minor readability issue |
| DOC-CPP-3 | `ProfilerContext` missing `m_` prefix | Style inconsistency |
| P-TEST-4 | `LeaveEvent : EnterEvent` inheritance | Semantically wrong but functionally fine |
