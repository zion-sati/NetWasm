namespace NetWasm.Compiler.Tests.Correctness;

// Issue 144 and its class. The runtime allocates a multicast delegate's clone,
// an array's clone or range, the boxes an array copy into object[] makes, a
// boxed value's clone and an uncached type object. A call to any of these must
// publish the references its caller holds, or a collection while the runtime
// allocates frees them: a multicast delegate's new invocation list was freed
// that way, and its memory's next object later failed a virtual call. With a
// collection before every allocation, each call here finds what it was given
// and what its caller holds intact.
public sealed class RuntimeCloneRootsGcTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void RuntimeClonesKeepTheirCallersReferencesAcrossCollections() =>
        runner.Run(new("RuntimeCloneRoots", "RuntimeCloneRoots", Source, [42])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            CollectsAtEveryAllocation = true,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace RuntimeCloneRoots;

        public sealed class Box
        {
            public int Value;
        }

        public sealed class Adder
        {
            public static int Total;
            public int Amount;
            public void Add() => Total += Amount;
        }

        public struct Pair
        {
            public string First;
            public int Second;
        }

        public static class EntryPoint
        {
            // Holds the probe, so its allocation stays one.
            public static Box? Probe;

            public static int Trace() => 0;

            // Each value comes from a call, so only the call it is passed to
            // holds it.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Box[] Boxes(int input)
            {
                var boxes = new Box[16];
                for (var index = 0; index < boxes.Length; index++) boxes[index] = new Box { Value = input + index };
                return boxes;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object[] Strings(int input)
            {
                var strings = new string[8];
                for (var index = 0; index < strings.Length; index++) strings[index] = new string((char)('a' + index), input % 5 + 1);
                return strings;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int[] Integers(int input)
            {
                var integers = new int[16];
                for (var index = 0; index < integers.Length; index++) integers[index] = input * index;
                return integers;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object BoxedPair(int input) => new Pair { First = new string('p', input % 7 + 1), Second = input };

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static string Marker(int input) => new string('m', input % 3 + 2);

            // Combining clones the delegate as its invocation list grows, and
            // removing clones it with a new list.
            private static int Delegates(int input)
            {
                var parts = new Action[40];
                Action? handler = null;
                for (var index = 0; index < parts.Length; index++)
                {
                    parts[index] = new Adder { Amount = input + index }.Add;
                    handler += parts[index];
                }
                Adder.Total = 0;
                handler!();
                var all = Adder.Total;
                for (var index = 0; index < parts.Length; index += 2) handler -= parts[index];
                Adder.Total = 0;
                handler!();
                return all == 40 * input + 780 && Adder.Total == 20 * input + 400 ? 0 : 2;
            }

            private static int Clone(int input)
            {
                var marker = Marker(input);
                var copy = (Box[])Boxes(input).Clone();
                var sum = 0;
                foreach (var box in copy) sum += box.Value;
                return sum == 16 * input + 120 && marker.Length == input % 3 + 2 ? 0 : 3;
            }

            private static int Range(int input)
            {
                var marker = Marker(input);
                var slice = Strings(input)[2..6];
                var text = string.Concat((string[])slice);
                return slice.Length == 4 && text.Length == 4 * (input % 5 + 1) && text[0] == 'c' && text[^1] == 'f' &&
                    marker.Length == input % 3 + 2 ? 0 : 4;
            }

            private static int Copy(int input)
            {
                var marker = Marker(input);
                var objects = new object[16];
                Array.Copy(Integers(input), objects, objects.Length);
                var sum = 0;
                foreach (var value in objects) sum += (int)value;
                return sum == 120 * input && marker.Length == input % 3 + 2 ? 0 : 5;
            }

            private static int BoxedClone(int input)
            {
                var marker = Marker(input);
                var copy = (Pair)RuntimeHelpers.GetObjectValue(BoxedPair(input))!;
                return copy.Second == input && copy.First.Length == input % 7 + 1 && copy.First[0] == 'p' &&
                    marker.Length == input % 3 + 2 ? 0 : 6;
            }

            private static int UnderlyingType(int input)
            {
                var marker = Marker(input);
                var underlying = Nullable.GetUnderlyingType(typeof(int?));
                return underlying == typeof(int) && marker.Length == input % 3 + 2 ? 0 : 7;
            }

            public static int Run(int input)
            {
                // On NetWasm, which compiles ahead of time, the checks below run
                // with a collection at every allocation, or they would pass by
                // luck; desktop .NET only computes the expected value.
                // A collection needs something to collect, so the probe is the
                // second object.
                Probe = new Box();
                var collections = GC.CollectionCount(0);
                Probe = new Box { Value = input };
                if (!RuntimeFeature.IsDynamicCodeSupported && GC.CollectionCount(0) == collections || Probe.Value != input) return 1;
                var failure = Delegates(input);
                if (failure == 0) failure = Clone(input);
                if (failure == 0) failure = Range(input);
                if (failure == 0) failure = Copy(input);
                if (failure == 0) failure = BoxedClone(input);
                if (failure == 0) failure = UnderlyingType(input);
                return failure == 0 ? input : failure;
            }
        }
        """;
}
