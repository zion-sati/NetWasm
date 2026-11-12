namespace NetWasm.Compiler.Tests.Correctness;

// A callee publishes the byrefs it receives, never what they address: a
// collection traces that in place, in its object or in its frame's reference
// map. Each case has a callee store a fresh string through a byref into one
// kind of storage, then allocate one of the same size, which reuses the
// first's memory if a collection freed it, before the caller reads it back.
// With a collection before every allocation, a storage kind that its frame's
// map leaves out reads back the second string.
public sealed class ByRefTargetRootsGcTests(CorrectnessTestRunner runner)
{
    public static TheoryData<string> Cases() =>
    [
        "ConstructedValue", "ConstructedValueStoredInField", "LaneSplitLocal", "LaneSplitArgument",
        "FilterCapturedLocal", "FilterCapturedArgument", "ValueReceiver", "Local", "ArrayElement", "BoxedValue",
        "StaticField", "ReturnedValue",
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void WhatACalleeStoresThroughAByrefSurvivesCollections(string name) =>
        runner.Run(new("ByRefTargetRoots" + name, "ByRefTargetRoots", Source.Replace("CASE", name, StringComparison.Ordinal), [42])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            CollectsAtEveryAllocation = true,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace ByRefTargetRoots;

        public struct Slot
        {
            public object Value;
            public int Count;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public Slot(int length)
            {
                Value = Cases.Text('c', length);
                Count = Cases.Text('d', length).Length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public int Refill(int length)
            {
                Value = Cases.Text('r', length);
                return Cases.Text('s', length).Length;
            }
        }

        public sealed class Holder
        {
            public Slot Field;
        }

        public static class Cases
        {
            private static Slot shared;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static string Text(char value, int length) => new(value, length);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Put(ref Slot slot, int length)
            {
                slot.Value = Text('p', length);
                slot.Count = Text('q', length).Length;
                return slot.Count;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool Put(ref object value, int length)
            {
                value = Text('f', length);
                return Text('g', length).Length == length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Slot Make(int length)
            {
                var slot = new Slot();
                slot.Value = Text('m', length);
                slot.Count = Text('n', length).Length;
                return slot;
            }

            private static bool Holds(object value, char expected, int length) =>
                value is string text && text.Length == length && text[0] == expected && text[^1] == expected;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Check(Slot slot, char expected, int length) =>
                Holds(slot.Value, expected, length) && slot.Count == length ? 0 : 2;

            // The constructor runs on newobj's temporary.
            public static int ConstructedValue(int input) => Check(new Slot(input % 3 + 2), 'c', input % 3 + 2);

            public static int ConstructedValueStoredInField(int input)
            {
                var holder = new Holder();
                holder.Field = new Slot(input % 3 + 2);
                return Check(holder.Field, 'c', input % 3 + 2);
            }

            // The local's fields are read from lane locals around the call. A
            // whole-value store would keep it in its storage, so it is filled
            // field by field.
            public static int LaneSplitLocal(int input)
            {
                Slot slot = default;
                slot.Value = Text('a', 2);
                slot.Count = input;
                var before = ((string)slot.Value).Length + slot.Count;
                Put(ref slot, input % 3 + 2);
                return before == input + 2 ? Check(slot, 'p', input % 3 + 2) : 3;
            }

            public static int LaneSplitArgument(int input) =>
                Refilled(new Slot { Value = Text('a', 2), Count = 1 }, input % 3 + 2);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Refilled(Slot slot, int length)
            {
                var before = ((string)slot.Value).Length + slot.Count;
                Put(ref slot, length);
                return before == 3 ? Check(slot, 'p', length) : 3;
            }

            // The filter takes the address of a slot its environment holds.
            public static int FilterCapturedLocal(int input)
            {
                object value = Text('a', 1);
                try
                {
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException) when (Put(ref value, input % 3 + 2))
                {
                }
                return Holds(value, 'f', input % 3 + 2) ? 0 : 2;
            }

            public static int FilterCapturedArgument(int input) => Filtered(Text('a', 1), input % 3 + 2);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Filtered(object value, int length)
            {
                try
                {
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException) when (Put(ref value, length))
                {
                }
                return Holds(value, 'f', length) ? 0 : 2;
            }

            // An instance method's receiver addresses its caller's local.
            public static int ValueReceiver(int input)
            {
                var slot = new Slot { Value = Text('a', 1) };
                slot.Count = slot.Refill(input % 3 + 2);
                return Check(slot, 'r', input % 3 + 2);
            }

            public static int Local(int input)
            {
                object value = Text('a', 1);
                return Put(ref value, input % 3 + 2) && Holds(value, 'f', input % 3 + 2) ? 0 : 2;
            }

            public static int ArrayElement(int input)
            {
                var values = new object[] { Text('a', 1), Text('b', 1) };
                return Put(ref values[1], input % 3 + 2) && Holds(values[1], 'f', input % 3 + 2) ? 0 : 2;
            }

            public static int BoxedValue(int input)
            {
                object boxed = new Slot { Value = Text('a', 1) };
                Put(ref Unsafe.Unbox<Slot>(boxed), input % 3 + 2);
                return Check((Slot)boxed, 'p', input % 3 + 2);
            }

            public static int StaticField(int input)
            {
                Put(ref shared, input % 3 + 2);
                return Check(shared, 'p', input % 3 + 2);
            }

            // The result is copied to the caller's buffer only at the return.
            public static int ReturnedValue(int input) => Check(Make(input % 3 + 2), 'm', input % 3 + 2);
        }

        public static class EntryPoint
        {
            public static object? Probe;

            public static int Trace() => 0;

            public static int Run(int input)
            {
                // On NetWasm, which compiles ahead of time, the case runs with a
                // collection at every allocation; desktop .NET only computes
                // the expected value.
                Probe = new object();
                var collections = GC.CollectionCount(0);
                Probe = new object();
                if (!RuntimeFeature.IsDynamicCodeSupported && GC.CollectionCount(0) == collections) return 1;
                var failure = Cases.CASE(input);
                return failure == 0 ? input : failure;
            }
        }
        """;
}
