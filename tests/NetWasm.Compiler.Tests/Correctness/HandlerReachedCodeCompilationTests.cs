// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// Code the analysis of static types reaches only through an exception handler: a filter's handler and what follows
// it when the protected code always throws, a finally whose protected code cannot throw, and handlers nested in
// them. A call there that needs its receiver's static type (DynamicInvoke on a delegate type, Array.Copy between
// arrays of known types) behaves as anywhere else.
public sealed class HandlerReachedCodeCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void CallsThatNeedStaticTypesBehaveAsDesktopDoesAfterEveryKindOfHandler() =>
        runner.Run(new("HandlerReachedCode", "HandlerReachedCode", Source, [0, 1, 2, 5])
        {
            ReportAllMismatches = true,
        });

    [Theory]
    [InlineData("Release-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm64-Direct-Linked")]
    public void CallsThatNeedStaticTypesRunOnTheLinkedRuntime(string cell) =>
        runner.Run(new("HandlerReachedCode", "HandlerReachedCode", Source, [0, 1, 2, 5])
        {
            ReportAllMismatches = true,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        namespace HandlerReachedCode;

        public delegate int Step(int value);

        public sealed class Item
        {
            public int Value;
        }

        public static class EntryPoint
        {
            private static int _trace;
            public static int Trace() => _trace;

            public static int Run(int input)
            {
                var hash = 17;
                hash = Mix(hash, AfterAFilterWhoseTryThrows(input));
                hash = Mix(hash, InAFiltersHandler(input));
                hash = Mix(hash, InAFinallyWhoseTryCannotThrow(input));
                hash = Mix(hash, AfterNestedFilters(input));
                hash = Mix(hash, InACatchAfterAFilter(input));
                _trace = hash;
                return hash;
            }

            private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

            private static int Invoke(int input)
            {
                Step step = value => value * 3 + 1;
                return (int)step.DynamicInvoke(input)!;
            }

            private static int Copy(int input)
            {
                var source = new Item[] { new() { Value = input }, new() { Value = input + 1 } };
                var destination = new Item[3];
                Array.Copy(source, 0, destination, 1, 2);
                return destination[1].Value * 10 + destination[2].Value + (destination[0] is null ? 100 : 0);
            }

            private static int AfterAFilterWhoseTryThrows(int input)
            {
                var seen = 0;
                try
                {
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException) when (input >= 0)
                {
                    seen = 1;
                }
                Step step = value => value + seen;
                return (int)step.DynamicInvoke(input)! + Copy(input);
            }

            private static int InAFiltersHandler(int input)
            {
                try
                {
                    throw new ArgumentException("value");
                }
                catch (ArgumentException exception) when (exception.Message.Length > input % 3)
                {
                    return Invoke(input) + Copy(input + 2);
                }
                catch (ArgumentException)
                {
                    return -Invoke(input);
                }
            }

            private static int InAFinallyWhoseTryCannotThrow(int input)
            {
                var result = 0;
                try
                {
                    result = input + 4;
                }
                finally
                {
                    Step step = value => value - 2;
                    result += (int)step.DynamicInvoke(result)! + Copy(result);
                }
                return result;
            }

            private static int AfterNestedFilters(int input)
            {
                var depth = 0;
                try
                {
                    try
                    {
                        throw new FormatException();
                    }
                    catch (FormatException) when (++depth > 0)
                    {
                        throw new InvalidOperationException();
                    }
                }
                catch (InvalidOperationException) when (++depth > 1)
                {
                    depth *= 10;
                }
                return Invoke(input + depth) + Copy(depth);
            }

            private static int InACatchAfterAFilter(int input)
            {
                try
                {
                    try
                    {
                        throw new NotSupportedException();
                    }
                    catch (NotSupportedException) when (input > 100)
                    {
                        return -1;
                    }
                }
                catch (NotSupportedException)
                {
                    return Invoke(input) * 7 + Copy(input + 1);
                }
            }
        }
        """;
}
