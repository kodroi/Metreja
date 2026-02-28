using System.Text.Json.Serialization;

namespace Metreja.Cli.Config;

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

    [JsonPropertyName("runId")]
    public string RunId { get; init; } = "";
}

public record InstrumentationConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "elt3";

    [JsonPropertyName("maxEvents")]
    public int MaxEvents { get; init; } = 0;

    [JsonPropertyName("computeDeltas")]
    public bool ComputeDeltas { get; init; } = true;

    [JsonPropertyName("trackMemory")]
    public bool TrackMemory { get; init; } = false;

    [JsonPropertyName("includes")]
    public List<FilterRule> Includes { get; init; } = [];

    [JsonPropertyName("excludes")]
    public List<FilterRule> Excludes { get; init; } = [];
}

public record FilterRule
{
    [JsonPropertyName("assembly")]
    public string Assembly { get; init; } = "*";

    [JsonPropertyName("namespace")]
    public string Namespace { get; init; } = "*";

    [JsonPropertyName("class")]
    public string Class { get; init; } = "*";

    [JsonPropertyName("method")]
    public string Method { get; init; } = "*";

    [JsonPropertyName("logLines")]
    public bool LogLines { get; init; } = false;
}

public record OutputConfig
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = ".metreja/output/{runId}_{pid}.ndjson";

    [JsonPropertyName("format")]
    public string Format { get; init; } = "ndjson";
}
