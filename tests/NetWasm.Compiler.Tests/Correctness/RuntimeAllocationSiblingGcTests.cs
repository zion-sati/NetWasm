namespace NetWasm.Compiler.Tests.Correctness;

// Siblings of issues 144, 146 and 160: every path that allocates inside the runtime or a method
// the compiler supplies (type objects, clones, enum and value type intrinsics, delegates, weak
// tables, handles, reflection, exceptions, boxing and formatting) must publish the references
// its caller holds, and each implicit exception must be an object of its own. With a collection
// before every allocation, an object held only in a local across each call keeps its fields.
public sealed class RuntimeAllocationSiblingGcTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void RuntimeAllocationsKeepTheirCallersReferencesAcrossCollections() =>
        runner.Run(new("RuntimeAllocationSiblings", "RuntimeAllocationSiblings", Source, [42])
        {
            AllowUnsafe = true,
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            CollectsAtEveryAllocation = true,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.ComponentModel;
        using System.Runtime.CompilerServices;
        using System.Runtime.ExceptionServices;
        using System.Runtime.InteropServices;
        namespace RuntimeAllocationSiblings;

        public sealed class Holder
        {
            public int Value;
            public string? Text;
            public Holder? Next;
            public Holder ShallowCopy() => (Holder)MemberwiseClone();
        }

        [Flags]
        public enum Colors { None = 0, Red = 1, Green = 2, Blue = 4 }

        public struct Keyed : IEquatable<Keyed>
        {
            public string Name;
            public Holder? Owner;
            public bool Equals(Keyed other) => Name == other.Name;
        }

        public struct Loose
        {
            public object? Item;
            public string? Label;
        }

        public sealed class Allocating
        {
            public int Id;
            public override bool Equals(object? other) => other is Allocating value && ("x" + value.Id) == ("x" + Id);
            public override int GetHashCode() => ("h" + Id).Length;
        }

        public sealed class Generic<T> { public T? Item; }

        [Description("described")]
        public sealed class Described { public int Field = 3; }

        public static class EntryPoint
        {
            private static int s_failed;

            public static int Trace() => 0;

            private static Holder Make(int value) =>
                new() { Value = value, Text = "keep" + value, Next = new Holder { Value = value + 1, Text = "next" } };

            // Allocates over whatever a missed root left behind, then checks the held object.
            private static void Verify(Holder held, int value, int step)
            {
                var noise = new Holder[4];
                for (var i = 0; i < noise.Length; i++) noise[i] = new Holder { Value = -1, Text = "noise" + i };
                if (s_failed == 0 &&
                    (held.Value != value || held.Text != "keep" + value || held.Next is not { Value: var next } || next != value + 1))
                {
                    s_failed = step;
                }
            }

            private static void Expect(bool condition, int step)
            {
                if (!condition && s_failed == 0) s_failed = 1000 + step;
            }

            private static string NameOf<T>() => typeof(T).Name + typeof(T[]).Name + typeof(List<T>).Name;

            public static int Run(int input)
            {
                var held = Make(1);
                Expect(new Generic<Colors>().GetType().Name.Length > 0 && NameOf<Keyed>().Length > 0, 1);
                Verify(held, 1, 1);

                held = Make(2);
                var copy = held.ShallowCopy();
                Verify(held, 2, 2);
                Expect(copy.Text == "keep2", 2);

                held = Make(3);
                var text = Colors.Red.ToString() + (Colors.Red | Colors.Blue) + Enum.GetName(Colors.Green) + Enum.Format(typeof(Colors), 6, "G");
                Verify(held, 3, 3);
                Expect(text == "RedRed, BlueGreenGreen, Blue", 3);

                held = Make(4);
                var names = Enum.GetNames<Colors>().Length + Enum.GetValues<Colors>().Length + Enum.GetValuesAsUnderlyingType<Colors>().Length;
                var parsed = Enum.Parse<Colors>("Red, Green") | (Enum.TryParse<Colors>("Blue", out var blue) ? blue : 0);
                var boxed = Enum.ToObject(typeof(Colors), 5);
                Verify(held, 4, 4);
                Expect(names == 12 && parsed == (Colors)7 && boxed.ToString() == "Red, Blue" && Enum.IsDefined(Colors.Green), 4);

                held = Make(5);
                var left = new Loose { Item = new Allocating { Id = 7 }, Label = "a" + held.Value };
                var right = new Loose { Item = new Allocating { Id = 7 }, Label = "a" + held.Value };
                var equal = left.Equals(right) && left.GetHashCode() == right.GetHashCode();
                Verify(held, 5, 5);
                Expect(equal, 5);

                held = Make(6);
                var grid = new Holder?[2, 2];
                grid[1, 1] = held;
                var gridCopy = (Holder?[,])grid.Clone();
                var integers = new[] { 1, 2, 3 };
                var objects = new object[3];
                Array.Copy(integers, objects, 3);
                Array.Resize(ref integers, 6);
                var boxedElement = ((Array)new[] { 9L, 8L }).GetValue(1);
                Verify(held, 6, 6);
                Expect(gridCopy[1, 1] == held && (int)objects[2] == 3 && integers.Length == 6 && (long)boxedElement! == 8, 6);

                held = Make(7);
                Action first = Touch;
                Action second = Untouched;
                var combined = first + second + first;
                var removed = combined - first;
                var list = combined.GetInvocationList();
                Verify(held, 7, 7);
                Expect(list.Length == 3 && removed!.GetInvocationList().Length == 2, 7);

                held = Make(8);
                var table = new ConditionalWeakTable<Holder, Holder>();
                table.Add(held, Make(80));
                table.AddOrUpdate(held.Next!, Make(81));
                var value = table.GetValue(new Holder(), _ => Make(82));
                Verify(held, 8, 8);
                Expect(table.TryGetValue(held, out var found) && found.Value == 80 && value.Value == 82, 8);

                held = Make(9);
                var weak = new WeakReference<Holder>(held.Next!);
                var handle = GCHandle.Alloc(held.Next!);
                var strong = (Holder)handle.Target!;
                handle.Free();
                Verify(held, 9, 9);
                Expect(weak.TryGetTarget(out var target) && target == held.Next && strong == held.Next, 9);

                held = Make(10);
                var activated = Activator.CreateInstance<Holder>();
                var described = new Described();
                var attribute = (DescriptionAttribute?)Attribute.GetCustomAttribute(typeof(Described), typeof(DescriptionAttribute));
                Verify(held, 10, 10);
                Expect(activated.Text is null && described.Field == 3 && attribute?.Description == "described", 10);

                held = Make(11);
                int? maybe = held.Value;
                object boxedNullable = maybe;
                var underlying = Nullable.GetUnderlyingType(typeof(int?));
                var keyed = new Keyed { Name = "k" + held.Value, Owner = held };
                object boxedKeyed = keyed;
                var unboxed = (Keyed)boxedKeyed;
                Verify(held, 11, 11);
                Expect((int)boxedNullable == 11 && underlying == typeof(int) && unboxed.Owner == held, 11);

                held = Make(12);
                var formatted = string.Format("{0}:{1:F2}:{2:X}", held.Value, 1.5, 255) + $"{held.Text}{3.25}" +
                    string.Join(",", new[] { "a", "b" }) + "a,b".Split(',').Length + held.Text!.Replace("keep", "k").ToUpperInvariant();
                Verify(held, 12, 12);
                Expect(formatted == "12:1.50:FFkeep123.25a,b2K12", 12);

                held = Make(13);
                var traced = "";
                try { Throw(held.Value); }
                catch (InvalidOperationException exception)
                {
                    // The type, from the type itself: a program carries no type names (issue 225).
                    traced = exception.Message + (exception.GetType() == typeof(InvalidOperationException)
                        ? "InvalidOperationException" : "?");
                    var captured = ExceptionDispatchInfo.Capture(exception);
                    try { captured.Throw(); }
                    catch (InvalidOperationException rethrown) { traced += rethrown.Message.Length; }
                }
                Verify(held, 13, 13);
                Expect(traced == "bad13InvalidOperationException5", 13);

                held = Make(14);
                Exception? nullReference = null, outOfRange = null, invalidCast = null;
                try { Holder? missing = input > 100 ? held : null; _ = missing!.Value; } catch (NullReferenceException exception) { nullReference = exception; }
                nullReference!.Data["held"] = Make(140);
                try { var single = new int[1]; _ = single[input]; } catch (IndexOutOfRangeException exception) { outOfRange = exception; }
                outOfRange!.Data["held"] = Make(141);
                try { _ = (string)(object)held; } catch (InvalidCastException exception) { invalidCast = exception; }
                GC.Collect();
                Verify(held, 14, 14);
                Expect(!ReferenceEquals(nullReference, outOfRange) && invalidCast is not null &&
                    ((Holder)nullReference.Data["held"]!).Value == 140 && ((Holder)outOfRange.Data["held"]!).Value == 141, 14);

                held = Make(15);
                var lazy = new Lazy<Holder>(() => Make(150));
                var dictionary = new Dictionary<string, Holder> { ["a"] = Make(151) };
                var tuple = (held.Value, held.Text);
                var record = tuple.ToString();
                Verify(held, 15, 15);
                Expect(lazy.Value.Value == 150 && dictionary["a"].Value == 151 && record == "(15, keep15)", 15);

                return s_failed == 0 ? input : s_failed;
            }

            private static void Touch() => s_failed += 0;

            private static void Untouched() { }

            private static void Throw(int value) => throw new InvalidOperationException("bad" + value);
        }
        """;
}
