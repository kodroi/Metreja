using Metreja.Tool.Analysis;
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
        var tracePath = await TestHelpers.WriteTraceToTempFileAsync(_fixture.Events);

        var output = await TestHelpers.CaptureConsoleOutputAsync(async () =>
            await CallTreeAnalyzer.AnalyzeAsync(tracePath, "RunSyncCallPaths", tidFilter: null, occurrence: 1));

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
        var tracePath = await TestHelpers.WriteTraceToTempFileAsync(_fixture.Events);

        var output = await TestHelpers.CaptureConsoleOutputAsync(async () =>
            await CallTreeAnalyzer.AnalyzeAsync(tracePath, "RunSyncCallPaths", tidFilter: null, occurrence: 1));

        // Should contain timing info in parentheses
        Assert.Matches(@"\(\d+[\.,]\d+", output);
    }

    [Fact]
    public async Task CallTree_NonExistentMethod_ShowsError()
    {
        var tracePath = await TestHelpers.WriteTraceToTempFileAsync(_fixture.Events);

        var errorOutput = await TestHelpers.CaptureConsoleErrorAsync(async () =>
            await CallTreeAnalyzer.AnalyzeAsync(tracePath, "NonExistentMethod123", tidFilter: null, occurrence: 1));

        Assert.Contains("No invocations found", errorOutput);
    }

    [Fact]
    public async Task CallTree_MultipleOccurrences_ReportsCount()
    {
        var tracePath = await TestHelpers.WriteTraceToTempFileAsync(_fixture.Events);

        var output = await TestHelpers.CaptureConsoleOutputAsync(async () =>
            await CallTreeAnalyzer.AnalyzeAsync(tracePath, "Main", tidFilter: null, occurrence: 1));

        Assert.Contains("invocation(s)", output);
    }
}
