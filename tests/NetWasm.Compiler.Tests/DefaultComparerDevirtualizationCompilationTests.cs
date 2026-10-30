using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// EqualityComparer<T>.Default and Comparer<T>.Default calls reach the comparer
// selected for T directly, and an enum compared with its own type is not boxed.
public sealed class DefaultComparerDevirtualizationCompilationTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;

        public enum Shade : byte { Light = 1, Dark = 200 }

        public enum Level { Low, Mid = 3, High = 7 }

        public readonly struct Point : IEquatable<Point>, IComparable<Point>
        {
            public readonly int X;
            public readonly int Y;

            public Point(int x, int y) { X = x; Y = y; }

            public bool Equals(Point other) => X == other.X && Y == other.Y;

            public override bool Equals(object obj) => obj is Point other && Equals(other);

            public override int GetHashCode() => X * 31 + Y;

            public int CompareTo(Point other) => X != other.X ? X.CompareTo(other.X) : Y.CompareTo(other.Y);
        }

        public struct Plain
        {
            public int A;
            public short B;
        }

        public sealed class Name : IComparable<Name>
        {
            public readonly string Text;

            public Name(string text) => Text = text;

            public override bool Equals(object obj) => obj is Name other && other.Text == Text;

            public override int GetHashCode() => Text.Length;

            public int CompareTo(Name other) => other is null ? 1 : string.CompareOrdinal(Text, other.Text);
        }

        public class Token
        {
        }

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool Same<T>(T x, T y) => EqualityComparer<T>.Default.Equals(x, y);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Hash<T>(T x) => EqualityComparer<T>.Default.GetHashCode(x);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Order<T>(T x, T y) => Math.Sign(Comparer<T>.Default.Compare(x, y));

            private static int Bit(bool value, int shift) => value ? 1 << shift : 0;

            public static int Run(int input)
            {
                var token = new Token();
                var text = "n" + input;
                var bits =
                    Bit(Same(input, input & 3), 0) |
                    Bit(Same(text, "n" + (input & 3)), 1) |
                    Bit(Same(new Point(input, 1), new Point(input & 3, 1)), 2) |
                    Bit(Same(new Plain { A = input, B = 2 }, new Plain { A = input & 3, B = 2 }), 3) |
                    Bit(Same<int?>(input == 0 ? null : input, input & 3), 4) |
                    Bit(Same(new Name(text), new Name("n" + (input & 3))), 5) |
                    Bit(Same(token, input == 0 ? token : new Token()), 6) |
                    Bit(Same<Name>(null, input == 1 ? null : new Name(text)), 7) |
                    Bit(Same((Level)(input & 7), Level.High), 8) |
                    Bit(Same((Shade)input, Shade.Dark), 9);
                var hashes = Hash(input) + Hash(new Point(input, 2)) * 5 + Hash<int?>(input == 7 ? null : input) * 7 +
                    Hash(new Name(text)) * 11 + Hash((Level)(input & 7)) * 13 + Hash((Shade)input) * 17;
                var orders = Order(input, 2) + 3 * (Order(text, "n2") + 3 * (Order(new Point(input, 0), new Point(2, 0)) +
                    3 * (Order<int?>(input == 0 ? null : input, 2) + 3 * (Order(new Name(text), new Name("n2")) +
                    3 * (Order((Level)(input & 7), Level.Mid) + 3 * Order((Shade)input, Shade.Light))))));
                return bits + 1024 * (hashes & 0xffff) + 1024 * 65536 * (orders & 0x1f);
            }

            // An enum compared with a value of its own type.
            public static int SameEnums(int input)
            {
                var level = (Level)(input & 7);
                var shade = (input & 1) == 0 ? Shade.Light : Shade.Dark;
                return Bit(level.Equals(Level.High), 0) |
                    Bit(level.Equals((object)Level.Mid), 1) |
                    Bit(shade.Equals(Shade.Dark), 2) |
                    Bit(Shade.Light.Equals(shade), 3);
            }

            // Enum.Equals(object) is true only for a boxed value of the receiver's own
            // enum type.
            public static int Enums(int input)
            {
                var level = (Level)(input & 7);
                var shade = (input & 1) == 0 ? Shade.Light : Shade.Dark;
                return SameEnums(input) |
                    Bit(level.Equals((object)7), 4) |
                    Bit(shade.Equals((object)Level.Low), 5) |
                    Bit(level.Equals(null), 6) |
                    Bit(((Shade)(input & 1)).Equals((object)(Level)(input & 1)), 7);
            }
        }
        """;

    // A call on a default comparer is virtual in IL; at O3 none stays virtual.
    [Fact]
    public void O3CallsTheDefaultComparersDirectly()
    {
        var methods = CompileO3()
            .Where(method => method.Method.Definition.Name is "Same" or "Hash" or "Order")
            .ToArray();

        Assert.True(methods.Length >= 20, $"{methods.Length} comparer helpers were compiled");
        Assert.All(methods, method => Assert.DoesNotContain(method.Body.Instructions, instruction =>
            instruction is { Operation: CilOperation.CallVirtual, Operand: CilOperand.MethodInstance { Value: var target } } &&
            (target.DeclaringType.ElementType?.FullName ?? target.DeclaringType.FullName) is
                "System.Collections.Generic.EqualityComparer`1" or "System.Collections.Generic.Comparer`1"));
    }

    // Enum.Equals(object) with a value of the receiver's own enum type compares
    // the two values; nothing is boxed.
    [Fact]
    public void O3ComparesAnEnumWithItsOwnTypeWithoutBoxing()
    {
        var sameEnums = Assert.Single(CompileO3(), method => method.Method.Definition.Name == "SameEnums");

        Assert.DoesNotContain(sameEnums.Body.Instructions, instruction => instruction.Operation == CilOperation.Box);
    }

    // As desktop .NET computes them.
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Run", 0, 1811972207)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Run", 1, 1342407871)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Run", 2, 403081279)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Run", 7, 336910592)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Run", 300, 386492416)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Run", -5, 3618816)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3, "Run", 300, 386492416)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz, "Run", -5, 3618816)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Enums", 0, 8)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Enums", 1, 4)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Enums", 7, 5)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Enums", -5, 6)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3, "Enums", 7, 5)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz, "Enums", 2, 8)]
    public void ComparisonsMatchDesktop(
        WasmTarget target,
        FinalWasmOptimization optimization,
        string method,
        int input,
        int expected)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        Assert.Equal(expected, executor.Execute(new CompilationScenario(
            $"DefaultComparerDevirtualization_{method}_{target}_{optimization}",
            Source,
            "EntryPoint",
            Optimize: true,
            target,
            input,
            [])
        {
            EntryMethodName = method,
            Optimization = optimization,
        }));
    }

    private static ManagedMethodBody[] CompileO3()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("DefaultComparerDevirtualization", Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [
                new RequestedExport("enums", "EntryPoint", "Enums"),
                new RequestedExport("sameEnums", "EntryPoint", "SameEnums"),
            ],
            Optimization: FinalWasmOptimization.O3));
        return [.. compilation.Program.Methods.Values.Concat(compilation.Program.ConstructedMethods.Values)
            .Where(method => method.Method.DeclaringType.FullName == "EntryPoint")];
    }
}
