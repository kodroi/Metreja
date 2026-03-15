using System.Text.Json;
using Metreja.Tool.Export;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class ExportTests : IAsyncLifetime
{
    private string _inputFile = "";
    private string _outputFile = "";

    public Task InitializeAsync()
    {
        _inputFile = Path.Combine(Path.GetTempPath(), $"metreja-export-{Guid.NewGuid():N}.ndjson");
        _outputFile = Path.Combine(Path.GetTempPath(), $"metreja-export-{Guid.NewGuid():N}.speedscope.json");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_inputFile)) File.Delete(_inputFile);
        if (File.Exists(_outputFile)) File.Delete(_outputFile);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Export_ValidJsonStructure()
    {
        await File.WriteAllLinesAsync(_inputFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"DoWork","async":false}""",
            """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"DoWork","async":false,"deltaNs":1000}"""
        ]);

        await SpeedscopeExporter.ExportAsync(_inputFile, _outputFile);

        Assert.True(File.Exists(_outputFile));
        var json = await File.ReadAllTextAsync(_outputFile);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("https://www.speedscope.app/file-format-schema.json", root.GetProperty("$schema").GetString());
        Assert.Equal("Metreja CLI", root.GetProperty("exporter").GetString());
        Assert.True(root.TryGetProperty("shared", out _));
        Assert.True(root.TryGetProperty("profiles", out _));
    }

    [Fact]
    public async Task Export_FramesPopulated()
    {
        await File.WriteAllLinesAsync(_inputFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"ClassA","m":"Method1","async":false}""",
            """{"event":"enter","tsNs":1500,"pid":1,"sessionId":"s1","tid":1,"depth":1,"asm":"App","ns":"MyNs","cls":"ClassB","m":"Method2","async":false}""",
            """{"event":"leave","tsNs":1800,"pid":1,"sessionId":"s1","tid":1,"depth":1,"asm":"App","ns":"MyNs","cls":"ClassB","m":"Method2","async":false,"deltaNs":300}""",
            """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"ClassA","m":"Method1","async":false,"deltaNs":1000}"""
        ]);

        await SpeedscopeExporter.ExportAsync(_inputFile, _outputFile);

        var json = await File.ReadAllTextAsync(_outputFile);
        var doc = JsonDocument.Parse(json);
        var frames = doc.RootElement.GetProperty("shared").GetProperty("frames");

        Assert.Equal(2, frames.GetArrayLength());
        var frameNames = new List<string>();
        foreach (var frame in frames.EnumerateArray())
        {
            frameNames.Add(frame.GetProperty("name").GetString()!);
        }
        Assert.Contains("MyNs.ClassA.Method1", frameNames);
        Assert.Contains("MyNs.ClassB.Method2", frameNames);
    }

    [Fact]
    public async Task Export_EventsAlternateOC()
    {
        await File.WriteAllLinesAsync(_inputFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M","async":false}""",
            """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M","async":false,"deltaNs":1000}"""
        ]);

        await SpeedscopeExporter.ExportAsync(_inputFile, _outputFile);

        var json = await File.ReadAllTextAsync(_outputFile);
        var doc = JsonDocument.Parse(json);
        var events = doc.RootElement.GetProperty("profiles")[0].GetProperty("events");

        Assert.Equal(2, events.GetArrayLength());
        Assert.Equal("O", events[0].GetProperty("type").GetString());
        Assert.Equal("C", events[1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Export_FrameIndicesValid()
    {
        await File.WriteAllLinesAsync(_inputFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M","async":false}""",
            """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M","async":false,"deltaNs":1000}"""
        ]);

        await SpeedscopeExporter.ExportAsync(_inputFile, _outputFile);

        var json = await File.ReadAllTextAsync(_outputFile);
        var doc = JsonDocument.Parse(json);
        var frames = doc.RootElement.GetProperty("shared").GetProperty("frames");
        var events = doc.RootElement.GetProperty("profiles")[0].GetProperty("events");

        var frameCount = frames.GetArrayLength();
        foreach (var ev in events.EnumerateArray())
        {
            var frameIdx = ev.GetProperty("frame").GetInt32();
            Assert.InRange(frameIdx, 0, frameCount - 1);
        }
    }

    [Fact]
    public async Task Export_MultipleThreadsProduceMultipleProfiles()
    {
        await File.WriteAllLinesAsync(_inputFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M1","async":false}""",
            """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M1","async":false,"deltaNs":1000}""",
            """{"event":"enter","tsNs":1500,"pid":1,"sessionId":"s1","tid":2,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M2","async":false}""",
            """{"event":"leave","tsNs":2500,"pid":1,"sessionId":"s1","tid":2,"depth":0,"asm":"App","ns":"N","cls":"C","m":"M2","async":false,"deltaNs":1000}"""
        ]);

        await SpeedscopeExporter.ExportAsync(_inputFile, _outputFile);

        var json = await File.ReadAllTextAsync(_outputFile);
        var doc = JsonDocument.Parse(json);
        var profiles = doc.RootElement.GetProperty("profiles");

        Assert.Equal(2, profiles.GetArrayLength());
    }
}
