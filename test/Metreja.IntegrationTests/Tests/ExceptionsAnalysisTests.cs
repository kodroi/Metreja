using Metreja.Tool.Analysis;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class ExceptionsAnalysisTests : IAsyncLifetime
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
    public async Task Exceptions_RanksByFrequency()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"exception","tsNs":100,"pid":1,"sessionId":"s1","tid":1,"exType":"System.ArgumentException","asm":"App","ns":"MyNs","cls":"MyClass","m":"Validate"}""",
            """{"event":"exception","tsNs":200,"pid":1,"sessionId":"s1","tid":1,"exType":"System.InvalidOperationException","asm":"App","ns":"MyNs","cls":"MyClass","m":"ThrowingMethod"}""",
            """{"event":"exception","tsNs":300,"pid":1,"sessionId":"s1","tid":1,"exType":"System.InvalidOperationException","asm":"App","ns":"MyNs","cls":"MyClass","m":"ThrowingMethod"}""",
            """{"event":"exception","tsNs":400,"pid":1,"sessionId":"s1","tid":1,"exType":"System.InvalidOperationException","asm":"App","ns":"MyNs","cls":"MyClass","m":"CatchChild"}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            ExceptionsAnalyzer.AnalyzeAsync(_tempFile, top: 20, filters: []));

        var lines = output.Split('\n');
        var invalidOpLine = Array.FindIndex(lines, l => l.Contains("System.InvalidOperationException"));
        var argExLine = Array.FindIndex(lines, l => l.Contains("System.ArgumentException"));
        Assert.True(invalidOpLine > 0 && argExLine > 0, $"Both exception types should appear in output:\n{output}");
        Assert.True(invalidOpLine < argExLine, "InvalidOperationException (3) should appear before ArgumentException (1)");
    }

    [Fact]
    public async Task Exceptions_ShowsThrowSites()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"exception","tsNs":100,"pid":1,"sessionId":"s1","tid":1,"exType":"System.InvalidOperationException","asm":"App","ns":"MyNs","cls":"MyClass","m":"ThrowingMethod"}""",
            """{"event":"exception","tsNs":200,"pid":1,"sessionId":"s1","tid":1,"exType":"System.InvalidOperationException","asm":"App","ns":"MyNs","cls":"MyClass","m":"ThrowingMethod"}""",
            """{"event":"exception","tsNs":300,"pid":1,"sessionId":"s1","tid":1,"exType":"System.InvalidOperationException","asm":"App","ns":"MyNs","cls":"OtherClass","m":"CatchChild"}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            ExceptionsAnalyzer.AnalyzeAsync(_tempFile, top: 20, filters: []));

        Assert.Contains("ThrowingMethod", output);
        Assert.Contains("CatchChild", output);
        Assert.Contains("(2)", output);
        Assert.Contains("(1)", output);
    }

    [Fact]
    public async Task Exceptions_FilterWorks()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"exception","tsNs":100,"pid":1,"sessionId":"s1","tid":1,"exType":"System.InvalidOperationException","asm":"App","ns":"MyNs","cls":"MyClass","m":"ThrowingMethod"}""",
            """{"event":"exception","tsNs":200,"pid":1,"sessionId":"s1","tid":1,"exType":"System.ArgumentException","asm":"App","ns":"MyNs","cls":"MyClass","m":"Validate"}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            ExceptionsAnalyzer.AnalyzeAsync(_tempFile, top: 20, filters: ["Argument"]));

        Assert.Contains("System.ArgumentException", output);
        Assert.DoesNotContain("System.InvalidOperationException", output);
    }

    [Fact]
    public async Task Exceptions_AggregatesExceptionStats()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"exception_stats","tsNs":100,"pid":1,"sessionId":"s1","exType":"System.ArgumentException","count":5,"asm":"App","ns":"MyNs","cls":"MyClass","m":"ErrorMethod"}""",
            """{"event":"exception_stats","tsNs":200,"pid":1,"sessionId":"s1","exType":"System.ArgumentException","count":3,"asm":"App","ns":"MyNs","cls":"MyClass","m":"ErrorMethod"}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            ExceptionsAnalyzer.AnalyzeAsync(_tempFile, top: 20, filters: []));

        Assert.Contains("System.ArgumentException", output);
        Assert.Contains("8", output);
    }

    [Fact]
    public async Task Exceptions_EmptyFile()
    {
        await File.WriteAllTextAsync(_tempFile, "");

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            ExceptionsAnalyzer.AnalyzeAsync(_tempFile, top: 20, filters: []));

        Assert.Contains("No exceptions found", output);
    }

    [Fact]
    public async Task Exceptions_IgnoresNonExceptionEvents()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"enter","tsNs":100,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"Run","async":false}""",
            """{"event":"leave","tsNs":200,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"MyNs","cls":"MyClass","m":"Run","async":false,"deltaNs":100}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            ExceptionsAnalyzer.AnalyzeAsync(_tempFile, top: 20, filters: []));

        Assert.Contains("No exceptions found", output);
    }
}
