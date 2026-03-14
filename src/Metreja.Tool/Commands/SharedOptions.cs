using System.CommandLine;

namespace Metreja.Tool.Commands;

public static class SharedOptions
{
    public static Option<string> SessionOption(bool required = true) => new("--session", "-s")
    {
        Description = "Session ID",
        Required = required
    };
}
