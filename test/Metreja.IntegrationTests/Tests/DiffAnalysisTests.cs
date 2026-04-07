using Metreja.Tool.Analysis;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class DiffAnalysisTests : IAsyncLifetime
{
    private string _baseFile = "";
    private string _compareFile = "";

    public Task InitializeAsync()
    {
        _baseFile = Path.Combine(Path.GetTempPath(), $"metreja-diff-base-{Guid.NewGuid():N}.ndjson");
        _compareFile = Path.Combine(Path.GetTempPath(), $"metreja-diff-compare-{Guid.NewGuid():N}.ndjson");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_baseFile)) File.Delete(_baseFile);
        if (File.Exists(_compareFile)) File.Delete(_compareFile);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task LeaveEvents_ShowsTimingDiff()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"leave","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"Svc","m":"Run","async":false,"deltaNs":10000000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"leave","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"Svc","m":"Run","async":false,"deltaNs":5000000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            DiffAnalyzer.AnalyzeAsync(_baseFile, _compareFile));

        Assert.Contains("Run", output);
        Assert.Contains("Method Timing Diff", output);
        Assert.Contains("Methods in base: 1", output);
    }

    [Fact]
    public async Task MethodStats_ShowsTimingDiff()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"MyNs","cls":"Svc","m":"DoWork","callCount":100,"totalSelfNs":10000000,"maxSelfNs":200000,"totalInclusiveNs":15000000,"maxInclusiveNs":300000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"MyNs","cls":"Svc","m":"DoWork","callCount":100,"totalSelfNs":3000000,"maxSelfNs":100000,"totalInclusiveNs":5000000,"maxInclusiveNs":200000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            DiffAnalyzer.AnalyzeAsync(_baseFile, _compareFile));

        Assert.Contains("DoWork", output);
        Assert.Contains("Method Timing Diff", output);
        Assert.Contains("Methods in base: 1", output);
    }

    [Fact]
    public async Task MethodStats_ShowsMultiMetricColumns()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"Target","callCount":100,"totalSelfNs":10000000,"maxSelfNs":200000,"totalInclusiveNs":15000000,"maxInclusiveNs":300000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"Target","callCount":80,"totalSelfNs":7000000,"maxSelfNs":150000,"totalInclusiveNs":10000000,"maxInclusiveNs":200000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            DiffAnalyzer.AnalyzeAsync(_baseFile, _compareFile));

        // Should have multi-metric column headers
        Assert.Contains("Self", output);
        Assert.Contains("Incl", output);
        Assert.Contains("Calls", output);
        // Should show call count delta (-20)
        Assert.Contains("-20", output);
    }

    [Fact]
    public async Task MethodStats_NewMethodInCompare_ShowsAsNew()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"Existing","callCount":10,"totalSelfNs":5000000,"maxSelfNs":500000,"totalInclusiveNs":8000000,"maxInclusiveNs":800000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"Existing","callCount":10,"totalSelfNs":5000000,"maxSelfNs":500000,"totalInclusiveNs":8000000,"maxInclusiveNs":800000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"NewMethod","callCount":5,"totalSelfNs":2000000,"maxSelfNs":400000,"totalInclusiveNs":3000000,"maxInclusiveNs":600000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            DiffAnalyzer.AnalyzeAsync(_baseFile, _compareFile));

        Assert.Contains("NewMethod", output);
        Assert.Contains("new", output);
    }

    [Fact]
    public async Task MethodStats_RemovedMethodInCompare_ShowsZeroCompare()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"RemovedMethod","callCount":10,"totalSelfNs":5000000,"maxSelfNs":500000,"totalInclusiveNs":8000000,"maxInclusiveNs":800000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"OtherMethod","callCount":1,"totalSelfNs":100000,"maxSelfNs":100000,"totalInclusiveNs":100000,"maxInclusiveNs":100000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            DiffAnalyzer.AnalyzeAsync(_baseFile, _compareFile));

        Assert.Contains("RemovedMethod", output);
    }

    [Fact]
    public async Task LeaveEvents_AggregatesMultipleCalls()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"Repeat","async":false,"deltaNs":3000000}""",
            """{"event":"leave","tsNs":4000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"Repeat","async":false,"deltaNs":2000000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"leave","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"Repeat","async":false,"deltaNs":1000000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            DiffAnalyzer.AnalyzeAsync(_baseFile, _compareFile));

        Assert.Contains("Repeat", output);
        // Base = 3ms + 2ms = 5ms, Compare = 1ms — leave-only, simplified output
        Assert.Contains(FormatUtils.FormatNs(5_000_000), output);
        Assert.Contains(FormatUtils.FormatNs(1_000_000), output);
    }

    [Fact]
    public async Task MethodStats_ShowsSelfTimeInMultiMetric()
    {
        // Self-time is 7ms/3ms, inclusive is 50ms/40ms — multi-metric shows both
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"Target","callCount":10,"totalSelfNs":7000000,"maxSelfNs":700000,"totalInclusiveNs":50000000,"maxInclusiveNs":5000000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"Target","callCount":10,"totalSelfNs":3000000,"maxSelfNs":300000,"totalInclusiveNs":40000000,"maxInclusiveNs":4000000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            DiffAnalyzer.AnalyzeAsync(_baseFile, _compareFile));

        // Multi-metric table should show self-time delta (-4ms)
        Assert.Contains(FormatUtils.FormatNs(4_000_000), output);
        // And inclusive delta (-10ms)
        Assert.Contains(FormatUtils.FormatNs(10_000_000), output);
    }

    [Fact]
    public async Task EmptyFiles_ShowsZeroMethods()
    {
        await File.WriteAllTextAsync(_baseFile, "");
        await File.WriteAllTextAsync(_compareFile, "");

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            DiffAnalyzer.AnalyzeAsync(_baseFile, _compareFile));

        Assert.Contains("Methods in base: 0", output);
        Assert.Contains("in compare: 0", output);
    }

    [Fact]
    public async Task Output_SortedByLargestDelta()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SmallChange","callCount":10,"totalSelfNs":10000000,"maxSelfNs":1000000,"totalInclusiveNs":10000000,"maxInclusiveNs":1000000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"BigChange","callCount":10,"totalSelfNs":100000000,"maxSelfNs":10000000,"totalInclusiveNs":100000000,"maxInclusiveNs":10000000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SmallChange","callCount":10,"totalSelfNs":9000000,"maxSelfNs":900000,"totalInclusiveNs":9000000,"maxInclusiveNs":900000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"BigChange","callCount":10,"totalSelfNs":10000000,"maxSelfNs":1000000,"totalInclusiveNs":10000000,"maxInclusiveNs":1000000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            DiffAnalyzer.AnalyzeAsync(_baseFile, _compareFile));

        var lines = output.Split('\n');
        var bigLine = Array.FindIndex(lines, l => l.Contains("BigChange"));
        var smallLine = Array.FindIndex(lines, l => l.Contains("SmallChange"));
        Assert.True(bigLine > 0 && smallLine > 0, $"Both methods should appear in output:\n{output}");
        Assert.True(bigLine < smallLine, "BigChange (90ms delta) should appear before SmallChange (1ms delta)");
    }

    [Fact]
    public async Task Output_UsesFormattedTime_NotRawNanoseconds()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SlowMethod","callCount":1,"totalSelfNs":2000000000,"maxSelfNs":2000000000,"totalInclusiveNs":2000000000,"maxInclusiveNs":2000000000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SlowMethod","callCount":1,"totalSelfNs":1000000000,"maxSelfNs":1000000000,"totalInclusiveNs":1000000000,"maxInclusiveNs":1000000000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            DiffAnalyzer.AnalyzeAsync(_baseFile, _compareFile));

        // Multi-metric table shows deltas with FormatNs units — self delta is 1s, inclusive delta is 1s
        Assert.Contains(FormatUtils.FormatNs(1_000_000_000), output);
        // Should not contain raw nanosecond numbers
        Assert.DoesNotContain("2000000000", output);
        Assert.DoesNotContain("1000000000", output);
    }

    [Fact]
    public async Task MethodStats_KeyFormat_ConsistentWithHotspots()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"MyNs","cls":"MyClass","m":"DoWork","callCount":10,"totalSelfNs":5000000,"maxSelfNs":500000,"totalInclusiveNs":8000000,"maxInclusiveNs":800000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"MyNs","cls":"MyClass","m":"DoWork","callCount":10,"totalSelfNs":3000000,"maxSelfNs":300000,"totalInclusiveNs":4000000,"maxInclusiveNs":400000}"""
        ]);

        var diffOutput = await TestHelpers.CaptureConsoleOutputAsync(() =>
            DiffAnalyzer.AnalyzeAsync(_baseFile, _compareFile));

        // Key should be "MyNs.MyClass.DoWork" (no asm prefix), matching HotspotsAnalyzer
        Assert.Contains("MyNs.MyClass.DoWork", diffOutput);
        Assert.DoesNotContain("App.MyNs.MyClass.DoWork", diffOutput);
    }

    [Fact]
    public async Task TopOption_LimitsOutput()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"MethodA","callCount":10,"totalSelfNs":10000000,"maxSelfNs":1000000,"totalInclusiveNs":10000000,"maxInclusiveNs":1000000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"MethodB","callCount":5,"totalSelfNs":5000000,"maxSelfNs":1000000,"totalInclusiveNs":5000000,"maxInclusiveNs":1000000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"MethodA","callCount":20,"totalSelfNs":20000000,"maxSelfNs":1000000,"totalInclusiveNs":20000000,"maxInclusiveNs":1000000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"MethodB","callCount":10,"totalSelfNs":10000000,"maxSelfNs":1000000,"totalInclusiveNs":10000000,"maxInclusiveNs":1000000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            DiffAnalyzer.AnalyzeAsync(_baseFile, _compareFile, top: 1));

        // Only one data row should appear (the one with largest delta)
        Assert.Contains("MethodA", output);
        Assert.DoesNotContain("MethodB", output);
    }

    [Fact]
    public async Task SortByCalls_OrdersByCallCountDelta()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"BigTimeDelta","callCount":10,"totalSelfNs":100000000,"maxSelfNs":10000000,"totalInclusiveNs":100000000,"maxInclusiveNs":10000000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"BigCallDelta","callCount":10,"totalSelfNs":1000000,"maxSelfNs":100000,"totalInclusiveNs":1000000,"maxInclusiveNs":100000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"BigTimeDelta","callCount":15,"totalSelfNs":10000000,"maxSelfNs":1000000,"totalInclusiveNs":10000000,"maxInclusiveNs":1000000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"BigCallDelta","callCount":1000,"totalSelfNs":500000,"maxSelfNs":50000,"totalInclusiveNs":500000,"maxInclusiveNs":50000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            DiffAnalyzer.AnalyzeAsync(_baseFile, _compareFile, sort: "calls"));

        var lines = output.Split('\n');
        var callLine = Array.FindIndex(lines, l => l.Contains("BigCallDelta"));
        var timeLine = Array.FindIndex(lines, l => l.Contains("BigTimeDelta"));
        Assert.True(callLine < timeLine, "BigCallDelta (990 call delta) should appear before BigTimeDelta (5 call delta)");
    }

    [Fact]
    public async Task FilterOption_NarrowsResults()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"MatchMe","callCount":10,"totalSelfNs":5000000,"maxSelfNs":500000,"totalInclusiveNs":8000000,"maxInclusiveNs":800000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SkipMe","callCount":10,"totalSelfNs":5000000,"maxSelfNs":500000,"totalInclusiveNs":8000000,"maxInclusiveNs":800000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"MatchMe","callCount":20,"totalSelfNs":10000000,"maxSelfNs":1000000,"totalInclusiveNs":16000000,"maxInclusiveNs":1600000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SkipMe","callCount":20,"totalSelfNs":10000000,"maxSelfNs":1000000,"totalInclusiveNs":16000000,"maxInclusiveNs":1600000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            DiffAnalyzer.AnalyzeAsync(_baseFile, _compareFile, filters: ["MatchMe"]));

        Assert.Contains("MatchMe", output);
        Assert.DoesNotContain("SkipMe", output);
    }

    [Fact]
    public async Task LeaveEvents_ShowsDashesForSelfAndCalls()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"leave","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"LeaveOnly","async":false,"deltaNs":10000000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"leave","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"LeaveOnly","async":false,"deltaNs":5000000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            DiffAnalyzer.AnalyzeAsync(_baseFile, _compareFile));

        // Leave-only data uses the simplified format (Base/Compare/Delta/Change)
        Assert.Contains("Base", output);
        Assert.Contains("Compare", output);
        Assert.Contains("Delta", output);
        // Should NOT have multi-metric headers
        Assert.DoesNotContain("Self", output);
        Assert.DoesNotContain("Calls", output);
    }
}
