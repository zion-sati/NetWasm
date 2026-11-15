// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AggregateArgumentOwnershipCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void MutableParametersPreserveCallerValuesWhileTrueReferencesRemainAliases(
        bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"AggregateArgumentOwnership_{optimize}_{target}",
            Source, "EntryPoint", optimize, target, 41, []));

        Assert.Equal(42, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void FilterCapturedValueLoadsAreSnapshotsBeforeLaterArgumentEvaluation(
        bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"FilterCaptureArgumentOwnership_{optimize}_{target}",
            """
            using System;

            public struct Value
            {
                public long Number;
                public long Tag;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = new Value { Number = input, Tag = 7 };
                    try
                    {
                        throw new InvalidOperationException();
                    }
                    catch (InvalidOperationException) when (value.Tag == 7)
                    {
                        var before = Consume(value, Mutate(ref value), input);
                        if (!before || value.Number != input + 1) return 1;
                        return 42;
                    }
                }

                private static long Mutate(ref Value value) => ++value.Number;
                private static bool Consume(Value snapshot, long current, int input) =>
                    snapshot.Number == input && snapshot.Tag == 7 && current == input + 1;
            }
            """,
            "EntryPoint", optimize, target, 41, []));

        Assert.Equal(42, observed);
    }

    private const string Source = """
        using System;

        public sealed class Item(int number)
        {
            public int Number = number;
        }

        public struct Value
        {
            public long First;
            public long Second;
            public Item Item;
            public byte Tag;

            public void Increment() => First++;
        }

        public struct Envelope<T>
        {
            public T Item;
            public int Count;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var item = new Item(input);
                var value = new Value { First = input, Second = input + 1, Item = item, Tag = 3 };
                if (Mutate(value, value, input) != 0) return 1;
                if (!Unchanged(value, item, input)) return 2;
                var other = new Value { First = 17, Second = 19, Item = new Item(23), Tag = 29 };
                var replaced = Replace(value, other);
                if (!Unchanged(value, item, input)) return 3;
                if (replaced.First != 17 || replaced.Item.Number != 23) return 4;

                var generic = new Envelope<Item> { Item = item, Count = input };
                var genericOther = new Envelope<Item> { Item = other.Item, Count = 7 };
                var genericResult = ReplaceGeneric(generic, genericOther);
                if (generic.Item != item || generic.Count != input) return 5;
                if (genericResult.Item != other.Item || genericResult.Count != 7) return 6;
                if (MutateThroughFilter(value, input) != input + 11) return 7;
                if (!Unchanged(value, item, input)) return 8;
                for (var index = 0; index < 4; index++)
                {
                    if (Mutate(value, value, input) != 0) return 9;
                    if (!Unchanged(value, item, input)) return 10;
                }

                value.Increment();
                if (value.First != input + 1) return 11;
                MutateByReference(ref value);
                if (value.First != input + 2) return 12;
                return 42;
            }

            private static bool Unchanged(Value value, Item item, int input) =>
                value.First == input && value.Second == input + 1 &&
                value.Item == item && value.Tag == 3;

            private static int Mutate(Value left, Value right, int input)
            {
                left.First += 7;
                if (right.First != input) return 1;
                right.Second += 9;
                if (left.Second != input + 1) return 2;
                left.Item = new Item(input + 2);
                if (right.Item.Number != input) return 3;
                right.Tag = 5;
                if (left.Tag != 3) return 4;
                return 0;
            }

            private static Value Replace(Value left, Value right)
            {
                left = right;
                return left;
            }

            private static Envelope<T> ReplaceGeneric<T>(Envelope<T> left, Envelope<T> right)
            {
                left = right;
                return left;
            }

            private static long MutateThroughFilter(Value value, int input)
            {
                value.First += 7;
                try
                {
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException) when (value.First == input + 7 && value.Item.Number == input)
                {
                    value.First += 4;
                    return value.First;
                }
            }

            private static void MutateByReference(ref Value value) => value.First++;
        }
        """;
}
