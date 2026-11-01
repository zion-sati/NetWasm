namespace NetWasm.Compiler.Tests.Correctness;

// Each case holds objects in one way a program can hold them while a collection
// runs: pinned, through an interior pointer, in a struct on the stack or in a
// return value, in a state machine, an exception in flight, a type initializer
// or a deep frame, or laid out inline, explicitly or in a generic instance.
// With a collection before every allocation, an object held that way is still
// intact when the case reads it.
public sealed class RootLifetimeGcTests(CorrectnessTestRunner runner)
{
    public static TheoryData<string> Cases() =>
    [
        "PinnedString", "PinnedArray", "PinnedField", "SpanInsideString", "SpanInsideArray", "RefReturnIntoArray",
        "RefIntoBox", "StructReturnsOnTheStack", "LargeStructReturn", "StructArgument", "AsyncHoistedLocals",
        "IteratorHoistedLocals", "Closure", "ExceptionInFilter", "ExceptionThroughFinally", "ParentLocalInFilter",
        "Rethrow", "DispatchInfoRethrow", "TypeInitializerMidExpression", "DeepRecursion", "FrameSlotReuse",
        "InlineArrayInClass", "ExplicitLayout", "NullableStruct", "ValueTuples", "RefToArrayOfStructs",
        "RectangularArray", "JaggedArray", "LargeArray", "GenericStructs", "ConstrainedCallOnLocal",
        "RefStructWithRefField", "RefStructWithStackSpan", "StringCreate", "InterpolatedStrings", "CollectionCallbacks",
        "DictionaryGrowth", "WeakReferenceWithStrongLocal", "ConditionalWeakTableValue", "ThreadStatic",
        "DelegateOverBoxedStruct", "VirtualCallOnTemporary", "OutParameters", "RefStructEnumerator",
        "ConditionalTemporaries", "ReceiverFieldReference", "ReceiverInArray", "WideFramesAcrossRootChunks", "InterfaceCallOnTemporaryBox", "InterfaceCallOnBoxedStruct",
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void WhatACaseHoldsSurvivesCollections(string name) =>
        runner.Run(new("RootLifetime" + name, "RootLifetime", Source.Replace("CASE", name, StringComparison.Ordinal), [42])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            CollectsAtEveryAllocation = true,
            AllowUnsafe = true,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Runtime.ExceptionServices;
        using System.Runtime.InteropServices;
        using System.Threading.Tasks;
        namespace RootLifetime;

        public sealed class Holder
        {
            public int Number = 7;
            public object Field = "";
        }

        public struct Pair
        {
            public string First;
            public int Second;
        }

        public struct Six
        {
            public string A, B, C, D, E, F;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct Explicit
        {
            [FieldOffset(0)] public int Number;
            [FieldOffset(8)] public object First;
            [FieldOffset(16)] public string Second;
        }

        [InlineArray(5)]
        public struct Five
        {
            private object element;
        }

        [InlineArray(64)]
        public struct Wide
        {
            private object element;
        }

        public sealed class WithInline
        {
            public Five Items;
            public string Tail = "";
        }

        public struct Generic<T>
        {
            public T Value;
            public string Name;
        }

        public interface IWork
        {
            int Work();
        }

        public struct Worker : IWork
        {
            public string Value;
            public int Work() => Cases.Text('w', 2).Length + Value.Length;

            // The receiver is dead after the reference to its field is taken.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public int Held()
            {
                ref var value = ref Value;
                Cases.Text('h', 2);
                return value.Length;
            }
        }

        public ref struct RefHolder
        {
            public ref object Field;
        }

        public ref struct SpanHolder
        {
            public Span<object> Values;
        }

        public ref struct Walker
        {
            private readonly Span<object> values;
            private int index;

            public Walker(Span<object> values)
            {
                this.values = values;
                index = -1;
            }

            public Walker GetEnumerator() => this;
            public bool MoveNext() => ++index < values.Length;
            public object Current => values[index];
        }

        public class Failure(string payload) : Exception
        {
            public string Payload = payload;
        }

        public class Shape
        {
            public string Name = Cases.Text('v', 3);
            public virtual int Measure() => Cases.Text('m', 2).Length + Name.Length;
        }

        public static class Late
        {
            public static readonly string Value = Build();

            private static string Build()
            {
                var parts = new List<string>();
                for (var index = 0; index < 20; index++) parts.Add(Cases.Text('t', index % 4 + 1));
                return string.Concat(parts);
            }
        }

        public static class Cases
        {
            [ThreadStatic] private static string? perThread;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static string Text(char value, int length) => new(value, length);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Churn()
            {
                for (var index = 0; index < 8; index++) _ = new object[index + 1];
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int[] Integers(int input)
            {
                var values = new int[16];
                for (var index = 0; index < values.Length; index++) values[index] = input + index;
                return values;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object[] Objects(int count)
            {
                var values = new object[count];
                for (var index = 0; index < count; index++) values[index] = Text((char)('a' + index % 26), index % 5 + 1);
                return values;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Pair MakePair(int input) => new() { First = Text('p', input % 4 + 1), Second = input };

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static ref object Element(object[] values, int index) => ref values[index];

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Combine(Pair first, Pair second) => Text('c', 1).Length + first.First.Length + second.First.Length;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Six MakeSix()
            {
                var six = new Six { A = Text('a', 1), B = Text('b', 2), C = Text('c', 3) };
                six.D = Text('d', 4);
                six.E = Text('e', 5);
                six.F = Text('f', 6);
                return six;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ByValue(Pair pair)
            {
                Churn();
                return pair.First.Length + pair.Second;
            }

            private static async Task<int> Hoisted(int input)
            {
                var first = Text('h', 3);
                var pair = MakePair(input);
                await Task.CompletedTask;
                Churn();
                var second = Text('i', 2);
                await Task.CompletedTask;
                return first.Length + pair.First.Length + second.Length;
            }

            private static IEnumerable<string> Yielded(int input)
            {
                var kept = Text('y', 2);
                for (var index = 0; index < 5; index++)
                {
                    var fresh = Text('z', index + 1);
                    yield return fresh + kept;
                    Churn();
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool Filter(Failure failure)
            {
                Churn();
                return failure.Payload == "xxxxx";
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool Check(string kept)
            {
                Churn();
                return kept == "kkkkkk";
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Throw(int input) => throw new Failure(Text('x', 5));

            // Deep enough for many frames of roots under every collection,
            // shallow enough for a Debug build on Node's native stack.
            private static int Recurse(int depth)
            {
                string a = Text('r', depth % 3 + 1), b = Text('s', depth % 2 + 1), c = Text('t', 1), d = Text('u', 1);
                string e = Text('v', 1), f = Text('w', 1), g = Text('x', 1), h = Text('y', 1);
                if (depth == 0) Churn();
                var below = depth == 0 ? 0 : Recurse(depth - 1);
                return below + a.Length + b.Length + c.Length + d.Length + e.Length + f.Length + g.Length + h.Length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Big()
            {
                string a = Text('a', 1), b = Text('b', 1), c = Text('c', 1), d = Text('d', 1);
                string e = Text('e', 1), f = Text('f', 1), g = Text('g', 1), h = Text('h', 1);
                return (a + b + c + d + e + f + g + h).Length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Small()
            {
                Churn();
                var a = Text('q', 2);
                Churn();
                return a.Length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Run<T>(ref T worker) where T : IWork => worker.Work();

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Read(RefHolder holder)
            {
                Churn();
                return ((string)holder.Field).Length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Read(SpanHolder holder)
            {
                var length = 0;
                foreach (var value in holder.Values) length += Text('g', 1).Length + ((string)value).Length;
                return length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static string Churned(char value)
            {
                Churn();
                return Text(value, 2);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Outputs(out object first, out string second, out Pair third)
            {
                first = Text('o', 1);
                Churn();
                second = Text('u', 2);
                Churn();
                third = MakePair(3);
                Churn();
                return ((string)first).Length + second.Length + third.First.Length;
            }

            public static unsafe int PinnedString(int input)
            {
                fixed (char* text = Text('a', 8))
                {
                    Churn();
                    var count = 0;
                    for (var index = 0; index < 8; index++) count += text[index] == 'a' ? 1 : 0;
                    return count == 8 ? 0 : 2;
                }
            }

            public static unsafe int PinnedArray(int input)
            {
                fixed (int* values = Integers(input))
                {
                    Churn();
                    var sum = 0;
                    for (var index = 0; index < 16; index++) sum += values[index];
                    return sum == 16 * input + 120 ? 0 : 2;
                }
            }

            public static unsafe int PinnedField(int input)
            {
                fixed (int* number = &new Holder().Number)
                {
                    Churn();
                    return *number == 7 ? 0 : 2;
                }
            }

            public static int SpanInsideString(int input)
            {
                var text = Text('s', 20).AsSpan(5, 10);
                Churn();
                return text.Length == 10 && text[0] == 's' && text[9] == 's' ? 0 : 2;
            }

            public static int SpanInsideArray(int input)
            {
                var values = Objects(16).AsSpan(8);
                Churn();
                var length = 0;
                foreach (var value in values) length += ((string)value).Length;
                return length == 4 + 5 + 1 + 2 + 3 + 4 + 5 + 1 ? 0 : 2;
            }

            public static int RefReturnIntoArray(int input)
            {
                ref var element = ref Element(Objects(4), 2);
                Churn();
                return (string)element == "ccc" ? 0 : 2;
            }

            public static int RefIntoBox(int input)
            {
                object box = MakePair(input);
                ref var pair = ref Unsafe.Unbox<Pair>(box);
                box = null!;
                Churn();
                return pair.First.Length == input % 4 + 1 && pair.Second == input ? 0 : 2;
            }

            public static int StructReturnsOnTheStack(int input) =>
                Combine(MakePair(input), MakePair(input + 1)) == 1 + input % 4 + 1 + (input + 1) % 4 + 1 ? 0 : 2;

            public static int LargeStructReturn(int input)
            {
                var six = MakeSix();
                Churn();
                return (six.A + six.B + six.C + six.D + six.E + six.F).Length == 21 ? 0 : 2;
            }

            public static int StructArgument(int input) => ByValue(MakePair(input)) == input % 4 + 1 + input ? 0 : 2;

            public static int AsyncHoistedLocals(int input) =>
                Hoisted(input).GetAwaiter().GetResult() == 3 + input % 4 + 1 + 2 ? 0 : 2;

            public static int IteratorHoistedLocals(int input)
            {
                var length = 0;
                foreach (var value in Yielded(input))
                {
                    Churn();
                    length += value.Length;
                }
                return length == 15 + 10 ? 0 : 2;
            }

            public static int Closure(int input)
            {
                var pair = MakePair(input);
                var text = Text('l', 3);
                Func<int> read = () => Text('n', 1).Length + pair.First.Length + text.Length;
                Churn();
                return read() == 1 + input % 4 + 1 + 3 ? 0 : 3;
            }

            public static int ExceptionInFilter(int input)
            {
                try
                {
                    Throw(input);
                }
                catch (Failure failure) when (Filter(failure))
                {
                    Churn();
                    return failure.Payload == "xxxxx" ? 0 : 2;
                }
                return 3;
            }

            public static int ExceptionThroughFinally(int input)
            {
                var finished = 0;
                try
                {
                    try
                    {
                        Throw(input);
                    }
                    finally
                    {
                        Churn();
                        finished = Text('f', 2).Length;
                    }
                }
                catch (Failure failure)
                {
                    return failure.Payload == "xxxxx" && finished == 2 ? 0 : 2;
                }
                return 3;
            }

            public static int ParentLocalInFilter(int input)
            {
                var kept = Text('k', 6);
                try
                {
                    Throw(input);
                }
                catch (Failure) when (Check(kept))
                {
                    Churn();
                    return kept.Length == 6 ? 0 : 2;
                }
                return 3;
            }

            public static int Rethrow(int input)
            {
                try
                {
                    try
                    {
                        Throw(input);
                    }
                    catch (Failure)
                    {
                        Churn();
                        throw;
                    }
                }
                catch (Failure failure)
                {
                    Churn();
                    return failure.Payload == "xxxxx" ? 0 : 2;
                }
                return 3;
            }

            public static int DispatchInfoRethrow(int input)
            {
                ExceptionDispatchInfo? captured = null;
                try
                {
                    Throw(input);
                }
                catch (Failure failure)
                {
                    captured = ExceptionDispatchInfo.Capture(failure);
                }
                Churn();
                try
                {
                    captured!.Throw();
                }
                catch (Failure failure)
                {
                    return failure.Payload == "xxxxx" ? 0 : 2;
                }
                return 3;
            }

            public static int TypeInitializerMidExpression(int input) =>
                (Text('a', 3) + Late.Value).Length == 3 + 50 ? 0 : 2;

            public static int DeepRecursion(int input)
            {
                var expected = 0;
                for (var depth = 0; depth <= 1500; depth++) expected += depth % 3 + 1 + depth % 2 + 1 + 6;
                return Recurse(1500) == expected ? 0 : 2;
            }

            public static int FrameSlotReuse(int input)
            {
                var total = 0;
                for (var round = 0; round < 4; round++) total += Big() + Small();
                return total == 4 * (8 + 2) ? 0 : 2;
            }

            public static int InlineArrayInClass(int input)
            {
                var holder = new WithInline();
                for (var index = 0; index < 5; index++) holder.Items[index] = Text('i', index + 1);
                holder.Tail = Text('t', 2);
                Churn();
                var length = holder.Tail.Length;
                foreach (var item in holder.Items) length += ((string)item).Length;
                return length == 2 + 15 ? 0 : 2;
            }

            public static int ExplicitLayout(int input)
            {
                var local = new Explicit { Number = input, First = Text('e', 3), Second = Text('x', 4) };
                var array = new Explicit[] { new() { First = Text('y', 2), Second = Text('z', 1) } };
                object boxed = new Explicit { First = Text('b', 5), Second = Text('c', 6) };
                Churn();
                var unboxed = (Explicit)boxed;
                return local.Number == input && ((string)local.First).Length + local.Second.Length == 7 &&
                    ((string)array[0].First).Length + array[0].Second.Length == 3 &&
                    ((string)unboxed.First).Length + unboxed.Second.Length == 11 ? 0 : 2;
            }

            public static int NullableStruct(int input)
            {
                Pair? pair = MakePair(input);
                Churn();
                return pair.Value.First.Length == input % 4 + 1 ? 0 : 2;
            }

            public static int ValueTuples(int input)
            {
                (string Name, object Value) tuple = (Text('n', 2), Text('v', 3));
                var array = new (string, int)[] { (Text('a', 4), 1), (Text('b', 5), 2) };
                Churn();
                return tuple.Name.Length + ((string)tuple.Value).Length + array[0].Item1.Length + array[1].Item1.Length == 14 ? 0 : 2;
            }

            public static int RefToArrayOfStructs(int input)
            {
                var pairs = new Pair[8];
                for (var index = 0; index < pairs.Length; index++) pairs[index] = MakePair(index);
                ref var pair = ref pairs[3];
                pairs = null!;
                Churn();
                return pair.First.Length == 4 && pair.Second == 3 ? 0 : 2;
            }

            public static int RectangularArray(int input)
            {
                var grid = new object[3, 4];
                for (var row = 0; row < 3; row++)
                    for (var column = 0; column < 4; column++) grid[row, column] = Text('g', row + column + 1);
                Churn();
                var length = 0;
                foreach (var item in grid) length += ((string)item).Length;
                return length == 42 ? 0 : 2;
            }

            public static int JaggedArray(int input)
            {
                var rows = new string[4][];
                for (var row = 0; row < 4; row++)
                {
                    rows[row] = new string[row + 1];
                    for (var column = 0; column <= row; column++) rows[row][column] = Text('j', column + 1);
                }
                Churn();
                var length = 0;
                foreach (var row in rows) foreach (var item in row) length += item.Length;
                return length == 1 + 3 + 6 + 10 ? 0 : 2;
            }

            public static int LargeArray(int input)
            {
                var values = new object[100_000];
                for (var index = 0; index < values.Length; index += 997) values[index] = Text('l', index % 7 + 1);
                Churn();
                var length = 0;
                for (var index = 0; index < values.Length; index += 997) length += ((string)values[index]).Length;
                var expected = 0;
                for (var index = 0; index < values.Length; index += 997) expected += index % 7 + 1;
                return length == expected ? 0 : 2;
            }

            public static int GenericStructs(int input)
            {
                var text = new Generic<string> { Value = Text('a', 2), Name = Text('b', 3) };
                var pair = new Generic<Pair> { Value = MakePair(input), Name = Text('c', 1) };
                var number = new Generic<int> { Value = input, Name = Text('d', 4) };
                Churn();
                return text.Value.Length + text.Name.Length + pair.Value.First.Length + pair.Name.Length + number.Name.Length ==
                    2 + 3 + input % 4 + 1 + 1 + 4 && number.Value == input ? 0 : 2;
            }

            public static int ConstrainedCallOnLocal(int input)
            {
                var worker = new Worker { Value = Text('w', 3) };
                return Run(ref worker) == 5 ? 0 : 2;
            }

            public static int RefStructWithRefField(int input)
            {
                var holder = new RefHolder { Field = ref Element(Objects(4), 3) };
                return Read(holder) == 4 ? 0 : 2;
            }

            public static int RefStructWithStackSpan(int input)
            {
                var five = new Five();
                for (var index = 0; index < 5; index++) five[index] = Text('h', index + 1);
                return Read(new SpanHolder { Values = five }) == 5 + 15 ? 0 : 2;
            }

            public static int StringCreate(int input)
            {
                var created = string.Create(6, Text('s', 3), static (span, state) =>
                {
                    Churn();
                    state.AsSpan().CopyTo(span);
                    Text('u', 2).AsSpan().CopyTo(span[3..]);
                    span[5] = '!';
                });
                return created == "sssuu!" ? 0 : 2;
            }

            public static int InterpolatedStrings(int input)
            {
                var text = $"{Text('a', 3)}-{input}-{Churned('b')}-{MakePair(input).First}";
                return text.Length == 3 + 1 + input.ToString().Length + 1 + 2 + 1 + input % 4 + 1 ? 0 : 2;
            }

            public static int CollectionCallbacks(int input)
            {
                var values = new List<string>();
                for (var index = 0; index < 60; index++) values.Add(Text('q', index % 5 + 1));
                values.Sort((left, right) =>
                {
                    Churn();
                    return left.Length.CompareTo(right.Length);
                });
                var converted = values.ConvertAll(value => value + Text('x', 1));
                converted.RemoveAll(value => Text('r', 1).Length + value.Length < 4);
                Churn();
                var length = 0;
                foreach (var value in converted) length += value.Length;
                return values[0].Length == 1 && values[^1].Length == 5 && length == 12 * (3 + 4 + 5 + 6) ? 0 : 2;
            }

            public static int DictionaryGrowth(int input)
            {
                var map = new Dictionary<string, object>();
                for (var index = 0; index < 200; index++) map[Text('k', 1) + index] = Text('v', index % 3 + 1);
                Churn();
                var length = 0;
                for (var index = 0; index < 200; index++) length += ((string)map["k" + index]).Length;
                return length == 67 * 1 + 67 * 2 + 66 * 3 ? 0 : 2;
            }

            public static int WeakReferenceWithStrongLocal(int input)
            {
                var pair = MakePair(input);
                var weak = new WeakReference(pair.First);
                Churn();
                GC.Collect();
                return weak.Target is string target && ReferenceEquals(target, pair.First) ? 0 : 2;
            }

            public static int ConditionalWeakTableValue(int input)
            {
                var table = new ConditionalWeakTable<object, string>();
                var key = new object();
                table.Add(key, Text('w', 4));
                Churn();
                GC.Collect();
                return table.TryGetValue(key, out var value) && value == "wwww" ? 0 : 2;
            }

            public static int ThreadStatic(int input)
            {
                perThread = Text('p', 5);
                Churn();
                return perThread == "ppppp" ? 0 : 2;
            }

            public static int DelegateOverBoxedStruct(int input)
            {
                var worker = new Worker { Value = Text('d', 4) };
                Func<int> work = worker.Work;
                worker = default;
                Churn();
                return work() == 6 ? 0 : 2;
            }

            public static int VirtualCallOnTemporary(int input) => new Shape().Measure() == 5 ? 0 : 2;

            public static int OutParameters(int input) =>
                Outputs(out var first, out var second, out var third) == 1 + 2 + 4 &&
                ((string)first).Length + second.Length + third.First.Length == 7 ? 0 : 2;

            public static int RefStructEnumerator(int input)
            {
                var five = new Five();
                for (var index = 0; index < 5; index++) five[index] = Text('e', index + 1);
                var length = 0;
                foreach (var value in new Walker(five)) length += Text('f', 1).Length + ((string)value).Length;
                return length == 5 + 15 ? 0 : 2;
            }

            public static int ReceiverFieldReference(int input)
            {
                var worker = new Worker { Value = Text('a', 4) };
                return worker.Held() == 4 ? 0 : 2;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Worker[] Workers()
            {
                var workers = new Worker[4];
                for (var index = 0; index < workers.Length; index++) workers[index].Value = Text('w', index + 1);
                return workers;
            }

            public static int ReceiverInArray(int input) => Workers()[2].Held() + Workers()[3].Work() == 3 + 2 + 4 ? 0 : 2;

            // Sixty-four roots a frame outgrow the first chunk of root frames,
            // and the frames' storage its chunks, at a depth Node's stack takes.
            private static int Widen(int depth)
            {
                var wide = new Wide();
                wide[0] = Text('a', depth % 4 + 1);
                wide[63] = Text('z', depth % 3 + 1);
                if (depth == 0) Churn();
                var below = depth == 0 ? 0 : Widen(depth - 1);
                return below + ((string)wide[0]).Length + ((string)wide[63]).Length;
            }

            public static int WideFramesAcrossRootChunks(int input)
            {
                var expected = 0;
                for (var depth = 0; depth <= 600; depth++) expected += depth % 4 + 1 + depth % 3 + 1;
                return Widen(600) == expected ? 0 : 2;
            }

            // The compiler sees the box's type and calls the struct directly,
            // so this checks that removing the box keeps what the struct holds.
            public static int InterfaceCallOnTemporaryBox(int input) =>
                ((IWork)new Worker { Value = Text('b', 3) }).Work() == 5 ? 0 : 2;

            // A box whose type the caller cannot see: the struct method reached
            // through the interface holds it only by its receiver once the
            // caller hands the box over.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static IWork Boxed(int input) => new Worker { Value = Text('b', input % 3 + 2) };

            public static int InterfaceCallOnBoxedStruct(int input) => Boxed(input).Work() == 2 + input % 3 + 2 ? 0 : 2;

            public static int ConditionalTemporaries(int input) =>
                (Text('a', 1) + (input > 0 ? Churned('b') : Text('c', 3)) + Text('d', 2)).Length == 5 ? 0 : 2;
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
