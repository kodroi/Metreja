using Metreja.Cli.Analysis;
using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

[Collection("ProfilerSession")]
public class CallTreeAnalysisTests
{
    private readonly ProfilerSessionFixture _fixture;

    public CallTreeAnalysisTests(ProfilerSessionFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CallTree_ShowsSyncCallPath()
    {
        var tracePath = await WriteTraceToTempFileAsync();

        var output = await CaptureConsoleOutputAsync(async () =>
        {
            await CallTreeAnalyzer.AnalyzeAsync(tracePath, "RunSyncCallPaths", tidFilter: null, occurrence: 1);
        });

        Assert.Contains("RunSyncCallPaths", output);
        Assert.Contains("OuterMethod", output);
        Assert.Contains("MiddleMethod", output);
        Assert.Contains("InnerMethod", output);
        // Verify proper nesting — InnerMethod should be deeper than RunSyncCallPaths
        var lines = output.Split('\n');
        var runSyncLine = lines.First(l => l.Contains("RunSyncCallPaths"));
        var innerLine = lines.First(l => l.Contains("InnerMethod"));
        Assert.True(innerLine.Length - innerLine.TrimStart().Length >
                    runSyncLine.Length - runSyncLine.TrimStart().Length,
            "InnerMethod should be indented deeper than RunSyncCallPaths");
    }

    [Fact]
    public async Task CallTree_ShowsTimings()
    {
        var tracePath = await WriteTraceToTempFileAsync();

        var output = await CaptureConsoleOutputAsync(async () =>
        {
            await CallTreeAnalyzer.AnalyzeAsync(tracePath, "RunSyncCallPaths", tidFilter: null, occurrence: 1);
        });

        // Should contain timing info in parentheses
        Assert.Matches(@"\(\d+[\.,]\d+", output);
    }

    [Fact]
    public async Task CallTree_NonExistentMethod_ShowsError()
    {
        var tracePath = await WriteTraceToTempFileAsync();

        var errorOutput = await CaptureConsoleErrorAsync(async () =>
        {
            await CallTreeAnalyzer.AnalyzeAsync(tracePath, "NonExistentMethod123", tidFilter: null, occurrence: 1);
        });

        Assert.Contains("No invocations found", errorOutput);
    }

    [Fact]
    public async Task CallTree_MultipleOccurrences_ReportsCount()
    {
        var tracePath = await WriteTraceToTempFileAsync();

        var output = await CaptureConsoleOutputAsync(async () =>
        {
            await CallTreeAnalyzer.AnalyzeAsync(tracePath, "Main", tidFilter: null, occurrence: 1);
        });

        Assert.Contains("invocation(s)", output);
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
                $"{{\"event\":\"leave\",\"tsNs\":{l.TsNs},\"pid\":{l.Pid},\"runId\":\"{l.RunId}\",\"tid\":{l.Tid},\"depth\":{l.Depth},\"asm\":\"{l.Asm}\",\"ns\":\"{l.Ns}\",\"cls\":\"{l.Cls}\",\"m\":\"{l.M}\",\"async\":{(l.Async ? "true" : "false")},\"deltaNs\":{l.DeltaNs}}}",
            EnterEvent en =>
                $"{{\"event\":\"enter\",\"tsNs\":{en.TsNs},\"pid\":{en.Pid},\"runId\":\"{en.RunId}\",\"tid\":{en.Tid},\"depth\":{en.Depth},\"asm\":\"{en.Asm}\",\"ns\":\"{en.Ns}\",\"cls\":\"{en.Cls}\",\"m\":\"{en.M}\",\"async\":{(en.Async ? "true" : "false")}}}",
            ExceptionEvent ex =>
                $"{{\"event\":\"exception\",\"tsNs\":{ex.TsNs},\"pid\":{ex.Pid},\"runId\":\"{ex.RunId}\",\"tid\":{ex.Tid},\"asm\":\"{ex.Asm}\",\"ns\":\"{ex.Ns}\",\"cls\":\"{ex.Cls}\",\"m\":\"{ex.M}\",\"exType\":\"{ex.ExType}\"}}",
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
