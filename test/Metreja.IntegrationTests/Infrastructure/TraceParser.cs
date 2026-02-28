using System.Text.Json;

namespace Metreja.IntegrationTests.Infrastructure;

public static class TraceParser
{
    public static async Task<List<TraceEvent>> ParseAsync(string ndjsonPath)
    {
        var events = new List<TraceEvent>();
        var lines = await File.ReadAllLinesAsync(ndjsonPath);

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
                    Async = root.GetProperty("async").GetBoolean()
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
                    Async = root.GetProperty("async").GetBoolean(),
                    DeltaNs = root.GetProperty("deltaNs").GetInt64()
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
                _ => throw new InvalidOperationException($"Unknown event type: {eventType}")
            };

            events.Add(parsed);
        }

        return events;
    }
}
