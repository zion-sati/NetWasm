namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class CleanupOutcomeDifferentialTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void SuccessFailureAndCancellationCleanupMatchDesktopDotNet()
    {
        runner.Run(new(
            "CleanupOutcomeDifferential",
            "NetWasm.Correctness.CleanupOutcomes",
            """
            using System;
            using System.Threading.Tasks;

            namespace NetWasm.Correctness.CleanupOutcomes;

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    _trace = 1;
                    try
                    {
                        return Execute(input).GetAwaiter().GetResult();
                    }
                    catch (InvalidOperationException)
                    {
                        _trace = unchecked(_trace * 19 + 103);
                        return 11;
                    }
                    catch (OperationCanceledException)
                    {
                        _trace = unchecked(_trace * 23 + 107);
                        return 13;
                    }
                }

                public static int Trace() => _trace;

                private static async Task<int> Execute(int input)
                {
                    try
                    {
                        await Task.CompletedTask;
                        if (input == 1)
                        {
                            throw new InvalidOperationException();
                        }
                        if (input == 2)
                        {
                            var canceled = new TaskCompletionSource<int>();
                            canceled.SetCanceled();
                            return await canceled.Task;
                        }
                        return input + 7;
                    }
                    finally
                    {
                        _trace = unchecked(_trace * 17 + 101);
                    }
                }
            }
            """,
            [0, 1, 2])
        { RequiresReactor = true });
    }
}
