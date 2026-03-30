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
        var tracePath = await TestHelpers.WriteTraceToTempFileAsync(_fixture.Events);

        var output = await TestHelpers.CaptureConsoleOutputAsync(async () =>
            await CallersAnalyzer.AnalyzeAsync(tracePath, "InnerMethod", top: 20));

        Assert.Contains("Callers of InnerMethod", output);
        Assert.Contains("MiddleMethod", output);
    }

    [Fact]
    public async Task Callers_ShowsTimingStats()
    {
        var tracePath = await TestHelpers.WriteTraceToTempFileAsync(_fixture.Events);

        var output = await TestHelpers.CaptureConsoleOutputAsync(async () =>
            await CallersAnalyzer.AnalyzeAsync(tracePath, "InnerMethod", top: 20));

        Assert.Contains("Total", output);
        Assert.Contains("Avg", output);
        Assert.Contains("Max", output);
    }

    [Fact]
    public async Task Callers_NonExistentMethod_ShowsError()
    {
        var tracePath = await TestHelpers.WriteTraceToTempFileAsync(_fixture.Events);

        var errorOutput = await TestHelpers.CaptureConsoleErrorAsync(async () =>
            await CallersAnalyzer.AnalyzeAsync(tracePath, "NonExistentMethod999", top: 20));

        Assert.Contains("No calls found", errorOutput);
    }

    [Fact]
    public async Task Callers_ShowsTotalCallCount()
    {
        var tracePath = await TestHelpers.WriteTraceToTempFileAsync(_fixture.Events);

        var output = await TestHelpers.CaptureConsoleOutputAsync(async () =>
            await CallersAnalyzer.AnalyzeAsync(tracePath, "InnerMethod", top: 20));

        Assert.Contains("total calls", output);
    }

}
