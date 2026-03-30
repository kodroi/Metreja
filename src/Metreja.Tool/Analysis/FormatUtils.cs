using System.Globalization;

namespace Metreja.Tool.Analysis;

internal static class FormatUtils
{
    public static string FormatNs(long ns)
    {
        return ns switch
        {
            < 1_000 => $"{ns}ns",
            < 1_000_000 => string.Format(CultureInfo.InvariantCulture, "{0:F2}us", ns / 1_000.0),
            < 1_000_000_000 => string.Format(CultureInfo.InvariantCulture, "{0:F2}ms", ns / 1_000_000.0),
            _ => string.Format(CultureInfo.InvariantCulture, "{0:F2}s", ns / 1_000_000_000.0)
        };
    }

    public static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : string.Concat("...", value.AsSpan(value.Length - maxLength + 3));
    }
}
