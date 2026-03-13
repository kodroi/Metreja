using System.Diagnostics;

namespace Metreja.TestApp;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine($"PID: {Environment.ProcessId}");
        Console.WriteLine("Metreja Test Application");
        Console.WriteLine("========================");

        RunSyncCallPaths();
        await RunAsyncCallPathsAsync();
        RunExceptionPaths();
        RunSelfCatchExceptionPaths();
        RunRecursiveCatchExceptionPaths();
        RunTightLoop();
        RunDeepRecursion(20);

        Console.WriteLine();
        Console.WriteLine("All tests completed.");

        if (args.Contains("--wait"))
        {
            Console.WriteLine("READY");
            Console.ReadLine();
        }
    }

    private static void RunSyncCallPaths()
    {
        Console.WriteLine();
        Console.WriteLine("[Sync Call Paths]");
        var result = OuterMethod();
        Console.WriteLine($"  Result: {result}");
    }

    private static int OuterMethod()
    {
        return MiddleMethod(10);
    }

    private static int MiddleMethod(int value)
    {
        return InnerMethod(value * 2);
    }

    private static int InnerMethod(int value)
    {
        return value + 1;
    }

    private static async Task RunAsyncCallPathsAsync()
    {
        Console.WriteLine();
        Console.WriteLine("[Async Call Paths]");
        var result = await ComputeAsync(5);
        Console.WriteLine($"  Async result: {result}");
    }

    private static async Task<int> ComputeAsync(int input)
    {
        var step1 = await StepOneAsync(input);
        var step2 = await StepTwoAsync(step1);
        return step2;
    }

    private static async Task<int> StepOneAsync(int value)
    {
        await Task.Delay(1);
        return value * 3;
    }

    private static async Task<int> StepTwoAsync(int value)
    {
        await Task.Delay(1);
        return value + 7;
    }

    private static void RunExceptionPaths()
    {
        Console.WriteLine();
        Console.WriteLine("[Exception Paths]");
        try
        {
            ThrowingMethod();
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"  Caught: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            NestedExceptionMethod();
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"  Caught: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ThrowingMethod()
    {
        throw new InvalidOperationException("Test exception");
    }

    private static void NestedExceptionMethod()
    {
        WrapperMethod();
    }

    private static void WrapperMethod()
    {
        throw new ArgumentException("Nested test exception");
    }

    /// <summary>
    /// Tests exception caught in the same method that throws (self-catch).
    /// This exercises the ExceptionUnwindFunctionEnter → LeaveStub path
    /// that previously caused double-pop stack misalignment.
    /// </summary>
    private static void RunSelfCatchExceptionPaths()
    {
        Console.WriteLine();
        Console.WriteLine("[Self-Catch Exception Paths]");
        var result = SelfCatchParent();
        Console.WriteLine($"  Result: {result}");
    }

    private static int SelfCatchParent()
    {
        return SelfCatchChild(10);
    }

    private static int SelfCatchChild(int value)
    {
        // Exception thrown and caught in the same method
        try
        {
            if (value > 0)
                throw new InvalidOperationException("self-catch test");
        }
        catch (InvalidOperationException)
        {
            // Caught in same method — exercises the catcher-skip path
        }

        return value + 1;
    }

    private static void RunTightLoop()
    {
        Console.WriteLine();
        Console.WriteLine("[Tight Loop - 1000 iterations]");
        var sw = Stopwatch.StartNew();
        var sum = 0;
        for (var i = 0; i < 1000; i++)
        {
            sum += LoopBody(i);
        }
        sw.Stop();
        Console.WriteLine($"  Sum: {sum}, Elapsed: {sw.ElapsedMilliseconds}ms");
    }

    private static int LoopBody(int i)
    {
        return (i * 2) + 1;
    }

    private static void RunDeepRecursion(int depth)
    {
        Console.WriteLine();
        Console.WriteLine($"[Deep Recursion - depth {depth}]");
        var result = Recurse(depth);
        Console.WriteLine($"  Result: {result}");
    }

    private static int Recurse(int n)
    {
        if (n <= 0) return 1;
        return n + Recurse(n - 1);
    }

    /// <summary>
    /// Tests recursive method where an outer activation catches an exception
    /// thrown by an inner activation. This exercises the frame-depth guard:
    /// ExceptionUnwindFunctionEnter must pop the inner activation (same FunctionID
    /// as the catcher) and preserve only the catching activation.
    /// </summary>
    private static void RunRecursiveCatchExceptionPaths()
    {
        Console.WriteLine();
        Console.WriteLine("[Recursive Catch Exception Paths]");
        var result = RecursiveCatchOuter();
        Console.WriteLine($"  Result: {result}");
    }

    private static int RecursiveCatchOuter()
    {
        return RecursiveCatch(3);
    }

    private static int RecursiveCatch(int depth)
    {
        if (depth <= 0)
            throw new InvalidOperationException("recursive-catch bottom");

        try
        {
            return RecursiveCatch(depth - 1);
        }
        catch (InvalidOperationException)
        {
            // Only depth=1 catches (depth=0 throws, unwinds through depth=0)
            return depth;
        }
    }
}
