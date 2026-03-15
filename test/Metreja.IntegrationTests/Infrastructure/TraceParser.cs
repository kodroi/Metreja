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
                    SessionId = root.GetProperty("sessionId").GetString()!
                },
                "alloc_by_class" => new AllocByClassEvent
                {
                    Event = eventType,
                    TsNs = root.GetProperty("tsNs").GetInt64(),
                    Pid = root.GetProperty("pid").GetInt32(),
                    SessionId = root.GetProperty("sessionId").GetString()!,
                    Tid = root.TryGetProperty("tid", out var allocTid) ? allocTid.GetInt32() : null,
                    ClassName = root.GetProperty("className").GetString()!,
                    Count = root.GetProperty("count").GetInt64(),
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

    private static async Task<string[]> ReadAllLinesSharedAsync(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        var content = await reader.ReadToEndAsync();
        return content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
