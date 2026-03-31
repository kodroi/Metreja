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
        Assert.Contains("0ns", output);
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
        // Base = 3ms + 2ms = 5ms, Compare = 1ms
        Assert.Contains(FormatUtils.FormatNs(5_000_000), output);
        Assert.Contains(FormatUtils.FormatNs(1_000_000), output);
    }

    [Fact]
    public async Task MethodStats_UsesInclusiveTimeForComparison()
    {
        // Self-time is 7ms/3ms, inclusive is 50ms/40ms — verify inclusive is used
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

        // Should use totalInclusiveNs (50ms/40ms), consistent with leave's deltaNs (also inclusive)
        Assert.Contains(FormatUtils.FormatNs(50_000_000), output);
        Assert.Contains(FormatUtils.FormatNs(40_000_000), output);
        // Should NOT show self-time values (7ms/3ms)
        Assert.DoesNotContain(FormatUtils.FormatNs(7_000_000), output);
        Assert.DoesNotContain(FormatUtils.FormatNs(3_000_000), output);
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
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"QuickMethod","callCount":1,"totalSelfNs":500,"maxSelfNs":500,"totalInclusiveNs":500,"maxInclusiveNs":500}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SlowMethod","callCount":1,"totalSelfNs":2000000000,"maxSelfNs":2000000000,"totalInclusiveNs":2000000000,"maxInclusiveNs":2000000000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"QuickMethod","callCount":1,"totalSelfNs":500,"maxSelfNs":500,"totalInclusiveNs":500,"maxInclusiveNs":500}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SlowMethod","callCount":1,"totalSelfNs":1000000000,"maxSelfNs":1000000000,"totalInclusiveNs":1000000000,"maxInclusiveNs":1000000000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            DiffAnalyzer.AnalyzeAsync(_baseFile, _compareFile));

        // Should use FormatNs units (ns, us, ms, s) — not raw nanosecond numbers
        Assert.Contains("500ns", output);
        Assert.Contains(FormatUtils.FormatNs(2_000_000_000), output);
        Assert.Contains(FormatUtils.FormatNs(1_000_000_000), output);
    }

    [Fact]
    public async Task MethodStats_KeyFormat_ConsistentWithHotspots()
    {
        // Both analyzers should produce the same key format for the same method
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
}
