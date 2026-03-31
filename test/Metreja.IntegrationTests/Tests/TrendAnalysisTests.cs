using Metreja.Tool.Analysis;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class TrendAnalysisTests : IAsyncLifetime
{
    private string _tempFile = "";

    public Task InitializeAsync()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"metreja-test-{Guid.NewGuid():N}.ndjson");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Trend_ShowsMultipleIntervals()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"method_stats","tsNs":10000000000,"pid":1,"sessionId":"s1","asm":"App","ns":"MyNs","cls":"MyClass","m":"DoWork","callCount":50,"totalSelfNs":5000000,"maxSelfNs":200000,"totalInclusiveNs":8000000,"maxInclusiveNs":500000}""",
            """{"event":"method_stats","tsNs":40000000000,"pid":1,"sessionId":"s1","asm":"App","ns":"MyNs","cls":"MyClass","m":"DoWork","callCount":100,"totalSelfNs":12300000,"maxSelfNs":300000,"totalInclusiveNs":18500000,"maxInclusiveNs":600000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            TrendAnalyzer.AnalyzeAsync(_tempFile, "DoWork"));

        Assert.Contains("Trend: MyNs.MyClass.DoWork", output);
        Assert.Contains("2 intervals found", output);
        var lines = output.Split('\n');
        Assert.Contains(lines, l => l.TrimStart().StartsWith('1'));
        Assert.Contains(lines, l => l.TrimStart().StartsWith('2'));
    }

    [Fact]
    public async Task Trend_NoMatch_ShowsError()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"method_stats","tsNs":10000000000,"pid":1,"sessionId":"s1","asm":"App","ns":"MyNs","cls":"MyClass","m":"DoWork","callCount":50,"totalSelfNs":5000000,"maxSelfNs":200000,"totalInclusiveNs":8000000,"maxInclusiveNs":500000}"""
        ]);

        var errorOutput = await TestHelpers.CaptureConsoleErrorAsync(() =>
            TrendAnalyzer.AnalyzeAsync(_tempFile, "NonExistentMethod"));

        Assert.Contains("No method_stats events found matching 'NonExistentMethod'", errorOutput);
    }

    [Fact]
    public async Task Trend_SingleInterval()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"method_stats","tsNs":5000000000,"pid":1,"sessionId":"s1","asm":"App","ns":"MyNs","cls":"Svc","m":"Process","callCount":10,"totalSelfNs":2000000,"maxSelfNs":300000,"totalInclusiveNs":4000000,"maxInclusiveNs":700000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            TrendAnalyzer.AnalyzeAsync(_tempFile, "Process"));

        Assert.Contains("1 intervals found", output);
        var lines = output.Split('\n');
        Assert.Contains(lines, l => l.TrimStart().StartsWith('1'));
        Assert.Contains("0ns", output);
    }

    [Fact]
    public async Task Trend_CorrectColumns()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"Target","callCount":200,"totalSelfNs":10000000,"maxSelfNs":100000,"totalInclusiveNs":20000000,"maxInclusiveNs":200000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            TrendAnalyzer.AnalyzeAsync(_tempFile, "Target"));

        Assert.Contains("200", output);
        Assert.Contains(FormatUtils.FormatNs(10_000_000), output);
        Assert.Contains(FormatUtils.FormatNs(20_000_000), output);
        Assert.Contains(FormatUtils.FormatNs(50_000), output);
        Assert.Contains(FormatUtils.FormatNs(100_000), output);
    }

    [Fact]
    public async Task Trend_IgnoresNonMatchingMethods()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"MyNs","cls":"MyClass","m":"DoWork","callCount":50,"totalSelfNs":5000000,"maxSelfNs":200000,"totalInclusiveNs":8000000,"maxInclusiveNs":500000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"MyNs","cls":"MyClass","m":"OtherMethod","callCount":999,"totalSelfNs":99000000,"maxSelfNs":9000000,"totalInclusiveNs":99000000,"maxInclusiveNs":9000000}""",
            """{"event":"method_stats","tsNs":30000000000,"pid":1,"sessionId":"s1","asm":"App","ns":"MyNs","cls":"MyClass","m":"DoWork","callCount":100,"totalSelfNs":12000000,"maxSelfNs":300000,"totalInclusiveNs":18000000,"maxInclusiveNs":600000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            TrendAnalyzer.AnalyzeAsync(_tempFile, "DoWork"));

        Assert.Contains("Trend: MyNs.MyClass.DoWork", output);
        Assert.Contains("2 intervals found", output);
        Assert.DoesNotContain("999", output);
        Assert.DoesNotContain("OtherMethod", output);
    }
}
