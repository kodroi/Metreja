using Metreja.Tool.Analysis;
using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

[Collection("ProfilerSession")]
public class CallersAnalysisTests
{
    private readonly ProfilerSessionFixture _fixture;

    public CallersAnalysisTests(ProfilerSessionFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Callers_ShowsCorrectCaller()
    {
        var tracePath = await WriteTraceToTempFileAsync();

        var output = await CaptureConsoleOutputAsync(async () =>
        {
            await CallersAnalyzer.AnalyzeAsync(tracePath, "InnerMethod", top: 20);
        });

        Assert.Contains("Callers of InnerMethod", output);
        Assert.Contains("MiddleMethod", output);
    }

    [Fact]
    public async Task Callers_ShowsTimingStats()
    {
        var tracePath = await WriteTraceToTempFileAsync();

        var output = await CaptureConsoleOutputAsync(async () =>
        {
            await CallersAnalyzer.AnalyzeAsync(tracePath, "InnerMethod", top: 20);
        });

        Assert.Contains("Total", output);
        Assert.Contains("Avg", output);
        Assert.Contains("Max", output);
    }

    [Fact]
    public async Task Callers_NonExistentMethod_ShowsError()
    {
        var tracePath = await WriteTraceToTempFileAsync();

        var errorOutput = await CaptureConsoleErrorAsync(async () =>
        {
            await CallersAnalyzer.AnalyzeAsync(tracePath, "NonExistentMethod999", top: 20);
        });

        Assert.Contains("No calls found", errorOutput);
    }

    [Fact]
    public async Task Callers_ShowsTotalCallCount()
    {
        var tracePath = await WriteTraceToTempFileAsync();

        var output = await CaptureConsoleOutputAsync(async () =>
        {
            await CallersAnalyzer.AnalyzeAsync(tracePath, "InnerMethod", top: 20);
        });

        Assert.Contains("total calls", output);
    }

    private async Task<string> WriteTraceToTempFileAsync()
    {
        var tracePath = Path.Combine(Path.GetTempPath(), $"metreja-test-{Guid.NewGuid():N}.ndjson");
        var lines = _fixture.Events.Select(EventToNdjson).Where(l => l is not null);
        await File.WriteAllLinesAsync(tracePath, lines!);
        return tracePath;
    }

    private static string? EventToNdjson(TraceEvent e)
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

    private static async Task<string> CaptureConsoleOutputAsync(Func<Task> action)
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            await action();
            return sw.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private static async Task<string> CaptureConsoleErrorAsync(Func<Task> action)
    {
        var originalErr = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            await action();
            return sw.ToString();
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }
}
