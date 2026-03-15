using Metreja.Tool.Export;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class CsvExportTests : IAsyncLifetime
{
    private string _inputFile = "";
    private string _outputFile = "";

    public Task InitializeAsync()
    {
        _inputFile = Path.Combine(Path.GetTempPath(), $"metreja-csv-{Guid.NewGuid():N}.ndjson");
        _outputFile = Path.Combine(Path.GetTempPath(), $"metreja-csv-{Guid.NewGuid():N}.csv");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_inputFile)) File.Delete(_inputFile);
        if (File.Exists(_outputFile)) File.Delete(_outputFile);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CsvExport_EnterLeaveEvents_ProducesValidCsv()
    {
        await File.WriteAllLinesAsync(_inputFile,
        [
            """{"event":"enter","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"DoWork","async":false}""",
            """{"event":"leave","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"depth":0,"asm":"App","ns":"N","cls":"C","m":"DoWork","async":false,"deltaNs":1000}"""
        ]);

        await CsvExporter.ExportAsync(_inputFile, _outputFile);

        Assert.True(File.Exists(_outputFile));
        var lines = await File.ReadAllLinesAsync(_outputFile);

        // First line is the header
        Assert.Equal("tsNs,event,tid,depth,ns,cls,method,deltaNs,async", lines[0]);

        // At least one data row exists
        Assert.True(lines.Length >= 2, "Expected at least one data row after the header.");

        // Verify enter row
        var enterColumns = lines[1].Split(',');
        Assert.Equal(9, enterColumns.Length);
        Assert.Equal("1000", enterColumns[0]);   // tsNs
        Assert.Equal("enter", enterColumns[1]);   // event
        Assert.Equal("1", enterColumns[2]);        // tid
        Assert.Equal("0", enterColumns[3]);        // depth
        Assert.Equal("N", enterColumns[4]);        // ns
        Assert.Equal("C", enterColumns[5]);        // cls
        Assert.Equal("DoWork", enterColumns[6]);   // method
        Assert.Equal("", enterColumns[7]);         // deltaNs (empty for enter)
        Assert.Equal("false", enterColumns[8]);    // async

        // Verify leave row
        var leaveColumns = lines[2].Split(',');
        Assert.Equal(9, leaveColumns.Length);
        Assert.Equal("2000", leaveColumns[0]);     // tsNs
        Assert.Equal("leave", leaveColumns[1]);    // event
        Assert.Equal("1000", leaveColumns[7]);     // deltaNs
    }

    [Fact]
    public async Task CsvExport_MethodStatsEvents_ProducesValidCsv()
    {
        await File.WriteAllLinesAsync(_inputFile,
        [
            """{"event":"method_stats","pid":1,"sessionId":"s1","ns":"N","cls":"C","m":"DoWork","callCount":10,"totalSelfNs":5000,"maxSelfNs":800,"totalInclusiveNs":9000,"maxInclusiveNs":1500}""",
            """{"event":"method_stats","pid":1,"sessionId":"s1","ns":"N","cls":"C","m":"Helper","callCount":3,"totalSelfNs":1200,"maxSelfNs":500,"totalInclusiveNs":2000,"maxInclusiveNs":900}"""
        ]);

        await CsvExporter.ExportAsync(_inputFile, _outputFile);

        Assert.True(File.Exists(_outputFile));
        var lines = await File.ReadAllLinesAsync(_outputFile);

        // First line is the header
        Assert.Equal("method,callCount,totalSelfNs,maxSelfNs,totalInclusiveNs,maxInclusiveNs", lines[0]);

        // At least one data row exists
        Assert.True(lines.Length >= 2, "Expected at least one data row after the header.");

        // Columns can be split by comma
        var firstRow = lines[1].Split(',');
        Assert.Equal(6, firstRow.Length);

        // Expected values appear in the correct columns
        Assert.Equal("N.C.DoWork", firstRow[0]);   // method
        Assert.Equal("10", firstRow[1]);            // callCount
        Assert.Equal("5000", firstRow[2]);          // totalSelfNs
        Assert.Equal("800", firstRow[3]);           // maxSelfNs
        Assert.Equal("9000", firstRow[4]);          // totalInclusiveNs
        Assert.Equal("1500", firstRow[5]);          // maxInclusiveNs

        // Verify second row
        var secondRow = lines[2].Split(',');
        Assert.Equal("N.C.Helper", secondRow[0]);
        Assert.Equal("3", secondRow[1]);
    }

    [Fact]
    public async Task CsvExport_EmptyFile_HandlesGracefully()
    {
        await File.WriteAllTextAsync(_inputFile, "");

        var stderrOutput = await TestHelpers.CaptureConsoleErrorAsync(
            () => CsvExporter.ExportAsync(_inputFile, _outputFile));

        // The exporter should report an error to stderr since no recognizable events exist
        Assert.Contains("no enter/leave or method_stats events found", stderrOutput);

        // Output file should not be created
        Assert.False(File.Exists(_outputFile));
    }
}
