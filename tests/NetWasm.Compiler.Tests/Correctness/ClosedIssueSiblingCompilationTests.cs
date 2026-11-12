namespace NetWasm.Compiler.Tests.Correctness;

// Siblings of closed compiler issues: shapes no report named, in the same class as one that did.
// Each program runs as desktop .NET does. #147: virtual and interface methods bind as ECMA-335's
// resolution does. #43 and #196: multidimensional arrays. #42 and #212: methods without CIL
// bodies (UnsafeAccessor, Activator, Lazy). #40 and #41: struct receivers in filters, async
// methods and closures.
public sealed class ClosedIssueSiblingCompilationTests(CorrectnessTestRunner runner)
{
    public static TheoryData<string> Cases => [.. Programs.Keys];

    [Theory]
    [MemberData(nameof(Cases))]
    public void ASiblingOfAClosedIssueRunsAsDesktopDoes(string name) =>
        runner.Run(new("Sibling" + name, "Siblings", Prelude + Programs[name], [0, 3, 7])
        {
            AllowUnsafe = true,
            ReportAllMismatches = true,
        });

    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;
        namespace Siblings;

        public static partial class EntryPoint
        {
            public static int Trace() => 0;
        }

        """;

    private static readonly Dictionary<string, string> Programs = new()
    {
        // #147's class: default interface methods in a diamond, re-abstraction, and the most
        // specific implementation.
        ["DefaultInterfaceDiamond"] = """
            public interface IBase { int Value() => 1; }
            public interface ILeft : IBase { int IBase.Value() => 2; }
            public interface IRight : IBase { }
            public interface IBoth : ILeft, IRight { }
            public interface IAbstract : IBase { abstract int IBase.Value(); }
            public class Diamond : IBoth { }
            public class Concrete : IAbstract { public int Value() => 5; int IBase.Value() => 6; }
            public class Implicit : IAbstract { public int Value() => 7; }
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    IBase diamond = new Diamond();
                    IBase concrete = new Concrete();
                    IBase implicitly = new Implicit();
                    return diamond.Value() * 100 + concrete.Value() * 10 + implicitly.Value() + input * 1000;
                }
            }
            """,
        ["StaticVirtualDefaults"] = """
            public interface IShape<TSelf> where TSelf : IShape<TSelf>
            {
                static virtual int Sides() => 0;
                static abstract string Name();
                static virtual string Describe() => TSelf.Name() + TSelf.Sides();
            }
            public struct Triangle : IShape<Triangle> { public static int Sides() => 3; public static string Name() => "tri"; }
            public class Circle : IShape<Circle> { public static string Name() => "circle"; }
            public sealed class Square : IShape<Square>
            {
                public static int Sides() => 4;
                public static string Name() => "square";
                public static string Describe() => "SQ";
            }
            public static partial class EntryPoint
            {
                private static string Describe<T>() where T : IShape<T> => T.Describe();

                public static int Run(int input) =>
                    (Describe<Triangle>() + Describe<Circle>() + Describe<Square>()).GetHashCode() == 0 ? 0 :
                    Describe<Triangle>().Length * 100 + Describe<Circle>().Length * 10 + Describe<Square>().Length + input;
            }
            """,
        ["GenericVirtualHiding"] = """
            public class A { public virtual string M<T>() => "A" + Tag.Of<T>(); }
            public class B : A { public override string M<T>() => "B" + Tag.Of<T>(); }
            public class C : B { public new virtual string M<T>() => "C" + Tag.Of<T>(); }
            public class D : C { public override string M<T>() => "D" + Tag.Of<T>(); }
            // The type argument as text, from the type itself: a program carries no type names (issue 225).
            public static class Tag
            {
                public static string Of<T>() =>
                    typeof(T) == typeof(int) ? "Int32" : typeof(T) == typeof(string) ? "String" :
                    typeof(T) == typeof(long) ? "Int64" : typeof(T) == typeof(byte) ? "Byte" : "?";
            }
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    A viaA = new D();
                    B viaB = new D();
                    C viaC = new D();
                    var text = viaA.M<int>() + viaB.M<string>() + viaC.M<long>() + ((A)new C()).M<byte>();
                    return text.Length * 1000 + (text.StartsWith("BInt32") ? 100 : 0) + (text.Contains("DInt64") ? 10 : 0) + input;
                }
            }
            """,
        ["CovariantReturns"] = """
            public class Animal { public virtual Animal Make() => new Animal(); public virtual string Kind => "animal"; }
            public class Dog : Animal { public override Dog Make() => new Dog(); public override string Kind => "dog"; }
            public class Puppy : Dog { public override Puppy Make() => new Puppy(); public override string Kind => "puppy"; }
            public abstract class Box<T> { public abstract Box<T> Clone(); public virtual int Size => 1; }
            public class BigBox<T> : Box<T> { public override BigBox<T> Clone() => new BigBox<T>(); public override int Size => 2; }
            public record Shape(int Sides) { }
            public record Polygon(int Sides, string Label) : Shape(Sides);
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    Animal animal = new Puppy();
                    Dog dog = new Puppy();
                    Box<int> box = new BigBox<int>();
                    Shape shape = new Polygon(input, "p");
                    var copy = shape with { Sides = input + 1 };
                    return animal.Make().Kind.Length * 1000 + dog.Make().Kind.Length * 100 + box.Clone().Size * 10 +
                        (copy is Polygon polygon && polygon.Label == "p" ? copy.Sides : -1);
                }
            }
            """,
        ["VariantDispatch"] = """
            public interface IProducer<out T> { T Produce(); }
            public interface IConsumer<in T> { int Consume(T value); }
            public class Base { public virtual int Id => 1; }
            public class Derived : Base { public override int Id => 2; }
            public class Factory : IProducer<Derived> { public Derived Produce() => new Derived(); }
            public class Sink : IConsumer<Base> { public int Consume(Base value) => value.Id * 10; }
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    IProducer<Base> producer = new Factory();
                    IConsumer<Derived> consumer = new Sink();
                    IEnumerable<Base> bases = new List<Derived> { new Derived(), new Derived() };
                    Func<Derived> make = new Factory().Produce;
                    Func<Base> covariant = make;
                    Action<Derived> contravariant = new Action<Base>(value => input += value.Id);
                    contravariant(new Derived());
                    var count = 0;
                    foreach (var item in bases) count += item.Id;
                    return producer.Produce().Id * 1000 + consumer.Consume(new Derived()) + count * 100 + covariant().Id + input * 10000;
                }
            }
            """,
        ["UnifiedGenericInterfaces"] = """
            public interface IPair<T> { string Name(T value); }
            public class Both : IPair<int>, IPair<string>
            {
                string IPair<int>.Name(int value) => "first";
                string IPair<string>.Name(string value) => "second";
            }
            public class Generic<T> : IPair<T>
            {
                // The type argument as text, from the type itself: a program carries no type names (issue 225).
                public virtual string Name(T value) => "generic" + (typeof(T) == typeof(string) ? "String" : "?");
            }
            public class Closed : Generic<int>, IPair<long> { public string Name(long value) => "long"; public override string Name(int value) => "closed"; }
            public class Base2 : IPair<int> { public virtual string Name(int value) => "base"; }
            public class Reimplements : Base2, IPair<int> { string IPair<int>.Name(int value) => "re"; }
            public class Inherits : Base2 { public override string Name(int value) => "override"; }
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    IPair<int> different = new Both();
                    IPair<string> other = new Both();
                    IPair<int> same = new Closed();
                    IPair<long> wide = new Closed();
                    IPair<string> open = new Generic<string>();
                    IPair<int> reimplemented = new Reimplements();
                    IPair<int> inherited = new Inherits();
                    var text = different.Name(input) + other.Name("x") + same.Name(input) + wide.Name(input) + open.Name("o") + reimplemented.Name(input) + inherited.Name(input);
                    return text.Length * 100 + text.IndexOf("re", StringComparison.Ordinal) + input;
                }
            }
            """,
        // #43 and #196's class: multidimensional arrays.
        ["MultidimensionalArrays"] = """
            public struct Cell { public int Value; public string? Tag; }
            public static partial class EntryPoint
            {
                private static T Corner<T>(T[,] grid) => grid[grid.GetLength(0) - 1, grid.GetLength(1) - 1];

                public static int Run(int input)
                {
                    var grid = new int[3, 4];
                    for (nint i = 0; i < 3; i++)
                        for (long j = 0; j < 4; j++)
                            grid[i, j] = (int)(i * 10 + j) + input;
                    grid[(uint)1, (ulong)2] += 100;
                    ref var cell = ref grid[2, 3];
                    cell *= 2;
                    var cells = new Cell[2, 2, 2];
                    cells[1, 1, 1].Value = input;
                    cells[1, 0, 1].Tag = "t";
                    var strings = new string[2, 3];
                    strings[1, 2] = "end";
                    var copy = (int[,])grid.Clone();
                    var total = 0;
                    foreach (var value in copy) total += value;
                    var boxed = (Array)grid;
                    boxed.SetValue(77, 0L, 0L);
                    var read = (int)boxed.GetValue(0L, 0L)!;
                    var target = new int[3, 4];
                    Array.Copy(grid, target, 12);
                    return total + read + Corner(grid) * 3 + cells[1, 1, 1].Value * 5 + (cells[1, 0, 1].Tag?.Length ?? 0) +
                        Corner(strings)!.Length + grid.GetLowerBound(1) * 1000 + grid.GetUpperBound(1) * 100 +
                        target[1, 2] + grid.Rank * 10000;
                }
            }
            """,
        ["JaggedAndRankOneArrays"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    Array rankOne = new long[3];
                    rankOne.SetValue(5L, 1);
                    var jagged = new int[2][,];
                    jagged[1] = new int[2, 2];
                    jagged[1][1, 1] = input;
                    var mixed = new int[2, 2][];
                    mixed[1, 0] = new[] { 1, 2, input };
                    return (int)(long)rankOne.GetValue(1)! * 100 + rankOne.GetLowerBound(0) * 10 + jagged[1][1, 1] + mixed[1, 0][2] * 1000 +
                        (rankOne is long[] ? 1 : 0) + rankOne.Length * 100000;
                }
            }
            """,
        // #42 and #212's class: methods without CIL bodies.
        ["UnsafeAccessors"] = """
            public class Secret
            {
                private int _count = 3;
                private static string s_name = "secret";
                public static string CurrentName => s_name;
                private Secret(int count) => _count = count;
                public Secret() { }
                private int Add(int value) => _count += value;
                private static int Twice(int value) => value * 2;
            }
            public struct Point { private int _x; private int Scale(int factor) => _x * factor; }
            public static partial class EntryPoint
            {
                [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_count")]
                private static extern ref int Count(Secret secret);
                [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "s_name")]
                private static extern ref string Name(Secret? secret);
                [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
                private static extern Secret Create(int count);
                [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Add")]
                private static extern int Add(Secret secret, int value);
                [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Twice")]
                private static extern int Twice(Secret? secret, int value);
                [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_x")]
                private static extern ref int X(ref Point point);
                [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Scale")]
                private static extern int Scale(ref Point point, int factor);

                public static int Run(int input)
                {
                    var secret = Create(input);
                    Count(secret) += 1;
                    Name(null) = "changed";
                    var point = new Point();
                    X(ref point) = 4;
                    Func<Secret, int, int> add = Add;
                    return add(secret, 2) * 1000 + Twice(null, input) * 10 + Name(null).Length + Secret.CurrentName.Length + Scale(ref point, 3) * 100000;
                }
            }
            """,
        ["ActivatorAndLazy"] = """
            public class Plain { public int Value = 7; }
            public class Hidden { private Hidden() { } }
            public abstract class Shape { }
            public struct Pair { public int A; }
            public static partial class EntryPoint
            {
                private static T Make<T>() where T : new() => new T();

                private static int Outcome(Func<object?> create)
                {
                    // The created type, from the type itself: a program carries no type names (issue 225).
                    try { return create() is { } value ? value.GetType() == typeof(Plain) ? 5 : value.GetType() == typeof(Pair) ? 4 : 1 : 0; }
                    catch (MissingMethodException) { return 100; }
                    catch (MemberAccessException) { return 200; }
                    catch (Exception) { return 300; }
                }

                public static int Run(int input)
                {
                    var lazy = new Lazy<Plain>();
                    var lazyFactory = new Lazy<string>(() => "made" + input);
                    int[] outcomes =
                    [
                        Outcome(() => Activator.CreateInstance<Plain>()), Outcome(() => Activator.CreateInstance<Pair>()),
                        Outcome(() => Activator.CreateInstance<Hidden>()),
                        Outcome(() => Make<Plain>()), Outcome(() => Make<Pair>()),
                    ];
                    var result = lazy.Value.Value + lazyFactory.Value.Length * 10;
                    foreach (var outcome in outcomes) result = unchecked(result * 31 + outcome);
                    return result;
                }
            }
            """,
        // #40 and #41's class: struct receivers and copies in filters, async methods and closures.
        ["StructReceiversInRegions"] = """
            public struct Counter
            {
                public int Count;
                public bool Bump(int limit) => ++Count < limit;
                public readonly int Twice() => Count * 2;
            }
            public static partial class EntryPoint
            {
                private static async Task<int> AcrossAwait(int input)
                {
                    var counter = new Counter { Count = input };
                    counter.Bump(100);
                    await Task.CompletedTask;
                    counter.Bump(100);
                    ref var reference = ref counter;
                    reference.Bump(100);
                    await new ValueTask<int>(1);
                    return counter.Twice();
                }

                private static ValueTask<int> Valued(int input) => new(input + 1);

                public static int Run(int input)
                {
                    var counter = new Counter();
                    var filtered = 0;
                    for (var i = 0; i < 3; i++)
                    {
                        try { throw new InvalidOperationException(i.ToString()); }
                        catch (InvalidOperationException) when (counter.Bump(2)) { filtered++; }
                        catch (InvalidOperationException) { filtered += 10; }
                    }
                    var awaitedInFilter = 0;
                    try { throw new InvalidOperationException("filter"); }
                    catch (InvalidOperationException) when (Valued(counter.Count).Result > 0 && counter.Bump(100))
                    {
                        awaitedInFilter = Valued(counter.Twice()).GetAwaiter().GetResult();
                    }
                    var snapshot = counter;
                    Func<int> captured = () => snapshot.Twice() + snapshot.Count + awaitedInFilter;
                    counter.Bump(100);
                    var awaited = AcrossAwait(input).GetAwaiter().GetResult();
                    var valued = Valued(input).GetAwaiter().GetResult();
                    return filtered * 1000 + captured() * 100 + awaited * 10 + valued;
                }
            }
            """,
    };
}
