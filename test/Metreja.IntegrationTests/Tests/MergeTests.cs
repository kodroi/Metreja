using System.CommandLine;
using System.Text.Json;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class MergeTests : IAsyncLifetime
{
    private string _file1 = "";
    private string _file2 = "";
    private string _output = "";

    public Task InitializeAsync()
    {
        _file1 = Path.Combine(Path.GetTempPath(), $"metreja-merge1-{Guid.NewGuid():N}.ndjson");
        _file2 = Path.Combine(Path.GetTempPath(), $"metreja-merge2-{Guid.NewGuid():N}.ndjson");
        _output = Path.Combine(Path.GetTempPath(), $"metreja-merged-{Guid.NewGuid():N}.ndjson");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_file1)) File.Delete(_file1);
        if (File.Exists(_file2)) File.Delete(_file2);
        if (File.Exists(_output)) File.Delete(_output);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Merge_SortsByTsNs()
    {
        await File.WriteAllLinesAsync(_file1,
        [
            """{"event":"enter","tsNs":100,"pid":1,"sessionId":"s1","tid":1}""",
            """{"event":"enter","tsNs":300,"pid":1,"sessionId":"s1","tid":1}"""
        ]);
        await File.WriteAllLinesAsync(_file2,
        [
            """{"event":"enter","tsNs":200,"pid":1,"sessionId":"s2","tid":1}""",
            """{"event":"enter","tsNs":400,"pid":1,"sessionId":"s2","tid":1}"""
        ]);

        await Metreja.Tool.Commands.MergeCommand.Create()
            .Parse([_file1, _file2, "--output", _output])
            .InvokeAsync();

        Assert.True(File.Exists(_output));
        var lines = await File.ReadAllLinesAsync(_output);
        Assert.Equal(4, lines.Length);

        var timestamps = new List<long>();
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            timestamps.Add(doc.RootElement.GetProperty("tsNs").GetInt64());
        }
        Assert.Equal([100, 200, 300, 400], timestamps);
    }

    [Fact]
    public async Task Merge_StableSortForSameTimestamps()
    {
        await File.WriteAllLinesAsync(_file1,
        [
            """{"event":"enter","tsNs":100,"pid":1,"sessionId":"s1","tid":1,"m":"first"}""",
            """{"event":"enter","tsNs":100,"pid":1,"sessionId":"s1","tid":1,"m":"second"}"""
        ]);
        await File.WriteAllLinesAsync(_file2,
        [
            """{"event":"enter","tsNs":100,"pid":1,"sessionId":"s2","tid":1,"m":"third"}"""
        ]);

        await Metreja.Tool.Commands.MergeCommand.Create()
            .Parse([_file1, _file2, "--output", _output])
            .InvokeAsync();

        var lines = await File.ReadAllLinesAsync(_output);
        Assert.Equal(3, lines.Length);

        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal(100, doc.RootElement.GetProperty("tsNs").GetInt64());
        }
    }

    [Fact]
    public async Task Merge_OneEmptyFile()
    {
        await File.WriteAllLinesAsync(_file1,
        [
            """{"event":"enter","tsNs":100,"pid":1,"sessionId":"s1","tid":1}""",
            """{"event":"enter","tsNs":200,"pid":1,"sessionId":"s1","tid":1}"""
        ]);
        await File.WriteAllTextAsync(_file2, "");

        await Metreja.Tool.Commands.MergeCommand.Create()
            .Parse([_file1, _file2, "--output", _output])
            .InvokeAsync();

        var lines = await File.ReadAllLinesAsync(_output);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public async Task Merge_DifferentSessionsPreserved()
    {
        await File.WriteAllLinesAsync(_file1,
        [
            """{"event":"enter","tsNs":100,"pid":1,"sessionId":"session-A","tid":1}"""
        ]);
        await File.WriteAllLinesAsync(_file2,
        [
            """{"event":"enter","tsNs":200,"pid":2,"sessionId":"session-B","tid":2}"""
        ]);

        await Metreja.Tool.Commands.MergeCommand.Create()
            .Parse([_file1, _file2, "--output", _output])
            .InvokeAsync();

        var lines = await File.ReadAllLinesAsync(_output);
        Assert.Equal(2, lines.Length);
        Assert.Contains("session-A", lines[0]);
        Assert.Contains("session-B", lines[1]);
    }
}
