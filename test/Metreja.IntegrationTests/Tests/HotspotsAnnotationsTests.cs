using Metreja.Tool.Analysis;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class HotspotsAnnotationsTests : IAsyncLifetime
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
    public async Task Hotspots_ShowsTailcallCount()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"TailMethod","async":false}""",
            """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"TailMethod","async":false,"deltaNs":1000,"tailcall":true}""",
            """{"event":"enter","tsNs":3000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"TailMethod","async":false}""",
            """{"event":"leave","tsNs":4000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"TailMethod","async":false,"deltaNs":1000,"tailcall":true}""",
            """{"event":"enter","tsNs":5000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"TailMethod","async":false}""",
            """{"event":"leave","tsNs":6000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"TailMethod","async":false,"deltaNs":1000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            HotspotsAnalyzer.AnalyzeAsync(_tempFile, top: 10, minMs: 0, sortBy: "self", filters: []));

        Assert.Contains("Tailcalls", output);
        Assert.Contains("TailMethod", output);
        // Header should contain Tailcalls column
        var lines = output.Split('\n');
        var headerLine = lines.First(l => l.Contains("Tailcalls"));
        Assert.Contains("Tailcalls", headerLine);
        // Data line for TailMethod should show tailcall count of 2
        var dataLine = lines.First(l => l.Contains("TailMethod"));
        Assert.Contains("2", dataLine);
    }

    [Fact]
    public async Task Hotspots_ShowsExceptionCount()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"ThrowMethod","async":false}""",
            """{"event":"exception","tsNs":1500,"pid":1,"sessionId":"s1","tid":1,"exType":"System.InvalidOperationException","asm":"App","ns":"N","cls":"C","m":"ThrowMethod"}""",
            """{"event":"exception","tsNs":1600,"pid":1,"sessionId":"s1","tid":1,"exType":"System.ArgumentException","asm":"App","ns":"N","cls":"C","m":"ThrowMethod"}""",
            """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"ThrowMethod","async":false,"deltaNs":1000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            HotspotsAnalyzer.AnalyzeAsync(_tempFile, top: 10, minMs: 0, sortBy: "self", filters: []));

        Assert.Contains("Exceptions", output);
        var lines = output.Split('\n');
        var dataLine = lines.First(l => l.Contains("ThrowMethod"));
        Assert.Contains("2", dataLine);
    }

    [Fact]
    public async Task CallTree_ShowsExceptionMarkers()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"Root","async":false}""",
            """{"event":"enter","tsNs":1100,"pid":1,"sessionId":"s1","tid":1,"depth":1,"asm":"App","ns":"N","cls":"C","m":"Child","async":false}""",
            """{"event":"exception","tsNs":1200,"pid":1,"sessionId":"s1","tid":1,"exType":"System.InvalidOperationException","asm":"App","ns":"N","cls":"C","m":"Child"}""",
            """{"event":"leave","tsNs":1500,"pid":1,"sessionId":"s1","tid":1,"depth":1,"asm":"App","ns":"N","cls":"C","m":"Child","async":false,"deltaNs":400}""",
            """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"Root","async":false,"deltaNs":1000}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            CallTreeAnalyzer.AnalyzeAsync(_tempFile, "Root", tidFilter: null, occurrence: 1));

        Assert.Contains("Root", output);
        Assert.Contains("Child", output);
        Assert.Contains("System.InvalidOperationException", output);
    }
}
