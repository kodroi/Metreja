using System.Text.Json.Serialization;

namespace Metreja.Tool.Config;

public record ProfilerConfig
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = "";

    [JsonPropertyName("metadata")]
    public MetadataConfig Metadata { get; init; } = new();

    [JsonPropertyName("instrumentation")]
    public InstrumentationConfig Instrumentation { get; init; } = new();

    [JsonPropertyName("output")]
    public OutputConfig Output { get; init; } = new();
}

public record MetadataConfig
{
    [JsonPropertyName("scenario")]
    public string Scenario { get; init; } = "";
}

public record InstrumentationConfig
{
    [JsonPropertyName("maxEvents")]
    public int MaxEvents { get; init; } = 0;

    [JsonPropertyName("computeDeltas")]
    public bool ComputeDeltas { get; init; } = true;

    [JsonPropertyName("trackMemory")]
    public bool TrackMemory { get; init; } = false;

    [JsonPropertyName("disableInlining")]
    public bool DisableInlining { get; init; } = true;

    [JsonPropertyName("includes")]
    public List<FilterRule> Includes { get; init; } = [];

    [JsonPropertyName("excludes")]
    public List<FilterRule> Excludes { get; init; } = [];
}

public record FilterRule
{
    [JsonPropertyName("level")]
    public string Level { get; init; } = "assembly";

    [JsonPropertyName("pattern")]
    public string Pattern { get; init; } = "*";
}

public static class DefaultFilters
{
    public static List<FilterRule> Excludes =>
    [
        new FilterRule { Level = "assembly", Pattern = "System.*" },
        new FilterRule { Level = "assembly", Pattern = "Microsoft.*" }
    ];
}

public record OutputConfig
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = ".metreja/output/{sessionId}_{pid}.ndjson";
}
