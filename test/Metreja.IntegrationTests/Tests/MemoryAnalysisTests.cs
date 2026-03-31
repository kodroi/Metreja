using Metreja.Tool.Analysis;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class MemoryAnalysisTests : IAsyncLifetime
{
    private string _tempFile = "";

    public Task InitializeAsync()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"metreja-memory-test-{Guid.NewGuid():N}.ndjson");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Memory_GcSummary_ShowsGenerationCountsAndPauseTimes()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"gc_start","tsNs":1000,"pid":1,"sessionId":"s1","gen0":true,"gen1":false,"gen2":false,"reason":"alloc_small"}""",
            """{"event":"gc_end","tsNs":2000,"pid":1,"sessionId":"s1","durationNs":500000,"heapSizeBytes":1048576}""",
            """{"event":"gc_start","tsNs":3000,"pid":1,"sessionId":"s1","gen0":true,"gen1":true,"gen2":false,"reason":"alloc_small"}""",
            """{"event":"gc_end","tsNs":4000,"pid":1,"sessionId":"s1","durationNs":1500000,"heapSizeBytes":2097152}""",
            """{"event":"gc_start","tsNs":5000,"pid":1,"sessionId":"s1","gen0":true,"gen1":true,"gen2":true,"reason":"induced"}""",
            """{"event":"gc_end","tsNs":6000,"pid":1,"sessionId":"s1","durationNs":3000000,"heapSizeBytes":3145728}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            MemoryAnalyzer.AnalyzeAsync(_tempFile, top: 10, filters: []));

        Assert.Contains("GC Summary", output);
        Assert.Contains("Gen 0", output);
        Assert.Contains("Gen 1", output);
        Assert.Contains("Gen 2", output);
        Assert.Contains("Total", output);
        // Gen0 count = 3 (all three gc_start events have gen0:true)
        Assert.Contains("3", output);
        // Pause times should appear formatted
        Assert.Contains("Total pause:", output);
        Assert.Contains("Avg pause:", output);
        Assert.Contains("Max pause:", output);
        // Peak heap should appear (3145728 bytes = 3.00 MB)
        Assert.Contains("Peak heap:", output);
        Assert.Contains("Last heap:", output);
    }

    [Fact]
    public async Task Memory_AllocationTable_ShowsClassNamesAndCounts()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"alloc_by_class","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"className":"System.String","count":100}""",
            """{"event":"alloc_by_class","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"className":"System.Byte[]","count":50}""",
            """{"event":"alloc_by_class","tsNs":3000,"pid":1,"sessionId":"s1","tid":1,"className":"System.String","count":25}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            MemoryAnalyzer.AnalyzeAsync(_tempFile, top: 10, filters: []));

        Assert.Contains("Allocations by Class", output);
        Assert.Contains("System.String", output);
        Assert.Contains("System.Byte[]", output);
        // System.String total = 125
        Assert.Contains("125", output);
        // System.Byte[] total = 50
        Assert.Contains("50", output);
        Assert.Contains("Showing top 2 of 2 types", output);
    }

    [Fact]
    public async Task Memory_HeapBreakdown_ShowsGenSizesAndPromotedBytes()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"gc_start","tsNs":1000,"pid":1,"sessionId":"s1","gen0":true,"gen1":false,"gen2":false,"reason":"alloc_small"}""",
            """{"event":"gc_end","tsNs":2000,"pid":1,"sessionId":"s1","durationNs":500000,"heapSizeBytes":4194304}""",
            """{"event":"gc_heap_stats","tsNs":2001,"pid":1,"sessionId":"s1","gen0SizeBytes":524288,"gen0PromotedBytes":65536,"gen1SizeBytes":1048576,"gen1PromotedBytes":131072,"gen2SizeBytes":2097152,"gen2PromotedBytes":0,"lohSizeBytes":524288,"lohPromotedBytes":0,"pohSizeBytes":0,"pohPromotedBytes":0,"finalizationQueueLength":5,"pinnedObjectCount":3}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            MemoryAnalyzer.AnalyzeAsync(_tempFile, top: 10, filters: []));

        // Heap breakdown table headers
        Assert.Contains("Heap", output);
        Assert.Contains("Size", output);
        Assert.Contains("Promoted", output);
        // Gen sizes should appear (512 KB, 1.00 MB, 2.00 MB, 512.00 KB)
        Assert.Contains("Gen 0", output);
        Assert.Contains("Gen 1", output);
        Assert.Contains("Gen 2", output);
        Assert.Contains("LOH", output);
        // Total promoted should appear
        Assert.Contains("Total promoted:", output);
        // Finalization queue and pinned objects
        Assert.Contains("Finalization:", output);
        Assert.Contains("5", output);
        Assert.Contains("Pinned objects:", output);
        Assert.Contains("3", output);
    }

    [Fact]
    public async Task Memory_FilterOption_NarrowsAllocationResults()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"alloc_by_class","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"className":"System.String","count":100}""",
            """{"event":"alloc_by_class","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"className":"System.Byte[]","count":50}""",
            """{"event":"alloc_by_class","tsNs":3000,"pid":1,"sessionId":"s1","tid":1,"className":"MyApp.Models.Customer","count":10}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            MemoryAnalyzer.AnalyzeAsync(_tempFile, top: 10, filters: ["String"]));

        Assert.Contains("System.String", output);
        Assert.DoesNotContain("System.Byte[]", output);
        Assert.DoesNotContain("Customer", output);
        Assert.Contains("Showing top 1 of 1 types", output);
    }

    [Fact]
    public async Task Memory_EmptyTrace_ShowsNoEventsMessages()
    {
        await File.WriteAllTextAsync(_tempFile, "");

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            MemoryAnalyzer.AnalyzeAsync(_tempFile, top: 10, filters: []));

        Assert.Contains("No GC events", output);
        Assert.Contains("No allocation events", output);
    }

    [Fact]
    public async Task Memory_NoGcEvents_ShowsNoGcMessage()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"alloc_by_class","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"className":"System.String","count":100}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            MemoryAnalyzer.AnalyzeAsync(_tempFile, top: 10, filters: []));

        Assert.Contains("No GC events", output);
        Assert.Contains("System.String", output);
    }

    [Fact]
    public async Task Memory_NoAllocationEvents_ShowsNoAllocationMessage()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"gc_start","tsNs":1000,"pid":1,"sessionId":"s1","gen0":true,"gen1":false,"gen2":false,"reason":"alloc_small"}""",
            """{"event":"gc_end","tsNs":2000,"pid":1,"sessionId":"s1","durationNs":500000,"heapSizeBytes":1048576}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            MemoryAnalyzer.AnalyzeAsync(_tempFile, top: 10, filters: []));

        Assert.Contains("GC Summary", output);
        Assert.Contains("No allocation events", output);
    }

    [Fact]
    public async Task Memory_TopParameter_LimitsDisplayedAllocations()
    {
        await File.WriteAllLinesAsync(_tempFile,
        [
            """{"event":"alloc_by_class","tsNs":1000,"pid":1,"sessionId":"s1","tid":1,"className":"System.String","count":100}""",
            """{"event":"alloc_by_class","tsNs":2000,"pid":1,"sessionId":"s1","tid":1,"className":"System.Byte[]","count":50}""",
            """{"event":"alloc_by_class","tsNs":3000,"pid":1,"sessionId":"s1","tid":1,"className":"System.Int32[]","count":25}"""
        ]);

        var output = await TestHelpers.CaptureConsoleOutputAsync(() =>
            MemoryAnalyzer.AnalyzeAsync(_tempFile, top: 2, filters: []));

        Assert.Contains("System.String", output);
        Assert.Contains("System.Byte[]", output);
        Assert.DoesNotContain("System.Int32[]", output);
        Assert.Contains("Showing top 2 of 3 types", output);
    }
}
