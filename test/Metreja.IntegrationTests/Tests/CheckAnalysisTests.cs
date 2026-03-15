using Metreja.Tool.Analysis;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class CheckAnalysisTests : IAsyncLifetime
{
    private string _baseFile = "";
    private string _compareFile = "";

    public Task InitializeAsync()
    {
        _baseFile = Path.Combine(Path.GetTempPath(), $"metreja-test-base-{Guid.NewGuid():N}.ndjson");
        _compareFile = Path.Combine(Path.GetTempPath(), $"metreja-test-compare-{Guid.NewGuid():N}.ndjson");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_baseFile)) File.Delete(_baseFile);
        if (File.Exists(_compareFile)) File.Delete(_compareFile);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Check_NoRegression_ExitsZero()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SlowMethod","callCount":10,"totalSelfNs":10000000,"maxSelfNs":1000000,"totalInclusiveNs":10000000,"maxInclusiveNs":1000000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SlowMethod","callCount":10,"totalSelfNs":10000000,"maxSelfNs":1000000,"totalInclusiveNs":10000000,"maxInclusiveNs":1000000}"""
        ]);

        var result = 0;
        var output = await TestHelpers.CaptureConsoleOutputAsync(
            async () => result = await CheckAnalyzer.AnalyzeAsync(_baseFile, _compareFile, 10.0));

        Assert.Equal(0, result);
        Assert.Contains("PASS", output);
    }

    [Fact]
    public async Task Check_Regression_ExitsOne()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SlowMethod","callCount":10,"totalSelfNs":10000000,"maxSelfNs":1000000,"totalInclusiveNs":10000000,"maxInclusiveNs":1000000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SlowMethod","callCount":10,"totalSelfNs":50000000,"maxSelfNs":5000000,"totalInclusiveNs":50000000,"maxInclusiveNs":5000000}"""
        ]);

        var result = 0;
        var output = await TestHelpers.CaptureConsoleOutputAsync(
            async () => result = await CheckAnalyzer.AnalyzeAsync(_baseFile, _compareFile, 10.0));

        Assert.Equal(1, result);
        Assert.Contains("FAIL", output);
        Assert.Contains("REGRESSION", output);
    }

    [Fact]
    public async Task Check_BelowThreshold_Passes()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SlowMethod","callCount":10,"totalSelfNs":10000000,"maxSelfNs":1000000,"totalInclusiveNs":10000000,"maxInclusiveNs":1000000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SlowMethod","callCount":10,"totalSelfNs":10500000,"maxSelfNs":1050000,"totalInclusiveNs":10500000,"maxInclusiveNs":1050000}"""
        ]);

        var result = 0;
        var output = await TestHelpers.CaptureConsoleOutputAsync(
            async () => result = await CheckAnalyzer.AnalyzeAsync(_baseFile, _compareFile, 10.0));

        Assert.Equal(0, result);
        Assert.Contains("OK", output);
    }

    [Fact]
    public async Task Check_CustomThreshold()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SlowMethod","callCount":10,"totalSelfNs":10000000,"maxSelfNs":1000000,"totalInclusiveNs":10000000,"maxInclusiveNs":1000000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"SlowMethod","callCount":10,"totalSelfNs":10500000,"maxSelfNs":1050000,"totalInclusiveNs":10500000,"maxInclusiveNs":1050000}"""
        ]);

        var result = 0;
        var output = await TestHelpers.CaptureConsoleOutputAsync(
            async () => result = await CheckAnalyzer.AnalyzeAsync(_baseFile, _compareFile, 3.0));

        Assert.Equal(1, result);
        Assert.Contains("REGRESSION", output);
    }

    [Fact]
    public async Task Check_NewMethod_NotRegression()
    {
        await File.WriteAllLinesAsync(_baseFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"ExistingMethod","callCount":10,"totalSelfNs":10000000,"maxSelfNs":1000000,"totalInclusiveNs":10000000,"maxInclusiveNs":1000000}"""
        ]);
        await File.WriteAllLinesAsync(_compareFile,
        [
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"ExistingMethod","callCount":10,"totalSelfNs":10000000,"maxSelfNs":1000000,"totalInclusiveNs":10000000,"maxInclusiveNs":1000000}""",
            """{"event":"method_stats","tsNs":0,"pid":1,"sessionId":"s1","asm":"App","ns":"N","cls":"C","m":"NewMethod","callCount":5,"totalSelfNs":50000000,"maxSelfNs":10000000,"totalInclusiveNs":50000000,"maxInclusiveNs":10000000}"""
        ]);

        var result = 0;
        var output = await TestHelpers.CaptureConsoleOutputAsync(
            async () => result = await CheckAnalyzer.AnalyzeAsync(_baseFile, _compareFile, 10.0));

        Assert.Equal(0, result);
        Assert.Contains("PASS", output);
        Assert.DoesNotContain("REGRESSION", output);
    }
}
