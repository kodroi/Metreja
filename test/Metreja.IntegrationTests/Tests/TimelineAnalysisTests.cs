using Metreja.Tool.Analysis;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class TimelineAnalysisTests : IAsyncLifetime
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
    public async Task Timeline_ShowsChronologicalOrder()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"First","async":false}""",
            """{"event":"enter","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":1,"asm":"App","ns":"MyNs","cls":"MyClass","m":"Second","async":false}""",
            """{"event":"leave","tsNs":3000,"pid":1,"sessionId":"s1","tid":1,"depth":1,"asm":"App","ns":"MyNs","cls":"MyClass","m":"Second","async":false,"deltaNs":1000}""",
            """{"event":"leave","tsNs":4000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"First","async":false,"deltaNs":3000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            TimelineAnalyzer.AnalyzeAsync(_tempFile, tidFilter: null, eventTypeFilter: null, methodFilter: null, top: 100));

        var lines = output.Split('\n').Where(l => l.Contains("MyNs")).ToArray();
        Assert.Equal(4, lines.Length);
        Assert.Contains("First", lines[0]);
        Assert.Contains("Second", lines[1]);
        Assert.Contains("Second", lines[2]);
        Assert.Contains("First", lines[3]);
    }

    [Fact]
    public async Task Timeline_TidFilter()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"OnThread1","async":false}""",
            """{"event":"enter","tsNs":2000,"pid":1,"sessionId":"s1","tid":2,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"OnThread2","async":false}""",
            """{"event":"leave","tsNs":3000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"OnThread1","async":false,"deltaNs":2000}""",
            """{"event":"leave","tsNs":4000,"pid":1,"sessionId":"s1","tid":2,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"OnThread2","async":false,"deltaNs":2000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            TimelineAnalyzer.AnalyzeAsync(_tempFile, tidFilter: 1, eventTypeFilter: null, methodFilter: null, top: 100));

        Assert.Contains("OnThread1", output);
        Assert.DoesNotContain("OnThread2", output);
    }

    [Fact]
    public async Task Timeline_EventTypeFilter()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"DoWork","async":false}""",
            """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"DoWork","async":false,"deltaNs":1000}""",
            """{"event":"exception","tsNs":1500,"pid":1,"sessionId":"s1","tid":1,"exType":"System.Exception","asm":"App","ns":"MyNs","cls":"MyClass","m":"DoWork"}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            TimelineAnalyzer.AnalyzeAsync(_tempFile, tidFilter: null, eventTypeFilter: "enter", methodFilter: null, top: 100));

        var dataLines = output.Split('\n').Where(l => l.Contains("MyNs")).ToArray();
        Assert.Single(dataLines);
        Assert.Contains("enter", dataLines[0]);
    }

    [Fact]
    public async Task Timeline_MethodFilter()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"DoWork","async":false}""",
            """{"event":"enter","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":1,"asm":"App","ns":"MyNs","cls":"MyClass","m":"Helper","async":false}""",
            """{"event":"gc_started","tsNs":2500,"pid":1,"sessionId":"s1","gen0":true,"gen1":false,"gen2":false}""",
            """{"event":"leave","tsNs":3000,"pid":1,"sessionId":"s1","tid":1,"depth":1,"asm":"App","ns":"MyNs","cls":"MyClass","m":"Helper","async":false,"deltaNs":1000}""",
            """{"event":"leave","tsNs":4000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"DoWork","async":false,"deltaNs":3000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            TimelineAnalyzer.AnalyzeAsync(_tempFile, tidFilter: null, eventTypeFilter: null, methodFilter: "DoWork", top: 100));

        Assert.Contains("DoWork", output);
        Assert.DoesNotContain("Helper", output);
        Assert.DoesNotContain("gc_started", output);
    }

    [Fact]
    public async Task Timeline_TopLimit()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"First","async":false}""",
            """{"event":"enter","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":1,"asm":"App","ns":"MyNs","cls":"MyClass","m":"Second","async":false}""",
            """{"event":"leave","tsNs":3000,"pid":1,"sessionId":"s1","tid":1,"depth":1,"asm":"App","ns":"MyNs","cls":"MyClass","m":"Second","async":false,"deltaNs":1000}""",
            """{"event":"leave","tsNs":4000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"First","async":false,"deltaNs":3000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            TimelineAnalyzer.AnalyzeAsync(_tempFile, tidFilter: null, eventTypeFilter: null, methodFilter: null, top: 2));

        var dataLines = output.Split('\n').Where(l => l.Contains("MyNs")).ToArray();
        Assert.Equal(2, dataLines.Length);
        Assert.Contains("Showing 2 event(s)", output);
    }

    [Fact]
    public async Task Timeline_RelativeTimestamps()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":5000000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"First","async":false}""",
            """{"event":"enter","tsNs":6000000,"pid":1,"sessionId":"s1","tid":1,"depth":1,"asm":"App","ns":"MyNs","cls":"MyClass","m":"Second","async":false}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            TimelineAnalyzer.AnalyzeAsync(_tempFile, tidFilter: null, eventTypeFilter: null, methodFilter: null, top: 100));

        var dataLines = output.Split('\n').Where(l => l.Contains("MyNs")).ToArray();
        Assert.Equal(2, dataLines.Length);
        Assert.Contains("0ns", dataLines[0]);
        Assert.Contains(AnalyzerHelpers.FormatNs(1_000_000), dataLines[1]);
    }
}
