using System.Globalization;
using Metreja.Tool.Analysis;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class SummaryAnalysisTests : IAsyncLifetime
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
    public async Task Summary_ShowsCorrectEventCounts()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"session_metadata","tsNs":0,"pid":1234,"sessionId":"sess1","scenario":"baseline"}""",
            """{"event":"enter","tsNs":1000,"pid":1234,"sessionId":"sess1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"DoWork","async":false}""",
            """{"event":"enter","tsNs":2000,"pid":1234,"sessionId":"sess1","tid":1,"depth":1,"asm":"App","ns":"N","cls":"C","m":"Helper","async":false}""",
            """{"event":"leave","tsNs":3000,"pid":1234,"sessionId":"sess1","tid":1,"depth":1,"asm":"App","ns":"N","cls":"C","m":"Helper","async":false,"deltaNs":1000}""",
            """{"event":"leave","tsNs":4000,"pid":1234,"sessionId":"sess1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"DoWork","async":false,"deltaNs":3000}""",
            """{"event":"exception","tsNs":5000,"pid":1234,"sessionId":"sess1","tid":1,"exType":"System.InvalidOperationException","asm":"App","ns":"N","cls":"C","m":"DoWork"}""",
            """{"event":"gc_start","tsNs":6000,"pid":1234,"sessionId":"sess1","gen0":true,"gen1":false,"gen2":false,"reason":"small"}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            SummaryAnalyzer.AnalyzeAsync(_tempFile));

        Assert.Contains("Total events: 7", output);
        Assert.Contains("enter", output);
        Assert.Contains("leave", output);
        Assert.Contains("exception", output);
        Assert.Contains("gc_start", output);
    }

    [Fact]
    public async Task Summary_ShowsWallClockDuration()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":1000000000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"Start","async":false}""",
            """{"event":"leave","tsNs":3450000000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"Start","async":false,"deltaNs":2450000000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            SummaryAnalyzer.AnalyzeAsync(_tempFile));

        var expectedDuration = (2450000000L / 1_000_000_000.0).ToString("F2", CultureInfo.CurrentCulture) + "s";
        Assert.Contains(expectedDuration, output);
    }

    [Fact]
    public async Task Summary_ShowsThreadAndMethodCounts()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"MethodA","async":false}""",
            """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"MethodA","async":false,"deltaNs":1000}""",
            """{"event":"enter","tsNs":3000,"pid":1,"sessionId":"s1","tid":2,"depth":0,"asm":"App","ns":"N","cls":"C","m":"MethodB","async":false}""",
            """{"event":"leave","tsNs":4000,"pid":1,"sessionId":"s1","tid":2,"depth":0,"asm":"App","ns":"N","cls":"C","m":"MethodB","async":false,"deltaNs":1000}""",
            """{"event":"enter","tsNs":5000,"pid":1,"sessionId":"s1","tid":3,"depth":0,"asm":"App","ns":"N","cls":"D","m":"MethodC","async":false}""",
            """{"event":"leave","tsNs":6000,"pid":1,"sessionId":"s1","tid":3,"depth":0,"asm":"App","ns":"N","cls":"D","m":"MethodC","async":false,"deltaNs":1000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            SummaryAnalyzer.AnalyzeAsync(_tempFile));

        Assert.Contains("Threads:      3", output);
        Assert.Contains("Methods:      3", output);
    }

    [Fact]
    public async Task Summary_ShowsSessionMetadata()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"session_metadata","tsNs":0,"pid":12345,"sessionId":"abc123","scenario":"baseline"}""",
            """{"event":"enter","tsNs":1000,"pid":12345,"sessionId":"abc123","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"Run","async":false}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            SummaryAnalyzer.AnalyzeAsync(_tempFile));

        Assert.Contains("Session:      abc123", output);
        Assert.Contains("Scenario:     baseline", output);
        Assert.Contains("PID:          12345", output);
    }

    [Fact]
    public async Task Summary_EmptyFile_ShowsZeros()
    {
        await File.WriteAllTextAsync(_tempFile, "");

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            SummaryAnalyzer.AnalyzeAsync(_tempFile));

        Assert.Contains("Total events: 0", output);
        Assert.Contains("Threads:      0", output);
        Assert.Contains("Methods:      0", output);
        Assert.Contains("Duration:     N/A", output);
        Assert.Contains("GC collections:  0", output);
        Assert.Contains("Exceptions:     0", output);
    }
}
