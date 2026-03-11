using System.CommandLine;
using System.Diagnostics;

namespace Metreja.Tool.Commands;

public static class ReportCommand
{
    private const string Repo = "kodroi/Metreja";

    public static Command Create()
    {
        var titleOption = new Option<string>("--title", "-t")
        {
            Description = "Issue title",
            Required = true
        };

        var descriptionOption = new Option<string>("--description", "-d")
        {
            Description = "Issue body/description",
            Required = true
        };

        var command = new Command("report", "Report an issue to the GitHub repository");
        command.Options.Add(titleOption);
        command.Options.Add(descriptionOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var title = parseResult.GetValue(titleOption)!;
            var description = parseResult.GetValue(descriptionOption)!;

            // 1. Check if gh is installed
            if (!await IsGhInstalled())
            {
                Console.Error.WriteLine(
                    "Error: GitHub CLI (gh) is not installed. Install it from https://cli.github.com/ and ensure it is on your PATH.");
                return 2;
            }

            // 2. Check if gh is authenticated
            if (!await IsGhAuthenticated(cancellationToken))
            {
                Console.Error.WriteLine(
                    "Error: GitHub CLI is not authenticated. Run 'gh auth login' to authenticate.");
                return 3;
            }

            // 3. Create the issue
            var (exitCode, stdout, stderr) = await RunGh(
                $"issue create --repo {Repo} --title \"{EscapeArg(title)}\" --body \"{EscapeArg(description)}\"",
                cancellationToken);

            if (exitCode == 0)
            {
                Console.WriteLine(stdout.Trim());
                return 0;
            }

            Console.Error.WriteLine(stderr.Trim());
            return 1;
        });

        return command;
    }

    private static async Task<bool> IsGhInstalled()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            await process.WaitForExitAsync();
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static async Task<bool> IsGhAuthenticated(CancellationToken cancellationToken)
    {
        var (exitCode, _, _) = await RunGh("auth status", cancellationToken);
        return exitCode == 0;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunGh(
        string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "gh",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, stdout, stderr);
    }

    private static string EscapeArg(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
