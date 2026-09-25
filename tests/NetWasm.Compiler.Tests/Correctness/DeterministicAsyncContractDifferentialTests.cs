namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class DeterministicAsyncContractDifferentialTests(
    CorrectnessTestRunner runner)
{
    [Fact]
    public void ExplicitEventPumpMatchesDesktopAcrossAsyncContracts()
    {
        // The Node oracle simulates GC.Collect. This fixture qualifies async
        // control flow only; rooting across real collection belongs to M5's
        // linked-runtime coverage, not this differential result.
        runner.Run(new(
            "DeterministicAsyncContract",
            "NetWasm.Correctness.DeterministicAsync",
            """
            using System;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;

            namespace NetWasm.Correctness.DeterministicAsync;

            public sealed class Pump
            {
                private readonly Queue<Action> _events = new();
                public int Trace;
                public void Post(Action action) { Trace = unchecked(Trace * 31 + 3); _events.Enqueue(action); }
                public void Drain()
                {
                    while (_events.Count != 0)
                    {
                        var action = _events.Dequeue();
                        Trace = unchecked(Trace * 31 + 5);
                        action();
                    }
                }
            }

            public sealed class ControlledAwaitable
            {
                private readonly Pump _pump;
                private readonly int _value;
                private readonly bool _synchronous;
                private readonly bool _fail;
                public ControlledAwaitable(Pump pump, int value, bool synchronous, bool fail)
                { _pump = pump; _value = value; _synchronous = synchronous; _fail = fail; }
                public Awaiter GetAwaiter() => new(_pump, _value, _synchronous, _fail);

                public sealed class Awaiter : INotifyCompletion
                {
                    private readonly Pump _pump;
                    private readonly int _value;
                    private readonly bool _fail;
                    public Awaiter(Pump pump, int value, bool completed, bool fail)
                    { _pump = pump; _value = value; IsCompleted = completed; _fail = fail; }
                    public bool IsCompleted { get; private set; }
                    public void OnCompleted(Action continuation)
                    {
                        _pump.Post(() => { IsCompleted = true; continuation(); });
                    }
                    public int GetResult()
                    {
                        _pump.Trace = unchecked(_pump.Trace * 31 + 7);
                        if (_fail) throw new InvalidOperationException();
                        return _value;
                    }
                }
            }

            public sealed class Resource : IAsyncDisposable
            {
                private readonly Pump _pump;
                public Resource(Pump pump) { _pump = pump; _pump.Trace += 11; }
                public ValueTask DisposeAsync() { _pump.Trace += 13; return default; }
            }

            public static class EntryPoint
            {
                private static int _trace;
                public static int Trace() => _trace;

                public static int Run(int input)
                {
                    var pump = new Pump { Trace = 1 };
                    Task<int> task;
                    try
                    {
                        if (input == -3) throw new ArgumentException();
                        task = Execute(pump, input);
                    }
                    catch (ArgumentException)
                    {
                        pump.Trace += 17;
                        _trace = pump.Trace;
                        return -30;
                    }
                    GC.Collect();
                    pump.Drain();
                    GC.Collect();
                    try
                    {
                        var result = task.GetAwaiter().GetResult();
                        _trace = pump.Trace;
                        return result;
                    }
                    catch (InvalidOperationException)
                    {
                        pump.Trace += 19;
                        _trace = pump.Trace;
                        return -20;
                    }
                }

                private static async Task<int> Execute(Pump pump, int input)
                {
                    var synchronous = input == 0;
                    var fail = input == -2;
                    var total = 0;
                    try
                    {
                        total += await Nested(pump, input, synchronous, fail);
                        total += await new ControlledAwaitable(pump, input + 2, false, false);
                        await using var resource = new Resource(pump);
                        await foreach (var value in Values(pump, input)) total += value;
                    }
                    catch (InvalidOperationException)
                    {
                        pump.Trace += 23;
                        throw;
                    }
                    finally
                    {
                        pump.Trace += 29;
                    }
                    return total;
                }

                private static async ValueTask<int> Nested(
                    Pump pump, int input, bool synchronous, bool fail)
                {
                    var value = await new ControlledAwaitable(
                        pump, input + 1, synchronous, fail);
                    return value;
                }

                private static async IAsyncEnumerable<int> Values(Pump pump, int input)
                {
                    yield return input;
                    await new ControlledAwaitable(pump, 0, false, false);
                    yield return input + 3;
                }
            }
            """,
            [-3, -2, -1, 0, 2])
        {
            CaptureCompilerDiagnostics = true,
            RequiresReactor = true,
        });
    }
}
