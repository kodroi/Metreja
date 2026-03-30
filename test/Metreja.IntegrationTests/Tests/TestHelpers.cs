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
        var lines = events.Select(EventToNdjson).Where(l => l is not null);
        await File.WriteAllLinesAsync(tracePath, lines!);
        return tracePath;
    }

    public static string? EventToNdjson(TraceEvent e)
    {
        return e switch
        {
            LeaveEvent l =>
                $"{{\"event\":\"leave\",\"tsNs\":{l.TsNs},\"pid\":{l.Pid},\"sessionId\":\"{l.SessionId}\",\"tid\":{l.Tid},\"depth\":{l.Depth},\"asm\":\"{l.Asm}\",\"ns\":\"{l.Ns}\",\"cls\":\"{l.Cls}\",\"m\":\"{l.M}\",\"async\":{(l.Async ? "true" : "false")},\"deltaNs\":{l.DeltaNs}}}",
            EnterEvent en =>
                $"{{\"event\":\"enter\",\"tsNs\":{en.TsNs},\"pid\":{en.Pid},\"sessionId\":\"{en.SessionId}\",\"tid\":{en.Tid},\"depth\":{en.Depth},\"asm\":\"{en.Asm}\",\"ns\":\"{en.Ns}\",\"cls\":\"{en.Cls}\",\"m\":\"{en.M}\",\"async\":{(en.Async ? "true" : "false")}}}",
            ExceptionEvent ex =>
                $"{{\"event\":\"exception\",\"tsNs\":{ex.TsNs},\"pid\":{ex.Pid},\"sessionId\":\"{ex.SessionId}\",\"tid\":{ex.Tid},\"asm\":\"{ex.Asm}\",\"ns\":\"{ex.Ns}\",\"cls\":\"{ex.Cls}\",\"m\":\"{ex.M}\",\"exType\":\"{ex.ExType}\"}}",
            _ => null
        };
    }
}
