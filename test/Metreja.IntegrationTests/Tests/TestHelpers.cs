using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

internal static class TestHelpers
{
    private static readonly SemaphoreSlim ConsoleGate = new(1, 1);

    public static async Task<string> CaptureConsoleOutputAsync(Func<Task> action)
    {
        await ConsoleGate.WaitAsync();
        var originalOut = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            await action();
            return sw.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
            ConsoleGate.Release();
        }
    }

    public static async Task<string> CaptureConsoleErrorAsync(Func<Task> action)
    {
        await ConsoleGate.WaitAsync();
        var originalErr = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            await action();
            return sw.ToString();
        }
        finally
        {
            Console.SetError(originalErr);
            ConsoleGate.Release();
        }
    }

    public static string GetSolutionRoot()
    {
        var root = ProfilerPrerequisites.FindSolutionRoot();
        var skipReason = ProfilerPrerequisites.GetSkipReason(root);
        if (skipReason is not null)
            throw new InvalidOperationException(skipReason);
        return root;
    }

    public static async Task<string> WriteTraceToTempFileAsync(IEnumerable<TraceEvent> events)
    {
        var tracePath = Path.Combine(Path.GetTempPath(), $"metreja-test-{Guid.NewGuid():N}.ndjson");
        var lines = events.Select(EventToNdjson);
        await File.WriteAllLinesAsync(tracePath, lines);
        return tracePath;
    }

    public static string EventToNdjson(TraceEvent e)
    {
        return e switch
        {
            LeaveEvent l =>
                $"{{\"event\":\"leave\",\"tsNs\":{l.TsNs},\"pid\":{l.Pid},\"sessionId\":\"{l.SessionId}\",\"tid\":{l.Tid},\"depth\":{l.Depth},\"asm\":\"{l.Asm}\",\"ns\":\"{l.Ns}\",\"cls\":\"{l.Cls}\",\"m\":\"{l.M}\",\"async\":{(l.Async ? "true" : "false")},\"deltaNs\":{l.DeltaNs}}}",
            EnterEvent en =>
                $"{{\"event\":\"enter\",\"tsNs\":{en.TsNs},\"pid\":{en.Pid},\"sessionId\":\"{en.SessionId}\",\"tid\":{en.Tid},\"depth\":{en.Depth},\"asm\":\"{en.Asm}\",\"ns\":\"{en.Ns}\",\"cls\":\"{en.Cls}\",\"m\":\"{en.M}\",\"async\":{(en.Async ? "true" : "false")}}}",
            ExceptionEvent ex =>
                $"{{\"event\":\"exception\",\"tsNs\":{ex.TsNs},\"pid\":{ex.Pid},\"sessionId\":\"{ex.SessionId}\",\"tid\":{ex.Tid},\"asm\":\"{ex.Asm}\",\"ns\":\"{ex.Ns}\",\"cls\":\"{ex.Cls}\",\"m\":\"{ex.M}\",\"exType\":\"{ex.ExType}\"}}",
            MethodStatsEvent ms =>
                $"{{\"event\":\"method_stats\",\"tsNs\":{ms.TsNs},\"pid\":{ms.Pid},\"sessionId\":\"{ms.SessionId}\",\"asm\":\"{ms.Asm}\",\"ns\":\"{ms.Ns}\",\"cls\":\"{ms.Cls}\",\"m\":\"{ms.M}\",\"callCount\":{ms.CallCount},\"totalSelfNs\":{ms.TotalSelfNs},\"maxSelfNs\":{ms.MaxSelfNs},\"totalInclusiveNs\":{ms.TotalInclusiveNs},\"maxInclusiveNs\":{ms.MaxInclusiveNs}}}",
            GcHeapStatsEvent ghs =>
                $"{{\"event\":\"gc_heap_stats\",\"tsNs\":{ghs.TsNs},\"pid\":{ghs.Pid},\"sessionId\":\"{ghs.SessionId}\",\"gen0SizeBytes\":{ghs.Gen0SizeBytes},\"gen0PromotedBytes\":{ghs.Gen0PromotedBytes},\"gen1SizeBytes\":{ghs.Gen1SizeBytes},\"gen1PromotedBytes\":{ghs.Gen1PromotedBytes},\"gen2SizeBytes\":{ghs.Gen2SizeBytes},\"gen2PromotedBytes\":{ghs.Gen2PromotedBytes},\"lohSizeBytes\":{ghs.LohSizeBytes},\"lohPromotedBytes\":{ghs.LohPromotedBytes},\"pohSizeBytes\":{ghs.PohSizeBytes},\"pohPromotedBytes\":{ghs.PohPromotedBytes},\"finalizationQueueLength\":{ghs.FinalizationQueueLength},\"pinnedObjectCount\":{ghs.PinnedObjectCount}}}",
            GcEvent gc =>
                $"{{\"event\":\"{gc.Event}\",\"tsNs\":{gc.TsNs},\"pid\":{gc.Pid},\"sessionId\":\"{gc.SessionId}\"{(gc.Gen0.HasValue ? $",\"gen0\":{(gc.Gen0.Value ? "true" : "false")}" : "")}{(gc.Gen1.HasValue ? $",\"gen1\":{(gc.Gen1.Value ? "true" : "false")}" : "")}{(gc.Gen2.HasValue ? $",\"gen2\":{(gc.Gen2.Value ? "true" : "false")}" : "")}{(gc.Reason is not null ? $",\"reason\":\"{gc.Reason}\"" : "")}{(gc.DurationNs.HasValue ? $",\"durationNs\":{gc.DurationNs.Value}" : "")}{(gc.HeapSizeBytes.HasValue ? $",\"heapSizeBytes\":{gc.HeapSizeBytes.Value}" : "")}}}",
            SessionMetadataEvent sm =>
                $"{{\"event\":\"session_metadata\",\"tsNs\":{sm.TsNs},\"pid\":{sm.Pid},\"sessionId\":\"{sm.SessionId}\",\"scenario\":\"{sm.Scenario}\"}}",
            ContentionEvent ce =>
                $"{{\"event\":\"{ce.Event}\",\"tsNs\":{ce.TsNs},\"pid\":{ce.Pid},\"sessionId\":\"{ce.SessionId}\",\"tid\":{ce.Tid}}}",
            AllocByClassEvent ac =>
                $"{{\"event\":\"alloc_by_class\",\"tsNs\":{ac.TsNs},\"pid\":{ac.Pid},\"sessionId\":\"{ac.SessionId}\",\"className\":\"{ac.ClassName}\",\"count\":{ac.Count}}}",
            ExceptionStatsEvent es =>
                $"{{\"event\":\"exception_stats\",\"tsNs\":{es.TsNs},\"pid\":{es.Pid},\"sessionId\":\"{es.SessionId}\",\"exType\":\"{es.ExType}\",\"asm\":\"{es.Asm}\",\"ns\":\"{es.Ns}\",\"cls\":\"{es.Cls}\",\"m\":\"{es.M}\",\"count\":{es.Count}}}",
            _ => throw new NotSupportedException($"EventToNdjson does not support {e.GetType().Name}")
        };
    }
}
