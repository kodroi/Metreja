namespace Metreja.IntegrationTests.Tests;

internal static class TestHelpers
{
    private static readonly SemaphoreSlim ConsoleGate = new(1, 1);

    public static async Task<string> CaptureConsoleOutputAsync(Func<Task> action)
    {
        await ConsoleGate.WaitAsync();
        var originalOut = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            await action();
            return sw.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
            ConsoleGate.Release();
        }
    }
}
