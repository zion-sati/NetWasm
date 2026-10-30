using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class EqualityComparerCompilationTests
{
    [Fact]
    public void CompilerExecutesDefaultInt32Comparer()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "Int32ComparerFixture",
            """
            using System.Collections.Generic;
            namespace Int32ComparerFixture;
            public static class EntryPoint
            {
                public static int Run(int input) =>
                    EqualityComparer<int>.Default.Equals(input, 41) ? input : -1;
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "Int32ComparerFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(41, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Fact]
    public void CompilerExecutesDefaultNullableComparer()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "NullableComparerFixture",
            """
            using System.Collections.Generic;
            namespace NullableComparerFixture;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int? left = input;
                    int? right = 41;
                    return EqualityComparer<int?>.Default.Equals(left, right) ? input : -1;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NullableComparerFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(41, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Fact]
    public void CompilerExecutesDefaultTupleComparer()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "TupleComparerFixture",
            """
            using System.Collections.Generic;
            namespace TupleComparerFixture;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var left = (input, "value");
                    var right = (41, string.Concat("val", "ue"));
                    return EqualityComparer<(int, string)>.Default.Equals(left, right)
                        ? input
                        : -1;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "TupleComparerFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(41, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Fact]
    public void CompilerExecutesDirectAndBoxedEnumHashing()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "EnumHashFixture",
            """
            namespace EnumHashFixture;
            public enum State : long { Ready = 0x0000000100000002 }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    object boxed = State.Ready;
                    return State.Ready.GetHashCode() * 10 + boxed.GetHashCode();
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EnumHashFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(33, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Fact]
    public void CompilerExecutesClosedDefaultComparerMatrix()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "EqualityComparerFixture",
            """
            using System;
            using System.Collections.Generic;

            namespace EqualityComparerFixture;

            public enum State : short
            {
                None,
                Ready = 7,
                Done = 9,
            }

            public readonly struct EquatableValue(int number) : IEquatable<EquatableValue>
            {
                public int Number { get; } = number;
                public bool Equals(EquatableValue other) => Number == other.Number;
                public override bool Equals(object? value) =>
                    value is EquatableValue other && Equals(other);
                public override int GetHashCode() => Number * 17;
            }

            public sealed class ConstantComparer : IEqualityComparer<EquatableValue>
            {
                public bool Equals(EquatableValue left, EquatableValue right) =>
                    left.Number % 10 == right.Number % 10;
                public int GetHashCode(EquatableValue value) => 1;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var result = 0;
                    if (EqualityComparer<int>.Default.Equals(41, 41) &&
                        !EqualityComparer<int>.Default.Equals(41, 42)) result |= 1;
                    if (EqualityComparer<long>.Default.Equals(long.MaxValue, long.MaxValue) &&
                        EqualityComparer<ulong>.Default.GetHashCode(5) == 5) result |= 2;
                    if (EqualityComparer<float>.Default.Equals(float.NaN, float.NaN) &&
                        EqualityComparer<double>.Default.Equals(-0.0, 0.0) &&
                        EqualityComparer<double>.Default.GetHashCode(-0.0) ==
                        EqualityComparer<double>.Default.GetHashCode(0.0)) result |= 4;
                    if (EqualityComparer<string>.Default.Equals("equal", "equal") &&
                        !EqualityComparer<string>.Default.Equals("equal", null) &&
                        EqualityComparer<string>.Default.GetHashCode(null!) == 0) result |= 8;
                    int? empty = null;
                    int? first = 7;
                    int? second = 7;
                    if (EqualityComparer<int?>.Default.Equals(empty, null) &&
                        EqualityComparer<int?>.Default.Equals(first, second) &&
                        first.GetHashCode() == second.GetHashCode()) result |= 16;
                    var tuple = (41, "value");
                    var equalTuple = (41, string.Concat("val", "ue"));
                    if (EqualityComparer<(int, string)>.Default.Equals(tuple, equalTuple) &&
                        tuple.GetHashCode() == equalTuple.GetHashCode()) result |= 32;
                    if (EqualityComparer<State>.Default.Equals(State.Ready, State.Ready))
                        result |= 64;
                    if (!EqualityComparer<State>.Default.Equals(State.Ready, State.Done))
                        result |= 1024;
                    if (EqualityComparer<State>.Default.GetHashCode(State.Ready) ==
                        EqualityComparer<State>.Default.GetHashCode(State.Ready)) result |= 2048;
                    var left = new EquatableValue(21);
                    var right = new EquatableValue(21);
                    if (EqualityComparer<EquatableValue>.Default.Equals(left, right) &&
                        EqualityComparer<EquatableValue>.Default.GetHashCode(left) == 357) result |= 128;
                    IEqualityComparer<EquatableValue> custom = new ConstantComparer();
                    if (custom.Equals(new EquatableValue(1), new EquatableValue(11)) &&
                        !custom.Equals(new EquatableValue(1), new EquatableValue(2))) result |= 256;
                    object same = new object();
                    object different = new object();
                    if (EqualityComparer<object>.Default.Equals(same, same) &&
                        !EqualityComparer<object>.Default.Equals(same, different) &&
                        !EqualityComparer<object>.Default.Equals(null, same)) result |= 512;
                    return result + input - input;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EqualityComparerFixture.EntryPoint",
            "Run",
            []));

        // EnumEqualityComparer<State> compares a State with a State, so the
        // underlying values are compared without boxing for Enum.Equals.
        Assert.DoesNotContain(compilation.Program.DispatchCallSites.Values, site =>
            site.Declaration.Definition.Name == "Equals" &&
            site.Targets.Any(target =>
                target.ReceiverType.FullName == "EqualityComparerFixture.State" &&
                target.Method.DeclaringType.FullName == "System.Enum" &&
                target.Method.Definition.Name == "Equals"));
        Assert.Equal(4095, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Fact]
    public void CompilerDoesNotBoxClosedEquatableValueComparerCalls()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ValueComparerAllocationFixture",
            """
            using System;
            using System.Collections.Generic;

            namespace ValueComparerAllocationFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var equality = EqualityComparer<int>.Default;
                    var order = Comparer<int>.Default;
                    _ = equality.Equals(input, input);
                    _ = order.Compare(input, input);
                    var before = GC.GetTotalAllocatedBytes();
                    var checksum = 0;
                    for (var index = 0; index < 1_000; index++)
                    {
                        if (equality.Equals(index, index)) checksum++;
                        checksum += order.Compare(index, index + 1);
                    }
                    var allocated = GC.GetTotalAllocatedBytes() - before;
                    return allocated == 0 && checksum == 0 ? input - input : -1;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ValueComparerAllocationFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(0, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Fact]
    public void CompilerSelectsUpstreamDefaultComparerFamilies()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DefaultComparerFamilyFixture",
            """
            using System;
            using System.Collections.Generic;

            namespace DefaultComparerFamilyFixture;

            public enum State : short { Ready = 7, Done = 9 }

            public sealed class EquatableReference : IEquatable<EquatableReference>
            {
                public EquatableReference(int value) => Value = value;
                public int Value { get; }
                public bool Equals(EquatableReference? other) => other?.Value == Value;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var result = 0;
                    if (EqualityComparer<int>.Default is GenericEqualityComparer<int>) result |= 1;
                    if (Comparer<int>.Default is GenericComparer<int>) result |= 2;
                    if (EqualityComparer<int?>.Default is NullableEqualityComparer<int>) result |= 4;
                    if (Comparer<int?>.Default is NullableComparer<int>) result |= 8;
                    if (EqualityComparer<State>.Default is EnumEqualityComparer<State>) result |= 16;
                    if (EqualityComparer<object>.Default is ObjectEqualityComparer<object>) result |= 32;
                    if (Comparer<object>.Default is ObjectComparer<object>) result |= 64;
                    if (EqualityComparer<EquatableReference>.Default is GenericEqualityComparer<EquatableReference>) result |= 128;
                    if (EqualityComparer<string>.Default is IAlternateEqualityComparer<ReadOnlySpan<char>, string>) result |= 256;
                    if (Comparer<State>.Default.Compare(State.Ready, State.Done) < 0) result |= 512;
                    return result + input - input;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DefaultComparerFamilyFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(1023, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Fact]
    public void CompilerExecutesAlternateStringComparerContract()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "AlternateStringComparerFixture",
            """
            using System;
            using System.Collections.Generic;

            namespace AlternateStringComparerFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var alternate = (IAlternateEqualityComparer<ReadOnlySpan<char>, string>)EqualityComparer<string>.Default;
                    ReadOnlySpan<char> value = "value";
                    ReadOnlySpan<char> created = "new";
                    return alternate.Equals(value, "value") &&
                        !alternate.Equals(value, "other") &&
                        alternate.GetHashCode(value) == "value".GetHashCode() &&
                        alternate.Create(created) == "new"
                            ? input - input
                            : -1;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "AlternateStringComparerFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(0, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Fact]
    public void CompilerExecutesDelegateComparerIdentityContract()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DelegateComparerFixture",
            """
            using System;
            using System.Collections.Generic;

            namespace DelegateComparerFixture;

            public static class EntryPoint
            {
                private static bool Equal(int left, int right) => left == right;
                private static int Hash(int value) => value * 17;

                public static int Run(int input)
                {
                    Func<int, int, bool> equals = Equal;
                    Func<int, int> hash = Hash;
                    var first = EqualityComparer<int>.Create(equals, hash);
                    var second = EqualityComparer<int>.Create(equals, hash);
                    return first.Equals(second) && first.GetHashCode() == second.GetHashCode() &&
                        first.Equals(41, 41) && !first.Equals(41, 42) &&
                        first.GetHashCode(41) == 697
                            ? input - input
                            : -1;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DelegateComparerFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(0, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }
}
