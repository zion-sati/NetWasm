namespace NetWasm.Compiler.Tests.Correctness;

// A delegate or function pointer can target any method, not only one with an ordinary CIL body
// (issue 223's class): runtime methods, intrinsics and methods the compiler supplies, and
// targets that need an adapter (a value-type receiver, a closed static, interface, default
// interface and generic virtual dispatch, base calls, constrained static abstracts and variant
// conversions). Each program runs as desktop .NET does.
public sealed class MethodGroupTargetCompilationTests(CorrectnessTestRunner runner)
{
    public static TheoryData<string> Cases => [.. Programs.Keys];

    [Theory]
    [MemberData(nameof(Cases))]
    public void AMethodGroupOfAnyKindOfMethodRunsAsDesktopDoes(string name) =>
        runner.Run(new("MethodGroup" + name, "MethodGroups", Prelude + Programs[name], [0, 3, 7])
        {
            AllowUnsafe = true,
        });

    // Enum's intrinsics take edge arguments (null, another enum type, a boxed integer, an
    // unknown format) as desktop's Enum does, at a call site and through a delegate whose body
    // forwards to the intrinsic. Each input reports one edge.
    [Fact]
    public void EnumIntrinsicsTakeEdgeArgumentsAsDesktopDoes() =>
        runner.Run(new("EnumEdgeArguments", "MethodGroups", Prelude + EnumEdges, [.. Enumerable.Range(0, 23)])
        {
            AllowUnsafe = true,
            ReportAllMismatches = true,
        });

    private const string EnumEdges = """
            public enum Other { X = 1 }

            public static partial class EntryPoint
            {
                private static int Outcome(Func<object?> run)
                {
                    try
                    {
                        var value = run();
                        return value switch { bool flag => flag ? 1 : 2, int number => 10 + number, string text => 20 + text.Length, _ => 3 };
                    }
                    catch (ArgumentNullException) { return 4; }
                    catch (ArgumentException) { return 5; }
                    catch (NullReferenceException) { return 6; }
                    catch (FormatException) { return 7; }
                    catch (Exception) { return 8; }
                }

                public static int Run(int input)
                {
                    Enum granted = Color.Green;
                    Enum? none = input > 100 ? Color.Red : null;
                    Func<Enum, bool> has = granted.HasFlag;
                    Func<object?, int> compare = granted.CompareTo;
                    Func<object?, bool> equals = granted.Equals;
                    Func<string?, string> format = granted.ToString;
                    int[] outcomes =
                    [
                        Outcome(() => granted.HasFlag(null!)), Outcome(() => has(null!)),
                        Outcome(() => granted.HasFlag(Other.X)), Outcome(() => has(Other.X)),
                        Outcome(() => none!.HasFlag(Color.Red)),
                        Outcome(() => granted.CompareTo(null)), Outcome(() => compare(null)),
                        Outcome(() => granted.CompareTo(Other.X)), Outcome(() => compare(Other.X)),
                        Outcome(() => granted.CompareTo(5)), Outcome(() => compare(5)),
                        Outcome(() => granted.Equals(null)), Outcome(() => equals(null)),
                        Outcome(() => granted.Equals(Other.X)), Outcome(() => equals(Other.X)),
                        Outcome(() => granted.ToString((string?)null)), Outcome(() => format(null)),
                        Outcome(() => granted.ToString("")), Outcome(() => format("X")),
                        Outcome(() => granted.ToString("Q")), Outcome(() => format("Q")),
                        Outcome(() => none!.ToString()), Outcome(() => none!.GetHashCode()),
                    ];
                    return outcomes[input % outcomes.Length];
                }
            }
            """;

    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        namespace MethodGroups;

        public enum Color { Red = 1, Green = 2, Blue = 4 }

        public struct Point
        {
            public int X;
            public int Y;
            public int Sum() => X + Y;
        }

        public struct Named
        {
            public int Value;
            public override string ToString() => "N" + Value;
        }

        public struct Overriding
        {
            public int Value;
            public string Text;
            public override bool Equals(object? other) => base.Equals(other);
            public override int GetHashCode() => base.GetHashCode();
            public Func<object?, bool> BaseEquals() => base.Equals;
            public Func<int> BaseHash() => base.GetHashCode;
        }

        public interface IShape { int Area(); }

        public interface IGreeter
        {
            int Value() => 40;
        }

        public sealed class Plain : IGreeter { }

        public sealed class Square(int side) : IShape
        {
            public int Area() => side * side;
        }

        public abstract class Shape
        {
            public abstract int Area();
            public virtual int Get<T>() => typeof(T) == typeof(int) ? 1 : 2;
        }

        public sealed class Circle : Shape
        {
            public override int Area() => 3;
            public override int Get<T>() => base.Get<T>() + 10;
        }

        public class Base
        {
            public virtual int Value() => 1;
        }

        public sealed class Derived : Base
        {
            public override int Value() => 2;

            public int ThroughBase()
            {
                Func<int> value = base.Value;
                return value() * 10 + Value();
            }

            public Func<object> Cloner() => MemberwiseClone;
        }

        public interface IZero<TSelf> where TSelf : IZero<TSelf>
        {
            static abstract int Zero();
        }

        public struct Seven : IZero<Seven>
        {
            public static int Zero() => 7;
        }

        public class Eight : IZero<Eight>
        {
            public static int Zero() => 8;
        }

        public sealed class Nine : IZero<Nine>
        {
            public static int Zero() => 9;
        }

        // Keeps a delegate or function pointer opaque, so it is called through its address rather
        // than folded back into a call.
        public static unsafe class Escape
        {
            public static Delegate? Last;
            public static nint Pointer;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static T Keep<T>(T value) where T : Delegate
            {
                Last = value;
                return value;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static double Call(delegate*<double, double> function, double value)
            {
                Pointer = (nint)function;
                return function(value);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Call(delegate*<ref int, int> function, ref int value)
            {
                Pointer = (nint)function;
                return function(ref value);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static string Call(delegate*<object, string> function, object value)
            {
                Pointer = (nint)function;
                return function(value);
            }
        }

        public delegate ref int ArrayHead(int[] array);

        public static class Extensions
        {
            public static int Twice(this string text) => text.Length * 2;
        }

        public static class Holder
        {
            public static int Field;
            public static ref int Get() => ref Field;
        }

        public delegate ref int RefGetter();

        public delegate void Producer(out int value);

        public static partial class EntryPoint
        {
            public static int Trace() => 0;

            private static string Text(int value) => "t" + value;

            private static int Measure(object value) => value is string text ? text.Length : -1;

            private static void Produce(out int value) => value = 9;

            private static int Of<T>() where T : IZero<T>
            {
                Func<int> zero = T.Zero;
                return zero();
            }

            private static int Call<T>() where T : IZero<T> => T.Zero();

            private static unsafe int Pointer<T>() where T : IZero<T>
            {
                delegate*<int> zero = &T.Zero;
                return zero();
            }
        }

        """;

    private static readonly Dictionary<string, string> Programs = new()
    {
        ["StructBaseCalls"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    var first = new Overriding { Value = input, Text = "a" + input };
                    var same = new Overriding { Value = input, Text = "a" + input };
                    var other = new Overriding { Value = input + 1, Text = "b" };
                    return (first.Equals(same) ? 1 : 0) + (first.Equals(other) ? 10 : 0) +
                        (first.GetHashCode() == same.GetHashCode() ? 100 : 0) +
                        (first.BaseEquals()(same) ? 1000 : 0) + (first.BaseHash()() == same.GetHashCode() ? 10000 : 0);
                }
            }
            """,
        ["ObjectDefaults"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = new object();
                    Func<int> hash = value.GetHashCode;
                    Func<object?, bool> equals = value.Equals;
                    Func<string?> text = value.ToString;
                    return (hash() == value.GetHashCode() ? 1 : 0) + (equals(value) ? 10 : 0) + (equals(input) ? 100 : 0) +
                        text()!.Length * 1000;
                }
            }
            """,
        ["NullTargets"] = """
            public sealed class Box
            {
                public int Value;
                public int Read() => Value;
                public override string ToString() => "box";
            }

            public static partial class EntryPoint
            {
                // ArgumentException where a delegate closes over a null instance target, NullReferenceException
                // where ldvirtftn reads one, and none where a static method closes over null.
                private static int Outcome(Func<object> create)
                {
                    try
                    {
                        return create() is null ? 9 : 1;
                    }
                    catch (NullReferenceException)
                    {
                        return 2;
                    }
                    catch (ArgumentException)
                    {
                        return 3;
                    }
                }

                public static int Run(int input)
                {
                    Func<int, int>? none = input > 100 ? x => x : null;
                    Box? box = input > 100 ? new Box() : null;
                    string? text = input > 100 ? "t" : null;
                    var invoke = Outcome(() => new Func<int, int>(none!));
                    var instance = Outcome(() => new Func<int>(box!.Read));
                    var virtualMethod = Outcome(() => new Func<string>(box!.ToString));
                    var closedStatic = Outcome(() => new Func<int>(text!.Twice));
                    return invoke * 1000 + instance * 100 + virtualMethod * 10 + closedStatic;
                }
            }
            """,
        ["MathIntrinsics"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    Func<double, double> sqrt = Math.Sqrt;
                    Func<int, int, int> max = Math.Max;
                    Func<int, int> abs = Math.Abs;
                    Func<float, float> floor = MathF.Floor;
                    return (int)sqrt(input * input) * 1000 + max(input, 3) * 100 + abs(-input) * 10 + (int)floor(input + 0.5f);
                }
            }
            """,
        ["FunctionPointersToIntrinsics"] = """
            public static unsafe partial class EntryPoint
            {
                public static int Run(int input)
                {
                    var count = input;
                    return (int)Escape.Call(&Math.Sqrt, input * input) * 100 + Escape.Call(&Interlocked.Increment, ref count) * 10 +
                        Escape.Call(&Unsafe.As<string>, "abc").Length;
                }
            }
            """,
        ["EscapingIntrinsicAddresses"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    object boxed = input;
                    var type = Escape.Keep<Func<Type>>(boxed.GetType);
                    var fromHandle = Escape.Keep<Func<RuntimeTypeHandle, Type?>>(Type.GetTypeFromHandle);
                    var cast = Escape.Keep<Func<object, string>>(Unsafe.As<string>);
                    var references = Escape.Keep<Func<bool>>(RuntimeHelpers.IsReferenceOrContainsReferences<string>);
                    var noReferences = Escape.Keep<Func<bool>>(RuntimeHelpers.IsReferenceOrContainsReferences<int>);
                    var head = Escape.Keep<ArrayHead>(System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference);
                    var copy = Escape.Keep<Func<object>>(new Derived().Cloner());
                    int[] array = [input, 2];
                    head(array) = 40;
                    return (type() == typeof(int) ? 1 : 0) + (fromHandle(typeof(string).TypeHandle) == typeof(string) ? 10 : 0) +
                        cast("abcd").Length * 100 + (references() ? 1000 : 0) + (noReferences() ? 0 : 10000) +
                        array[0] * 100000 + (copy() is Derived ? 10000000 : 0);
                }
            }
            """,
        ["ObjectMethods"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    object boxed = input;
                    var text = "x" + input;
                    Func<Type> type = boxed.GetType;
                    Func<object, object, bool> same = ReferenceEquals;
                    Func<object, int> hash = RuntimeHelpers.GetHashCode;
                    Func<string?> toText = boxed.ToString;
                    Func<object, bool> equals = boxed.Equals;
                    return (type() == typeof(int) ? 1 : 0) + (same(text, text) ? 10 : 0) +
                        (hash(text) == RuntimeHelpers.GetHashCode(text) ? 100 : 0) + toText()!.Length * 1000 +
                        (equals(input) ? 10000 : 0);
                }
            }
            """,
        ["EnumMethods"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    var color = (Color)(1 << (input % 3));
                    Func<string> text = color.ToString;
                    Func<Enum, bool> hasFlag = color.HasFlag;
                    Func<int> hash = color.GetHashCode;
                    Func<object?, int> compare = color.CompareTo;
                    Func<object?, bool> equals = color.Equals;
                    Func<TypeCode> code = color.GetTypeCode;
                    return text().Length * 10000 + (hasFlag(Color.Green) ? 1000 : 0) + hash() * 100 +
                        (compare(Color.Green) + 1) * 10 + (equals(Color.Blue) ? 1 : 0) + (int)code();
                }
            }
            """,
        ["ValueTypeMethods"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    var point = new Point { X = input, Y = 2 };
                    Func<object?, bool> equals = point.Equals;
                    Func<int> hash = point.GetHashCode;
                    Func<int> sum = point.Sum;
                    var named = new Named { Value = input };
                    Func<string> text = named.ToString;
                    Func<string> number = input.ToString;
                    Func<string?, string> formatted = input.ToString;
                    point.X = 100;
                    return (equals(new Point { X = input, Y = 2 }) ? 1 : 0) + (hash() == new Point { X = input, Y = 2 }.GetHashCode() ? 10 : 0) +
                        sum() * 100 + text().Length * 10000 + number().Length * 100000 + formatted("D3").Length * 1000000;
                }
            }
            """,
        ["VirtualDispatchKinds"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    IShape square = new Square(input);
                    Func<int> area = square.Area;
                    IGreeter greeter = new Plain();
                    Func<int> greeting = greeter.Value;
                    Shape circle = new Circle();
                    Func<int> abstractArea = circle.Area;
                    Func<int> generic = circle.Get<int>;
                    Func<int> genericOther = circle.Get<string>;
                    return area() * 1000 + greeting() * 10 + abstractArea() + generic() * 100000 + genericOther() * 10000000 +
                        new Derived().ThroughBase() * 1000000;
                }
            }
            """,
        ["MemberwiseClone"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    var original = new Derived();
                    var copy = original.Cloner()();
                    return (copy is Derived && !ReferenceEquals(copy, original) ? 1 : 0) + input;
                }
            }
            """,
        ["ClosedExtension"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    var text = new string('a', input);
                    Func<int> twice = text.Twice;
                    return twice();
                }
            }
            """,
        ["ConstrainedMethodGroups"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input) => Of<Seven>() * 100 + Of<Eight>() * 10 + Of<Nine>() + input * 1000;
            }
            """,
        ["ConstrainedCalls"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input) => Call<Seven>() * 100 + Call<Eight>() * 10 + Call<Nine>() + input * 1000;
            }
            """,
        ["ConstrainedPointers"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input) => Pointer<Seven>() * 100 + Pointer<Eight>() * 10 + Pointer<Nine>() + input * 1000;
            }
            """,
        ["DelegateMethods"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    Func<int, int> twice = x => x * 2;
                    Func<Delegate[]> list = twice.GetInvocationList;
                    Func<object?, bool> equals = twice.Equals;
                    Func<int> hash = twice.GetHashCode;
                    Func<Delegate?, Delegate?, Delegate?> combine = Delegate.Combine;
                    var both = (Func<int, int>)combine(twice, twice)!;
                    Func<int, AsyncCallback?, object?, IAsyncResult> begin = twice.BeginInvoke;
                    var unsupported = 0;
                    try
                    {
                        begin(input, null, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        unsupported = 1;
                    }
                    return list().Length + (equals(twice) ? 10 : 0) + (hash() == twice.GetHashCode() ? 100 : 0) +
                        both.GetInvocationList().Length * 1000 + both(input) * 10000 + unsupported * 1000000;
                }
            }
            """,
        ["VariantConversions"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    Func<int, string> text = Text;
                    Func<int, object> boxed = new Func<int, object>(text);
                    Func<object, int> measure = Measure;
                    Func<string, int> narrowed = new Func<string, int>(measure);
                    Func<object> getText = new Func<object>(() => "abcd");
                    Func<object> again = getText.Invoke;
                    return ((string)boxed(input)).Length * 1000 + narrowed("hello") * 100 + ((string)again()).Length;
                }
            }
            """,
        ["ByRefSignatures"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    RefGetter getter = Holder.Get;
                    getter() = input;
                    RefGetter wrapped = new RefGetter(getter);
                    wrapped() += 5;
                    Producer producer = Produce;
                    Producer wrappedProducer = producer.Invoke;
                    wrappedProducer(out var produced);
                    return Holder.Field * 100 + produced;
                }
            }
            """,
    };
}
