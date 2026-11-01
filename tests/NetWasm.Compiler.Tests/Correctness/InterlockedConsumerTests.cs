namespace NetWasm.Compiler.Tests.Correctness;

public sealed class InterlockedConsumerTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void UnchangedTUnitCounterPreservesCountsAndNotifications()
    {
        using var stream = typeof(InterlockedConsumerTests).Assembly.GetManifestResourceStream(
            "Correctness/HostFixtures/Interlocked/TUnitCounter.cs.txt");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        runner.Run(new("TUnitCounterConsumer", "TUnitCounterConsumer", Source, [42])
        {
            AdditionalSources = [new("TUnitCounter.cs", reader.ReadToEnd())],
            ExpectedReturnValue = 42,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });
    }

    private const string Source = """
        global using System;
        global using System.Collections.Generic;
        global using System.Threading;
        using TUnit.Core.Helpers;
        namespace TUnitCounterConsumer;

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input)
            {
                var counter = new Counter();
                var sum = 0;
                EventHandler<int> good = (_, value) => sum += value;
                EventHandler<int> bad = (_, _) => throw new InvalidOperationException("subscriber");
                counter.OnCountChanged += bad;
                counter.OnCountChanged += good;
                try { counter.Increment(); return 1; }
                catch (AggregateException exception)
                {
                    if (exception.InnerExceptions.Count != 1) return 2;
                }
                if (counter.CurrentCount != 1 || sum != 1) return 3;
                counter.OnCountChanged -= bad;
                if (counter.Add(input) != input + 1 || counter.Decrement() != input) return 4;
                if (counter.CurrentCount != input || sum != 2 * input + 2) return 5;
                counter.OnCountChanged -= good;
                if (counter.Decrement() != input - 1 || sum != 2 * input + 2) return 6;
                try { counter.Add(-input); return 7; }
                catch (InvalidOperationException)
                {
                    if (counter.CurrentCount != -1) return 8;
                }
                return input;
            }
        }
        """;
}
