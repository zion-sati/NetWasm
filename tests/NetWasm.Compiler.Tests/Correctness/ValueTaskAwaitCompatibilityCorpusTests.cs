// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// Awaits of value tasks that are not calls of lowered methods: constructed in place, stored, configured, returned by
// calls and passed to generic methods, completed or pending on an IValueTaskSource that the program completes later,
// over int, string, tuple and DateTime results and the untyped value task. A pending source's result is taken once,
// with its token, after the await resumes; locals stay live across each suspension; a faulted source's exception
// reaches the awaiting method. Desktop .NET's state machines run the same source, at every preset: the optimizer splits
// an awaited value task into a local per field, saved into and restored from the suspended frame field by field.
public sealed class ValueTaskAwaitCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void ValueTaskAwaitsMatchDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "ValueTaskAwaitCompatibilityCorpus" + optimization,
            "NetWasm.Correctness.ValueTaskAwaits",
            """
            #nullable enable
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using System.Threading.Tasks.Sources;

            namespace NetWasm.Correctness.ValueTaskAwaits;

            // A source the program completes, which counts the results taken and the tokens they were taken with.
            public sealed class Source<T> : IValueTaskSource<T>, IValueTaskSource
            {
                private ManualResetValueTaskSourceCore<T> _core;
                public int Taken;
                public int WrongTokens;

                public short Version => _core.Version;
                public void Reset() => _core.Reset();
                public void SetResult(T value) => _core.SetResult(value);
                public void SetException(Exception error) => _core.SetException(error);

                public T GetResult(short token)
                {
                    Taken++;
                    if (token != _core.Version) WrongTokens++;
                    return _core.GetResult(token);
                }

                void IValueTaskSource.GetResult(short token) => GetResult(token);

                public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

                public void OnCompleted(Action<object?> continuation, object? state, short token,
                    ValueTaskSourceOnCompletedFlags flags) => _core.OnCompleted(continuation, state, token, flags);
            }

            public static class EntryPoint
            {
                private static int _hash;

                private static void Mix(int value) => _hash = unchecked((_hash ^ value) * 16777619);

                private static void Mix(string? text)
                {
                    if (text is null)
                    {
                        Mix(-1);
                        return;
                    }
                    Mix(text.Length);
                    foreach (var c in text) Mix(c);
                }

                private static async Task<int> CompletedInts(int input)
                {
                    var sum = await new ValueTask<int>(input);
                    sum += await new ValueTask<int>(input + 1).ConfigureAwait(false);
                    ValueTask<int> stored = new ValueTask<int>(input * 3);
                    sum += await stored;
                    sum += await new ValueTask<int>(Task.FromResult(input + 7));
                    await new ValueTask();
                    await default(ValueTask);
                    await new ValueTask(Task.CompletedTask).ConfigureAwait(false);
                    return sum;
                }

                private static async Task<string> CompletedStrings(int input)
                {
                    var text = await new ValueTask<string>("a" + input);
                    text += await new ValueTask<string>(Task.FromResult("b"));
                    text += await new ValueTask<string>("c").ConfigureAwait(true);
                    return text;
                }

                private static async Task<long> CompletedStructs(int input)
                {
                    var pair = await new ValueTask<(int Small, long Large)>((input, input * 1000L));
                    var date = await new ValueTask<DateTime>(new DateTime(2020, 1, 1).AddDays(input));
                    return pair.Small + pair.Large + date.DayOfYear;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static ValueTask<int> Produce(int value) => new(value * 5);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static ValueTask<string> ProduceText(int value) => new("p" + value);

                private static async Task<string> FromCalls(int input) =>
                    await ProduceText(input) + await Produce(input) + await Produce(input + 1).ConfigureAwait(false);

                private static async Task<T> Awaited<T>(ValueTask<T> task) => await task;

                // Three rounds over three pending sources: each await suspends until the program completes its source,
                // then takes the result with the token it was created with.
                private static async Task<string> FromSources(Source<string> text, Source<int> number, Source<int> plain,
                    int rounds)
                {
                    var result = "";
                    for (var round = 0; round < rounds; round++)
                    {
                        var label = "r" + round;
                        var value = await new ValueTask<string>(text, text.Version);
                        var count = await new ValueTask<int>(number, number.Version).ConfigureAwait(false);
                        await new ValueTask(plain, plain.Version);
                        result += label + value + count + ";";
                    }
                    return result;
                }

                private static async Task<int> Faulted(Source<int> number)
                {
                    try
                    {
                        return await new ValueTask<int>(number, number.Version);
                    }
                    catch (InvalidOperationException error)
                    {
                        return error.Message.Length;
                    }
                }

                public static int Run(int input)
                {
                    _hash = unchecked((int)2166136261);
                    Mix(CompletedInts(input).GetAwaiter().GetResult());
                    Mix(CompletedStrings(input).GetAwaiter().GetResult());
                    Mix((int)CompletedStructs(input).GetAwaiter().GetResult());
                    Mix(FromCalls(input).GetAwaiter().GetResult());
                    Mix(Awaited(new ValueTask<string>("g" + input)).GetAwaiter().GetResult());
                    Mix(Awaited(new ValueTask<int>(Task.FromResult(input))).GetAwaiter().GetResult());

                    var text = new Source<string>();
                    var number = new Source<int>();
                    var plain = new Source<int>();
                    var pending = FromSources(text, number, plain, 3);
                    for (var round = 0; round < 3; round++)
                    {
                        Mix(pending.IsCompleted ? 1 : 0);
                        text.SetResult("t" + round + input);
                        text.Reset();
                        Mix(pending.IsCompleted ? 1 : 0);
                        number.SetResult(round * 10 + input);
                        number.Reset();
                        plain.SetResult(0);
                        plain.Reset();
                    }
                    Mix(pending.IsCompleted ? 1 : 0);
                    Mix(pending.GetAwaiter().GetResult());
                    Mix(text.Taken);
                    Mix(number.Taken);
                    Mix(plain.Taken);
                    Mix(text.WrongTokens + number.WrongTokens + plain.WrongTokens);

                    var failing = new Source<int>();
                    var faulted = Faulted(failing);
                    Mix(faulted.IsCompleted ? 1 : 0);
                    failing.SetException(new InvalidOperationException("boom" + input));
                    Mix(faulted.GetAwaiter().GetResult());
                    Mix(failing.Taken);
                    return _hash;
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 7])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = optimization != FinalWasmOptimization.None,
        });
}
