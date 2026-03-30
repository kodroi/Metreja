using System.Text.Json;

namespace Metreja.Tool.Analysis;

internal static class JsonOutputOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
