using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ArrayStoreCheckCompatibilityTests(CorrectnessTestRunner runner)
{
    // Stores into arrays with an exact element type skip the covariant check;
    // every other reference store must still throw ArrayTypeMismatchException
    // exactly where desktop .NET does.
    [Theory]
    [InlineData(FinalWasmOptimization.O3, "Release-Wasm32-Direct-Linked")]
    [InlineData(FinalWasmOptimization.O3, "Release-Wasm64-Direct-Linked")]
    [InlineData(FinalWasmOptimization.Oz, "Release-Wasm32-Direct-Linked")]
    public void ArrayStoreChecksMatchDesktop(FinalWasmOptimization optimization, string cell) =>
        runner.Run(new("ArrayStoreChecks", "ArrayStoreChecks", Source, [0, 1, 5])
        {
            Optimization = optimization,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        namespace ArrayStoreChecks;

        public class Animal { public int Legs; }
        public class Dog : Animal { }
        public sealed class Leaf { public int Value; }
        public sealed class Box<T> { public T Item = default!; }

        public static class EntryPoint
        {
            private static Leaf[] s_leaves = new Leaf[4];
            private static Animal[] s_animals = new Dog[4];

            public static int Trace() => 0;

            public static int Run(int seed)
            {
                var total = Exact(seed) + Covariant(seed) + Generic(seed);
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Exact(int seed)
            {
                var leaves = new Leaf[seed + 2];
                for (var index = 0; index < leaves.Length; index++) leaves[index] = new Leaf { Value = index + seed };
                leaves[0] = null!;
                var strings = new[] { "a", "bb", seed.ToString() };
                strings[1] = "ccc";
                s_leaves[seed % 4] = leaves[^1];
                var boxes = new Box<int>[2];
                boxes[1] = new Box<int> { Item = seed };
                var holder = new Holder(seed);
                holder.Leaves[seed % 3] = new Leaf { Value = 7 };
                Fill(leaves, new Leaf { Value = 11 });
                // The array stays on the stack across the branches of each value.
                for (var index = 0; index < leaves.Length; index++)
                    leaves[index] = index % 3 == 0 ? leaves[index] : new Leaf { Value = seed > index ? index : seed };
                var list = new List<Leaf>();
                for (var index = 0; index < 5 + seed; index++) list.Add(new Leaf { Value = index });
                var sum = 0;
                foreach (var leaf in leaves) sum += leaf?.Value ?? 100;
                foreach (var leaf in list) sum += leaf.Value;
                return sum + strings[1].Length + strings[2].Length + boxes[1].Item + s_leaves[seed % 4].Value +
                    holder.Leaves[seed % 3].Value;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Fill(Leaf[] leaves, Leaf leaf)
            {
                for (var index = 1; index < leaves.Length; index += 2) leaves[index] = leaf;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Covariant(int seed)
            {
                var result = 0;
                object[] objects = seed == 1 ? new object[2] : new string[2];
                try { objects[0] = "text"; result += 1; } catch (ArrayTypeMismatchException) { result += 10; }
                try { objects[1] = seed; result += 100; } catch (ArrayTypeMismatchException) { result += 1000; }
                try { s_animals[0] = new Dog(); result += 10_000; } catch (ArrayTypeMismatchException) { result += 20_000; }
                try { s_animals[1] = new Animal(); result += 40_000; } catch (ArrayTypeMismatchException) { result += 80_000; }
                Animal[] animals = seed == 5 ? new Animal[1] : new Dog[1];
                try { Store(animals, new Animal()); result += 160_000; } catch (ArrayTypeMismatchException) { result += 320_000; }
                try { animals[0] = null!; result += 640_000; } catch (ArrayTypeMismatchException) { result += 1_280_000; }
                return result;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Store<T>(T[] values, T value) => values[0] = value;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Generic(int seed)
            {
                var leaves = new Leaf[2];
                Store(leaves, new Leaf { Value = seed });
                var names = new string[1];
                Store(names, "name");
                object[] mixed = seed == 0 ? new Leaf[1] : new object[1];
                try { Store(mixed, (object)"wrong"); return leaves[0].Value + names[0].Length + 1; }
                catch (ArrayTypeMismatchException) { return leaves[0].Value + names[0].Length + 2; }
            }

            private sealed class Holder(int seed)
            {
                public readonly Leaf[] Leaves = new Leaf[3 + seed];
            }
        }
        """;
}
