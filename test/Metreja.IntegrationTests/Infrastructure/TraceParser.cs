using System.Text.Json;

namespace Metreja.IntegrationTests.Infrastructure;

public static class TraceParser
{
    public static async Task<List<TraceEvent>> ParseAsync(string ndjsonPath)
    {
        var events = new List<TraceEvent>();
        var lines = await ReadAllLinesSharedAsync(ndjsonPath);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var eventType = root.GetProperty("event").GetString()!;

            TraceEvent parsed = eventType switch
            {
                "session_metadata" => new SessionMetadataEvent
                {
                    Event = eventType,
                    TsNs = root.GetProperty("tsNs").GetInt64(),
                    Pid = root.GetProperty("pid").GetInt32(),
                    SessionId = root.GetProperty("sessionId").GetString()!,
                    Scenario = root.TryGetProperty("scenario", out var s) ? s.GetString() ?? "" : ""
                },
                "enter" => new EnterEvent
                {
                    Event = eventType,
                    TsNs = root.GetProperty("tsNs").GetInt64(),
                    Pid = root.GetProperty("pid").GetInt32(),
                    SessionId = root.GetProperty("sessionId").GetString()!,
                    Tid = root.GetProperty("tid").GetInt32(),
                    Depth = root.GetProperty("depth").GetInt32(),
                    Asm = root.GetProperty("asm").GetString()!,
                    Ns = root.GetProperty("ns").GetString()!,
                    Cls = root.GetProperty("cls").GetString()!,
                    M = root.GetProperty("m").GetString()!,
                    Async = root.TryGetProperty("async", out var a) && a.GetBoolean()
                },
                "leave" => new LeaveEvent
                {
                    Event = eventType,
                    TsNs = root.GetProperty("tsNs").GetInt64(),
                    Pid = root.GetProperty("pid").GetInt32(),
                    SessionId = root.GetProperty("sessionId").GetString()!,
                    Tid = root.GetProperty("tid").GetInt32(),
                    Depth = root.GetProperty("depth").GetInt32(),
                    Asm = root.GetProperty("asm").GetString()!,
                    Ns = root.GetProperty("ns").GetString()!,
                    Cls = root.GetProperty("cls").GetString()!,
                    M = root.GetProperty("m").GetString()!,
                    Async = root.TryGetProperty("async", out var al) && al.GetBoolean(),
                    DeltaNs = root.GetProperty("deltaNs").GetInt64(),
                    Tailcall = root.TryGetProperty("tailcall", out var tc) && tc.GetBoolean(),
                    WallTimeNs = root.TryGetProperty("wallTimeNs", out var wt) ? wt.GetInt64() : null
                },
                "exception" => new ExceptionEvent
                {
                    Event = eventType,
                    TsNs = root.GetProperty("tsNs").GetInt64(),
                    Pid = root.GetProperty("pid").GetInt32(),
                    SessionId = root.GetProperty("sessionId").GetString()!,
                    Tid = root.GetProperty("tid").GetInt32(),
                    Asm = root.GetProperty("asm").GetString()!,
                    Ns = root.GetProperty("ns").GetString()!,
                    Cls = root.GetProperty("cls").GetString()!,
                    M = root.GetProperty("m").GetString()!,
                    ExType = root.GetProperty("exType").GetString()!
                },
                "method_stats" => new MethodStatsEvent
                {
                    Event = eventType,
                    TsNs = root.GetProperty("tsNs").GetInt64(),
                    Pid = root.GetProperty("pid").GetInt32(),
                    SessionId = root.GetProperty("sessionId").GetString()!,
                    Asm = root.GetProperty("asm").GetString()!,
                    Ns = root.GetProperty("ns").GetString()!,
                    Cls = root.GetProperty("cls").GetString()!,
                    M = root.GetProperty("m").GetString()!,
                    CallCount = root.GetProperty("callCount").GetInt64(),
                    TotalSelfNs = root.GetProperty("totalSelfNs").GetInt64(),
                    MaxSelfNs = root.GetProperty("maxSelfNs").GetInt64(),
                    TotalInclusiveNs = root.GetProperty("totalInclusiveNs").GetInt64(),
                    MaxInclusiveNs = root.GetProperty("maxInclusiveNs").GetInt64()
                },
                "exception_stats" => new ExceptionStatsEvent
                {
                    Event = eventType,
                    TsNs = root.GetProperty("tsNs").GetInt64(),
                    Pid = root.GetProperty("pid").GetInt32(),
                    SessionId = root.GetProperty("sessionId").GetString()!,
                    ExType = root.GetProperty("exType").GetString()!,
                    Asm = root.GetProperty("asm").GetString()!,
                    Ns = root.GetProperty("ns").GetString()!,
                    Cls = root.GetProperty("cls").GetString()!,
                    M = root.GetProperty("m").GetString()!,
                    Count = root.GetProperty("count").GetInt64()
                },
                "gc_start" or "gc_end" => new GcEvent
                {
                    Event = eventType,
                    TsNs = root.GetProperty("tsNs").GetInt64(),
                    Pid = root.GetProperty("pid").GetInt32(),
                    SessionId = root.GetProperty("sessionId").GetString()!,
                    Gen0 = root.TryGetProperty("gen0", out var gcg0) ? gcg0.GetBoolean() : null,
                    Gen1 = root.TryGetProperty("gen1", out var gcg1) ? gcg1.GetBoolean() : null,
                    Gen2 = root.TryGetProperty("gen2", out var gcg2) ? gcg2.GetBoolean() : null,
                    Reason = root.TryGetProperty("reason", out var gcr) ? gcr.GetString() : null,
                    DurationNs = root.TryGetProperty("durationNs", out var gcd) ? gcd.GetInt64() : null,
                    HeapSizeBytes = root.TryGetProperty("heapSizeBytes", out var gchs) ? gchs.GetInt64() : null
                },
                "gc_heap_stats" => new GcHeapStatsEvent
                {
                    Event = eventType,
                    TsNs = root.GetProperty("tsNs").GetInt64(),
                    Pid = root.GetProperty("pid").GetInt32(),
                    SessionId = root.GetProperty("sessionId").GetString()!,
                    Gen0SizeBytes = SafeGetInt64(root, "gen0SizeBytes"),
                    Gen0PromotedBytes = SafeGetInt64(root, "gen0PromotedBytes"),
                    Gen1SizeBytes = SafeGetInt64(root, "gen1SizeBytes"),
                    Gen1PromotedBytes = SafeGetInt64(root, "gen1PromotedBytes"),
                    Gen2SizeBytes = SafeGetInt64(root, "gen2SizeBytes"),
                    Gen2PromotedBytes = SafeGetInt64(root, "gen2PromotedBytes"),
                    LohSizeBytes = SafeGetInt64(root, "lohSizeBytes"),
                    LohPromotedBytes = SafeGetInt64(root, "lohPromotedBytes"),
                    PohSizeBytes = SafeGetInt64(root, "pohSizeBytes"),
                    PohPromotedBytes = SafeGetInt64(root, "pohPromotedBytes"),
                    FinalizationQueueLength = SafeGetInt64(root, "finalizationQueueLength"),
                    PinnedObjectCount = root.TryGetProperty("pinnedObjectCount", out var poc) && poc.TryGetInt32(out var pocVal) ? pocVal : 0
                },
                "alloc_by_class" => new AllocByClassEvent
                {
                    Event = eventType,
                    TsNs = root.GetProperty("tsNs").GetInt64(),
                    Pid = root.GetProperty("pid").GetInt32(),
                    SessionId = root.GetProperty("sessionId").GetString()!,
                    Tid = root.TryGetProperty("tid", out var allocTid) ? allocTid.GetInt32() : null,
                    ClassName = root.GetProperty("className").GetString()!,
                    Count = SafeGetInt64(root, "count"),
                    AllocAsm = root.TryGetProperty("allocAsm", out var aasm) ? aasm.GetString() : null,
                    AllocM = root.TryGetProperty("allocM", out var am) ? am.GetString() : null,
                    AllocNs = root.TryGetProperty("allocNs", out var ans) ? ans.GetString() : null,
                    AllocCls = root.TryGetProperty("allocCls", out var acs) ? acs.GetString() : null
                },
                "contention_start" or "contention_end" => new ContentionEvent
                {
                    Event = eventType,
                    TsNs = root.GetProperty("tsNs").GetInt64(),
                    Pid = root.GetProperty("pid").GetInt32(),
                    SessionId = root.GetProperty("sessionId").GetString()!,
                    Tid = root.TryGetProperty("tid", out var contentionTid) ? contentionTid.GetInt32() : 0
                },
                _ => throw new InvalidOperationException($"Unknown event type: {eventType}")
            };

            events.Add(parsed);
        }

        return events;
    }

    // Safely parse Int64, handling unsigned values that exceed Int64.MaxValue (e.g., %lu on macOS 64-bit)
    private static long SafeGetInt64(JsonElement root, string propertyName)
    {
        var prop = root.GetProperty(propertyName);
        return prop.TryGetInt64(out var value) ? value : (long)prop.GetUInt64();
    }

    private static async Task<string[]> ReadAllLinesSharedAsync(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        var content = await reader.ReadToEndAsync();
        return content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
