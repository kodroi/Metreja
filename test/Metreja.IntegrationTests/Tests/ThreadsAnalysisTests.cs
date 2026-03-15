using Metreja.Tool.Analysis;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class ThreadsAnalysisTests : IAsyncLifetime
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
    public async Task Threads_ShowsPerThreadBreakdown()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M1","async":false}""",
            """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M1","async":false,"deltaNs":1000}""",
            """{"event":"enter","tsNs":1500,"pid":1,"sessionId":"s1","tid":2,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M2","async":false}""",
            """{"event":"leave","tsNs":3000,"pid":1,"sessionId":"s1","tid":2,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M2","async":false,"deltaNs":1500}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            ThreadsAnalyzer.AnalyzeAsync(_tempFile, "calls"));

        Assert.Contains("TID", output);
        Assert.Contains("Calls", output);
        Assert.Contains("Root Time", output);
        Assert.Contains("Total threads: 2", output);
    }

    [Fact]
    public async Task Threads_SortByCalls()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M1","async":false}""",
            """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M1","async":false,"deltaNs":1000}""",
            """{"event":"enter","tsNs":3000,"pid":1,"sessionId":"s1","tid":2,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M2","async":false}""",
            """{"event":"leave","tsNs":4000,"pid":1,"sessionId":"s1","tid":2,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M2","async":false,"deltaNs":1000}""",
            """{"event":"enter","tsNs":5000,"pid":1,"sessionId":"s1","tid":2,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M3","async":false}""",
            """{"event":"leave","tsNs":6000,"pid":1,"sessionId":"s1","tid":2,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M3","async":false,"deltaNs":1000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            ThreadsAnalyzer.AnalyzeAsync(_tempFile, "calls"));

        var lines = output.Split('\n');
        var tid2Line = Array.FindIndex(lines, l => l.TrimStart().StartsWith('2'));
        var tid1Line = Array.FindIndex(lines, l => l.TrimStart().StartsWith('1'));
        Assert.True(tid2Line > 0 && tid1Line > 0, $"Both threads should appear in output:\n{output}");
        Assert.True(tid2Line < tid1Line, "Thread 2 (2 calls) should appear before thread 1 (1 call)");
    }

    [Fact]
    public async Task Threads_SortByTime()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M1","async":false}""",
            """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M1","async":false,"deltaNs":5000}""",
            """{"event":"enter","tsNs":3000,"pid":1,"sessionId":"s1","tid":2,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M2","async":false}""",
            """{"event":"leave","tsNs":4000,"pid":1,"sessionId":"s1","tid":2,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M2","async":false,"deltaNs":1000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            ThreadsAnalyzer.AnalyzeAsync(_tempFile, "time"));

        var lines = output.Split('\n');
        var tid1Line = Array.FindIndex(lines, l => l.TrimStart().StartsWith('1'));
        var tid2Line = Array.FindIndex(lines, l => l.TrimStart().StartsWith('2'));
        Assert.True(tid1Line > 0 && tid2Line > 0, $"Both threads should appear in output:\n{output}");
        Assert.True(tid1Line < tid2Line, "Thread 1 (5000ns root time) should appear before thread 2 (1000ns root time)");
    }

    [Fact]
    public async Task Threads_ActiveWindow()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":10000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M1","async":false}""",
            """{"event":"leave","tsNs":20000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M1","async":false,"deltaNs":10000}""",
            """{"event":"enter","tsNs":30000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M2","async":false}""",
            """{"event":"leave","tsNs":50000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M2","async":false,"deltaNs":20000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            ThreadsAnalyzer.AnalyzeAsync(_tempFile, "calls"));

        Assert.Contains("0ns", output);
        Assert.Contains(AnalyzerHelpers.FormatNs(40000), output);
        Assert.Contains("Total threads: 1", output);
    }

    [Fact]
    public async Task Threads_SingleThread()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":5000,"pid":1,"sessionId":"s1","tid":42,"depth":0,"asm":"App","ns":"N","cls":"C","m":"Run","async":false}""",
            """{"event":"leave","tsNs":15000,"pid":1,"sessionId":"s1","tid":42,"depth":0,"asm":"App","ns":"N","cls":"C","m":"Run","async":false,"deltaNs":10000}""",
            """{"event":"enter","tsNs":16000,"pid":1,"sessionId":"s1","tid":42,"depth":0,"asm":"App","ns":"N","cls":"C","m":"Run2","async":false}""",
            """{"event":"leave","tsNs":25000,"pid":1,"sessionId":"s1","tid":42,"depth":0,"asm":"App","ns":"N","cls":"C","m":"Run2","async":false,"deltaNs":9000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            ThreadsAnalyzer.AnalyzeAsync(_tempFile, "calls"));

        Assert.Contains("42", output);
        Assert.Contains("Total threads: 1", output);
        var lines = output.Split('\n');
        var tidLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("42", StringComparison.Ordinal));
        Assert.NotNull(tidLine);
        Assert.Contains("2", tidLine);
        Assert.Contains(AnalyzerHelpers.FormatNs(19000), output);
    }
}
