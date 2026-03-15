using System.Text.Json;
using Metreja.Tool.Analysis;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class JsonFormatTests : IAsyncLifetime
{
    private string _traceFile = "";
    private string _baseFile = "";
    private string _compareFile = "";

    public Task InitializeAsync()
    {
        _traceFile = Path.Combine(Path.GetTempPath(), $"metreja-json-test-{Guid.NewGuid():N}.ndjson");
        _baseFile = Path.Combine(Path.GetTempPath(), $"metreja-json-base-{Guid.NewGuid():N}.ndjson");
        _compareFile = Path.Combine(Path.GetTempPath(), $"metreja-json-compare-{Guid.NewGuid():N}.ndjson");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_traceFile)) File.Delete(_traceFile);
        if (File.Exists(_baseFile)) File.Delete(_baseFile);
        if (File.Exists(_compareFile)) File.Delete(_compareFile);
        return Task.CompletedTask;
    }

    private static readonly string[] SampleTrace =
    [
        """{"event":"session_metadata","tsNs":0,"pid":1,"sessionId":"s1","scenario":"test"}""",
        """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"DoWork","async":false}""",
        """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"DoWork","async":false,"deltaNs":1000}""",
        """{"event":"method_stats","tsNs":3000,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"DoWork","callCount":10,"totalSelfNs":10000,"maxSelfNs":1000,"totalInclusiveNs":15000,"maxInclusiveNs":1500}""",
        """{"event":"exception","tsNs":4000,"pid":1,"sessionId":"s1","tid":1,"asm":"App","ns":"N","cls":"C","m":"DoWork","exType":"System.InvalidOperationException"}""",
        """{"event":"gc_start","tsNs":5000,"pid":1,"sessionId":"s1","gen0":true,"gen1":false,"gen2":false}""",
        """{"event":"gc_end","tsNs":6000,"pid":1,"sessionId":"s1","durationNs":1000}""",
        """{"event":"alloc_by_class","tsNs":7000,"pid":1,"sessionId":"s1","tid":1,"className":"System.String","count":50}"""
    ];

    private static readonly string[] MethodStatsTrace =
    [
        """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"DoWork","callCount":10,"totalSelfNs":10000000,"maxSelfNs":1000000,"totalInclusiveNs":10000000,"maxInclusiveNs":1000000}"""
    ];

    private static readonly string[] MethodStatsCompareTrace =
    [
        """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"DoWork","callCount":10,"totalSelfNs":50000000,"maxSelfNs":5000000,"totalInclusiveNs":50000000,"maxInclusiveNs":5000000}"""
    ];

    [Fact]
    public async Task Summary_JsonFormat()
    {
        await File.WriteAllLinesAsync(_traceFile, SampleTrace);

        var output = await TestHelpers.CaptureConsoleOutputAsync(
            async () => await SummaryAnalyzer.AnalyzeAsync(_traceFile, format: "json"));

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("sessionId", out _));
        Assert.True(root.TryGetProperty("scenario", out _));
        Assert.True(root.TryGetProperty("pid", out _));
        Assert.True(root.TryGetProperty("durationNs", out _));
        Assert.True(root.TryGetProperty("threadCount", out _));
        Assert.True(root.TryGetProperty("methodCount", out _));
        Assert.True(root.TryGetProperty("totalEvents", out _));
        Assert.True(root.TryGetProperty("eventBreakdown", out _));
        Assert.True(root.TryGetProperty("gcCollections", out _));
        Assert.True(root.TryGetProperty("exceptionCount", out _));
    }

    [Fact]
    public async Task Hotspots_JsonFormat()
    {
        await File.WriteAllLinesAsync(_traceFile, SampleTrace);

        var output = await TestHelpers.CaptureConsoleOutputAsync(
            async () => await HotspotsAnalyzer.AnalyzeAsync(
                _traceFile, top: 10, minMs: 0, sortBy: "self", filters: [], format: "json"));

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("methods", out _));
        Assert.True(root.TryGetProperty("totalMethods", out _));
        Assert.True(root.TryGetProperty("sortedBy", out _));
        Assert.True(root.TryGetProperty("minThresholdMs", out _));
    }

    [Fact]
    public async Task CallTree_JsonFormat()
    {
        await File.WriteAllLinesAsync(_traceFile, SampleTrace);

        var output = await TestHelpers.CaptureConsoleOutputAsync(
            async () => await CallTreeAnalyzer.AnalyzeAsync(
                _traceFile, methodPattern: "DoWork", tidFilter: null, occurrence: 1, format: "json"));

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("method", out _));
        Assert.True(root.TryGetProperty("occurrences", out _));
        Assert.True(root.TryGetProperty("showing", out _));
        Assert.True(root.TryGetProperty("tid", out _));
        Assert.True(root.TryGetProperty("totalNs", out _));
        Assert.True(root.TryGetProperty("tree", out _));
    }

    [Fact]
    public async Task Callers_JsonFormat()
    {
        await File.WriteAllLinesAsync(_traceFile, SampleTrace);

        var output = await TestHelpers.CaptureConsoleOutputAsync(
            async () => await CallersAnalyzer.AnalyzeAsync(
                _traceFile, methodPattern: "DoWork", top: 10, format: "json"));

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("method", out _));
        Assert.True(root.TryGetProperty("totalCalls", out _));
        Assert.True(root.TryGetProperty("callers", out _));
    }

    [Fact]
    public async Task Memory_JsonFormat()
    {
        await File.WriteAllLinesAsync(_traceFile, SampleTrace);

        var output = await TestHelpers.CaptureConsoleOutputAsync(
            async () => await MemoryAnalyzer.AnalyzeAsync(
                _traceFile, top: 10, filters: [], format: "json"));

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("gc", out var gc));
        Assert.True(gc.TryGetProperty("totalCount", out _));
        Assert.True(gc.TryGetProperty("gen0Count", out _));
        Assert.True(gc.TryGetProperty("gen1Count", out _));
        Assert.True(gc.TryGetProperty("gen2Count", out _));
        Assert.True(gc.TryGetProperty("totalPauseNs", out _));
        Assert.True(gc.TryGetProperty("maxPauseNs", out _));
        Assert.True(root.TryGetProperty("allocations", out _));
        Assert.True(root.TryGetProperty("totalTypes", out _));
    }

    [Fact]
    public async Task Exceptions_JsonFormat()
    {
        await File.WriteAllLinesAsync(_traceFile, SampleTrace);

        var output = await TestHelpers.CaptureConsoleOutputAsync(
            async () => await ExceptionsAnalyzer.AnalyzeAsync(
                _traceFile, top: 10, filters: [], format: "json"));

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("exceptions", out _));
        Assert.True(root.TryGetProperty("totalTypes", out _));
    }

    [Fact]
    public async Task Timeline_JsonFormat()
    {
        await File.WriteAllLinesAsync(_traceFile, SampleTrace);

        var output = await TestHelpers.CaptureConsoleOutputAsync(
            async () => await TimelineAnalyzer.AnalyzeAsync(
                _traceFile, tidFilter: null, eventTypeFilter: null, methodFilter: null, top: 100, format: "json"));

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("events", out _));
        Assert.True(root.TryGetProperty("totalShown", out _));
        Assert.True(root.TryGetProperty("limit", out _));
    }

    [Fact]
    public async Task Threads_JsonFormat()
    {
        await File.WriteAllLinesAsync(_traceFile, SampleTrace);

        var output = await TestHelpers.CaptureConsoleOutputAsync(
            async () => await ThreadsAnalyzer.AnalyzeAsync(
                _traceFile, sortBy: "calls", format: "json"));

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("threads", out _));
        Assert.True(root.TryGetProperty("totalThreads", out _));
    }

    [Fact]
    public async Task Trend_JsonFormat()
    {
        await File.WriteAllLinesAsync(_traceFile, SampleTrace);

        var output = await TestHelpers.CaptureConsoleOutputAsync(
            async () => await TrendAnalyzer.AnalyzeAsync(
                _traceFile, methodPattern: "DoWork", format: "json"));

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("method", out _));
        Assert.True(root.TryGetProperty("intervals", out _));
        Assert.True(root.TryGetProperty("totalIntervals", out _));
    }

    [Fact]
    public async Task Diff_JsonFormat()
    {
        await File.WriteAllLinesAsync(_baseFile, MethodStatsTrace);
        await File.WriteAllLinesAsync(_compareFile, MethodStatsCompareTrace);

        var output = await TestHelpers.CaptureConsoleOutputAsync(
            async () => await DiffAnalyzer.AnalyzeAsync(
                _baseFile, _compareFile, format: "json"));

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("methods", out _));
        Assert.True(root.TryGetProperty("baseMethodCount", out _));
        Assert.True(root.TryGetProperty("compareMethodCount", out _));
    }

    [Fact]
    public async Task Check_JsonFormat()
    {
        await File.WriteAllLinesAsync(_baseFile, MethodStatsTrace);
        await File.WriteAllLinesAsync(_compareFile, MethodStatsCompareTrace);

        var output = await TestHelpers.CaptureConsoleOutputAsync(
            async () => await CheckAnalyzer.AnalyzeAsync(
                _baseFile, _compareFile, threshold: 10.0, format: "json"));

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("result", out _));
        Assert.True(root.TryGetProperty("regressionCount", out _));
        Assert.True(root.TryGetProperty("threshold", out _));
        Assert.True(root.TryGetProperty("methods", out _));
    }
}
