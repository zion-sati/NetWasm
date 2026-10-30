using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// foreach over an array seen as IEnumerable<T>, from a field, a property, a
// local, a loop or a getter the inliner keeps, and ICollection<T>.Count: the array's interface methods do
// not depend on its element type, so each call reaches the array helper
// directly and the enumerator's calls become direct in turn. A covariant
// array and a null one behave as on desktop .NET.
public sealed class ArrayInterfaceDevirtualizationCompilationTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;

        public sealed class Holder
        {
            private readonly int[] _values = { 2, 7, 1, 8 };

            public int Member(int input)
            {
                IEnumerable<int> values = _values;
                var sum = 0;
                foreach (var value in values) sum += value + input;
                return sum;
            }
        }

        // A static constructor keeps calls to the getter, and NoInlining keeps the other.
        public static class Initialized
        {
            private static readonly int[] s_values;

            static Initialized() => s_values = new[] { 5, 6, 7 };

            private static IEnumerable<int> Values() => s_values;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static IEnumerable<int> Opaque() => s_values;

            public static int Sum()
            {
                var sum = 0;
                foreach (var value in Values()) sum += value;
                foreach (var value in Opaque()) sum += value * 2;
                return sum;
            }
        }

        public static class EntryPoint
        {
            private static int[] s_values = { 3, 1, 4, 1, 5, 9, 2, 6 };
            private static int[] s_none;

            private static IEnumerable<int> Values() => s_values;

            public static int Run(int input)
            {
                IEnumerable<int> values = s_values;
                var sum = 0;
                foreach (var value in values) sum += value * input;
                foreach (var value in Values()) sum += value;
                for (var round = 0; round < 3; round++)
                {
                    IEnumerable<int> each = Values();
                    foreach (var value in each) sum += value + round;
                }
                ICollection<int> collection = s_values;
                sum += collection.Count * 100;
                object[] names = new string[] { "a", "bc", "def" };
                IEnumerable<object> objects = names;
                foreach (var name in objects) sum += ((string)name).Length * 1000;
                sum += new Holder().Member(input) * 10000;
                sum += Initialized.Sum() * 7;
                try
                {
                    IEnumerable<int> none = s_none;
                    foreach (var value in none) sum += value;
                }
                catch (NullReferenceException)
                {
                    sum += 1000000;
                }
                return sum;
            }
        }
        """;

    [Fact]
    public void O3CallsAnArraysInterfaceMethodsDirectly()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ArrayInterfaceDevirtualization", Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: FinalWasmOptimization.O3));
        var methods = compilation.Program.Methods.Values
            .Where(method => method.Method.Definition.Name is "Run" or "Member" or "Sum")
            .ToArray();

        Assert.Equal(3, methods.Length);
        var remaining = (
            from method in methods
            from instruction in method.Body.Instructions
            where instruction is { Operation: CilOperation.CallVirtual, Operand: CilOperand.MethodInstance { Value: var target } } &&
                (target.DeclaringType.ElementType?.FullName ?? target.DeclaringType.FullName) is
                    "System.Collections.Generic.IEnumerable`1" or "System.Collections.Generic.IEnumerator`1" or
                    "System.Collections.IEnumerator" or "System.IDisposable" or "System.Collections.Generic.ICollection`1"
            select $"{method.Method.Definition.Name}: {((CilOperand.MethodInstance)instruction.Operand).Value.Definition.Name}").ToArray();
        Assert.True(remaining.Length == 0, string.Join(", ", remaining));
    }

    // As desktop .NET computes them.
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, 0, 1187326)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, 1, 1227357)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, 7, 1467543)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, -3, 1067233)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3, 2, 1267388)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz, 1000, 41218326)]
    public void ArrayEnumerationMatchesDesktop(WasmTarget target, FinalWasmOptimization optimization, int input, int expected)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        Assert.Equal(expected, executor.Execute(new CompilationScenario(
            $"ArrayInterfaceDevirtualization_{target}_{optimization}",
            Source,
            "EntryPoint",
            Optimize: true,
            target,
            input,
            [])
        {
            Optimization = optimization,
        }));
    }
}
