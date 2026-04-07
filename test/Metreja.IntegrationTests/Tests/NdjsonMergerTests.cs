using System.Text.Json;
using Metreja.Tool.Analysis;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class NdjsonMergerTests : IAsyncLifetime
{
    private string _tempDir = "";

    public Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"metreja-merger-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("/abs/path/{sessionId}_{pid}.ndjson", "abc123", "/abs/path/abc123.ndjson")]
    [InlineData("/abs/path/trace-{sessionId}-{pid}.ndjson", "abc123", "/abs/path/trace-abc123.ndjson")]
    [InlineData("/abs/path/{pid}_{sessionId}.ndjson", "abc123", "/abs/path/abc123.ndjson")]
    [InlineData("/abs/path/{sessionId}.ndjson", "abc123", "/abs/path/abc123.ndjson")]
    public void ComputeMergedPath_HandlesVariousTemplates(string template, string sessionId, string expected)
    {
        var result = NdjsonMerger.ComputeMergedPath(template, sessionId);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindSessionOutputFiles_FindsMultipleFiles()
    {
        var template = Path.Combine(_tempDir, "{sessionId}_{pid}.ndjson");
        File.WriteAllText(Path.Combine(_tempDir, "abc123_1000.ndjson"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "abc123_2000.ndjson"), "{}");

        var files = NdjsonMerger.FindSessionOutputFiles(template, "abc123");

        Assert.Equal(2, files.Length);
        Assert.All(files, f => Assert.Contains("abc123_", f));
    }

    [Fact]
    public void FindSessionOutputFiles_ExcludesMergedFile()
    {
        var template = Path.Combine(_tempDir, "{sessionId}_{pid}.ndjson");
        File.WriteAllText(Path.Combine(_tempDir, "abc123_1000.ndjson"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "abc123.ndjson"), "{}"); // merged file

        var files = NdjsonMerger.FindSessionOutputFiles(template, "abc123");

        Assert.Single(files);
        Assert.Contains("abc123_1000", files[0]);
    }

    [Fact]
    public void FindSessionOutputFiles_EmptyDirectory_ReturnsEmpty()
    {
        var template = Path.Combine(_tempDir, "{sessionId}_{pid}.ndjson");

        var files = NdjsonMerger.FindSessionOutputFiles(template, "abc123");

        Assert.Empty(files);
    }

    [Fact]
    public void FindSessionOutputFiles_NonexistentDirectory_ReturnsEmpty()
    {
        var template = Path.Combine(_tempDir, "nonexistent", "{sessionId}_{pid}.ndjson");

        var files = NdjsonMerger.FindSessionOutputFiles(template, "abc123");

        Assert.Empty(files);
    }

    [Fact]
    public async Task MergeFilesAsync_SortsByTimestamp()
    {
        var file1 = Path.Combine(_tempDir, "f1.ndjson");
        var file2 = Path.Combine(_tempDir, "f2.ndjson");
        var output = Path.Combine(_tempDir, "merged.ndjson");

        await File.WriteAllLinesAsync(file1,
        [
            """{"event":"enter","tsNs":300,"tid":1}""",
            """{"event":"enter","tsNs":100,"tid":1}"""
        ]);
        await File.WriteAllLinesAsync(file2,
        [
            """{"event":"enter","tsNs":200,"tid":2}"""
        ]);

        var result = await NdjsonMerger.MergeFilesAsync([file1, file2], output);

        Assert.Equal(3, result.EventCount);
        Assert.Equal(0, result.SkippedCount);

        var lines = await File.ReadAllLinesAsync(output);
        var timestamps = lines.Select(line =>
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.GetProperty("tsNs").GetInt64();
        }).ToList();
        Assert.Equal([100, 200, 300], timestamps);
    }

    [Fact]
    public async Task MergeFilesAsync_HandlesEmptyFiles()
    {
        var file1 = Path.Combine(_tempDir, "empty.ndjson");
        var output = Path.Combine(_tempDir, "merged.ndjson");

        await File.WriteAllTextAsync(file1, "");

        var result = await NdjsonMerger.MergeFilesAsync([file1], output);

        Assert.Equal(0, result.EventCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public async Task MergeFilesAsync_SkipsMalformedLines()
    {
        var file1 = Path.Combine(_tempDir, "bad.ndjson");
        var output = Path.Combine(_tempDir, "merged.ndjson");

        await File.WriteAllLinesAsync(file1,
        [
            """{"event":"enter","tsNs":100,"tid":1}""",
            "not json at all",
            """{"event":"enter","tsNs":200,"tid":1}"""
        ]);

        var result = await NdjsonMerger.MergeFilesAsync([file1], output);

        Assert.Equal(2, result.EventCount);
        Assert.Equal(1, result.SkippedCount);
    }
}
