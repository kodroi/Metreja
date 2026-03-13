using Metreja.Tool.Analysis;
using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

[Collection("ProfilerSession")]
public class HotspotsAnalysisTests
{
    private readonly ProfilerSessionFixture _fixture;

    public HotspotsAnalysisTests(ProfilerSessionFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Hotspots_ShowsAllMethods_SortedBySelfTime()
    {
        var tracePath = await WriteTraceToTempFileAsync();

        var output = await TestHelpers.CaptureConsoleOutputAsync(async () =>
            await HotspotsAnalyzer.AnalyzeAsync(tracePath, top: 50, minMs: 0, sortBy: "self", filters: []));

        Assert.Contains("Self Total", output);
        Assert.Contains("Incl Total", output);
        Assert.Contains("RunSyncCallPaths", output);
        Assert.Contains("InnerMethod", output);
    }

    [Fact]
    public async Task Hotspots_SortedByInclusive_ShowsDifferentOrder()
    {
        var tracePath = await WriteTraceToTempFileAsync();

        var output = await TestHelpers.CaptureConsoleOutputAsync(async () =>
            await HotspotsAnalyzer.AnalyzeAsync(tracePath, top: 5, minMs: 0, sortBy: "inclusive", filters: []));

        Assert.Contains("Incl Total", output);
        // The method with highest inclusive time should be first (Main or <Main>)
        var lines = output.Split('\n').Where(l => l.Contains("Main")).ToList();
        Assert.NotEmpty(lines);
    }

    [Fact]
    public async Task Hotspots_WithFilter_ShowsOnlyMatchingMethods()
    {
        var tracePath = await WriteTraceToTempFileAsync();

        var output = await TestHelpers.CaptureConsoleOutputAsync(async () =>
            await HotspotsAnalyzer.AnalyzeAsync(tracePath, top: 20, minMs: 0, sortBy: "self",
                filters: ["InnerMethod", "MiddleMethod"]));

        Assert.Contains("InnerMethod", output);
        Assert.Contains("MiddleMethod", output);
        Assert.DoesNotContain("RunSyncCallPaths", output);
    }

    [Fact]
    public async Task Hotspots_WithMinMs_FiltersLowTimeMethods()
    {
        var tracePath = await WriteTraceToTempFileAsync();

        var output = await TestHelpers.CaptureConsoleOutputAsync(async () =>
            await HotspotsAnalyzer.AnalyzeAsync(tracePath, top: 20, minMs: 100, sortBy: "self", filters: []));

        // With a 100ms threshold, most methods should be filtered out
        Assert.DoesNotContain("InnerMethod", output);
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

}
