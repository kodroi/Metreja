using Metreja.Tool.Analysis;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class HotspotsMethodStatsTests : IAsyncLifetime
{
    private string _tempFile = "";

    public Task InitializeAsync()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"metreja-test-{Guid.NewGuid():N}.ndjson");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task MethodStats_AggregatesCallCountAndTiming()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"MyNs","cls":"MyClass","m":"DoWork","callCount":100,"totalSelfNs":5000000,"maxSelfNs":200000,"totalInclusiveNs":8000000,"maxInclusiveNs":500000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"MyNs","cls":"MyClass","m":"Helper","callCount":50,"totalSelfNs":2000000,"maxSelfNs":100000,"totalInclusiveNs":3000000,"maxInclusiveNs":150000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            HotspotsAnalyzer.AnalyzeAsync(_tempFile, top: 10, minMs: 0, sortBy: "self", filters: []));

        Assert.Contains("DoWork", output);
        Assert.Contains("Helper", output);
        Assert.Contains("100", output);
        Assert.Contains("50", output);
    }

    [Fact]
    public async Task MethodStats_SortBySelf_HighestSelfTimeFirst()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"LowSelf","callCount":10,"totalSelfNs":1000000,"maxSelfNs":100000,"totalInclusiveNs":9000000,"maxInclusiveNs":900000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"HighSelf","callCount":10,"totalSelfNs":5000000,"maxSelfNs":500000,"totalInclusiveNs":6000000,"maxInclusiveNs":600000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            HotspotsAnalyzer.AnalyzeAsync(_tempFile, top: 10, minMs: 0, sortBy: "self", filters: []));

        var lines = output.Split('\n');
        var highSelfLine = Array.FindIndex(lines, l => l.Contains("HighSelf"));
        var lowSelfLine = Array.FindIndex(lines, l => l.Contains("LowSelf"));
        Assert.True(highSelfLine > 0 && lowSelfLine > 0, $"Both methods should appear in output:\n{output}");
        Assert.True(highSelfLine < lowSelfLine, "HighSelf should appear before LowSelf when sorted by self-time");
    }

    [Fact]
    public async Task MethodStats_SortByInclusive_HighestInclusiveFirst()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"HighIncl","callCount":10,"totalSelfNs":1000000,"maxSelfNs":100000,"totalInclusiveNs":9000000,"maxInclusiveNs":900000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"LowIncl","callCount":10,"totalSelfNs":5000000,"maxSelfNs":500000,"totalInclusiveNs":2000000,"maxInclusiveNs":200000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            HotspotsAnalyzer.AnalyzeAsync(_tempFile, top: 10, minMs: 0, sortBy: "inclusive", filters: []));

        var lines = output.Split('\n');
        var highLine = Array.FindIndex(lines, l => l.Contains("HighIncl"));
        var lowLine = Array.FindIndex(lines, l => l.Contains("LowIncl"));
        Assert.True(highLine > 0 && lowLine > 0, $"Both methods should appear in output:\n{output}");
        Assert.True(highLine < lowLine, "HighIncl should appear before LowIncl when sorted by inclusive time");
    }

    [Fact]
    public async Task MethodStats_SortByCalls_HighestCallCountFirst()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"FewCalls","callCount":5,"totalSelfNs":1000000,"maxSelfNs":100000,"totalInclusiveNs":2000000,"maxInclusiveNs":200000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"ManyCalls","callCount":500,"totalSelfNs":1000000,"maxSelfNs":100000,"totalInclusiveNs":2000000,"maxInclusiveNs":200000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            HotspotsAnalyzer.AnalyzeAsync(_tempFile, top: 10, minMs: 0, sortBy: "calls", filters: []));

        var lines = output.Split('\n');
        var manyLine = Array.FindIndex(lines, l => l.Contains("ManyCalls"));
        var fewLine = Array.FindIndex(lines, l => l.Contains("FewCalls"));
        Assert.True(manyLine > 0 && fewLine > 0, $"Both methods should appear in output:\n{output}");
        Assert.True(manyLine < fewLine, "ManyCalls should appear before FewCalls when sorted by calls");
    }

    [Fact]
    public async Task MethodStats_WithFilter_ShowsOnlyMatching()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"MyNs","cls":"ServiceA","m":"DoWork","callCount":10,"totalSelfNs":5000000,"maxSelfNs":500000,"totalInclusiveNs":8000000,"maxInclusiveNs":800000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"MyNs","cls":"ServiceB","m":"Process","callCount":20,"totalSelfNs":3000000,"maxSelfNs":300000,"totalInclusiveNs":4000000,"maxInclusiveNs":400000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            HotspotsAnalyzer.AnalyzeAsync(_tempFile, top: 10, minMs: 0, sortBy: "self", filters: ["ServiceA"]));

        Assert.Contains("DoWork", output);
        Assert.DoesNotContain("Process", output);
    }

    [Fact]
    public async Task MethodStats_WithMinMs_FiltersLowTimeMethods()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"FastMethod","callCount":10,"totalSelfNs":100000,"maxSelfNs":10000,"totalInclusiveNs":200000,"maxInclusiveNs":20000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SlowMethod","callCount":10,"totalSelfNs":50000000,"maxSelfNs":5000000,"totalInclusiveNs":80000000,"maxInclusiveNs":8000000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            HotspotsAnalyzer.AnalyzeAsync(_tempFile, top: 10, minMs: 1, sortBy: "self", filters: []));

        Assert.Contains("SlowMethod", output);
        Assert.DoesNotContain("FastMethod", output);
    }

    [Fact]
    public async Task MethodStats_MixedWithEnterLeave_AggregatesBoth()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"MethodA","async":false}""",
            """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"MethodA","async":false,"deltaNs":1000000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"MethodB","callCount":50,"totalSelfNs":5000000,"maxSelfNs":500000,"totalInclusiveNs":8000000,"maxInclusiveNs":800000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            HotspotsAnalyzer.AnalyzeAsync(_tempFile, top: 10, minMs: 0, sortBy: "self", filters: []));

        Assert.Contains("MethodA", output);
        Assert.Contains("MethodB", output);
    }

    [Fact]
    public async Task MethodStats_ReportsCorrectMethodCount()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"M1","callCount":1,"totalSelfNs":1000000,"maxSelfNs":1000000,"totalInclusiveNs":1000000,"maxInclusiveNs":1000000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"M2","callCount":1,"totalSelfNs":2000000,"maxSelfNs":2000000,"totalInclusiveNs":2000000,"maxInclusiveNs":2000000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"M3","callCount":1,"totalSelfNs":3000000,"maxSelfNs":3000000,"totalInclusiveNs":3000000,"maxInclusiveNs":3000000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            HotspotsAnalyzer.AnalyzeAsync(_tempFile, top: 10, minMs: 0, sortBy: "self", filters: []));

        Assert.Contains("3 methods", output);
    }
}
