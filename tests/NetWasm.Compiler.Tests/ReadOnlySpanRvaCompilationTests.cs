using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ReadOnlySpanRvaCompilationTests
{
    private const string Source = """
        using System;

        public static class EntryPoint
        {
            private static ReadOnlySpan<byte> Bytes => [1, 2];
            private static ReadOnlySpan<sbyte> SBytes => [-1, 2];
            private static ReadOnlySpan<bool> Booleans => [false, true];
            private static ReadOnlySpan<char> Characters => ['a', 'z'];
            private static ReadOnlySpan<short> Int16s => [-3, 4];
            private static ReadOnlySpan<ushort> UInt16s => [5, 6];
            private static ReadOnlySpan<int> Int32s => [-7, 8];
            private static ReadOnlySpan<uint> UInt32s => [9, 10];
            private static ReadOnlySpan<long> Int64s => [-11, 12];
            private static ReadOnlySpan<ulong> UInt64s => [13, 14];
            private static ReadOnlySpan<float> Singles => [1.5f, 2.5f];
            private static ReadOnlySpan<double> Doubles => [3.5, 4.5];

            public static int Run(int input)
            {
                if (Bytes[1] != 2 || SBytes[0] != -1 || !Booleans[1] ||
                    Characters[1] != 'z' || Int16s[0] != -3 || UInt16s[1] != 6 ||
                    Int32s[0] != -7 || UInt32s[1] != 10 || Int64s[0] != -11 ||
                    UInt64s[1] != 14 || Singles[0] != 1.5f || Doubles[1] != 4.5)
                {
                    return 1;
                }
                return input + 1;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void CompilerMaterializesEverySupportedPrimitiveRvaSpan(
        WasmTarget target,
        bool optimize)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var observed = executor.Execute(new CompilationScenario(
            $"ReadOnlySpanRvaPrimitives_{target}_{optimize}",
            Source,
            "EntryPoint",
            Optimize: optimize,
            target,
            41,
            []));

        Assert.Equal(42, observed);
    }

    [Fact]
    public void EveryPrimitiveGetterRetainsRvaDataWithoutCreateSpanCalls()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "ReadOnlySpanRvaStructure",
            Source);
        // Oz's static data-view elimination folds these constant reads away, so it stays off here;
        // StaticDataViewEliminatorTests covers it.
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            OptimizationOverrides: new(StaticDataViewElimination: ManagedStaticDataViewElimination.Disabled)));
        var creations = compilation.Program.Methods.Values
            .SelectMany(method => method.Body.Instructions)
            .Where(instruction =>
                instruction.Operation == CilOperation.CreateReadOnlySpanFromRva)
            .Select(instruction => Assert.IsType<CilOperand.ReadOnlySpanData>(
                instruction.Operand))
            .ToArray();

        // Roslyn uses the pointer constructor directly for byte-sized elements.
        Assert.Equal(
            ["primitive:char", "primitive:f4", "primitive:f8", "primitive:i2",
                "primitive:i4", "primitive:i8", "primitive:u2", "primitive:u4", "primitive:u8"],
            creations.Select(creation => creation.ElementType.CanonicalName).Order());
        Assert.All(creations, creation =>
        {
            Assert.True(creation.ElementCount > 0);
            Assert.Contains(
                creation.Field.Definition.Key,
                compilation.Program.Fields);
        });
        Assert.DoesNotContain(compilation.Program.Methods.Values
            .SelectMany(method => method.Body.Instructions), instruction =>
            instruction.Operation == CilOperation.Call &&
            instruction.Operand is CilOperand.MethodInstance method &&
            method.Value.Definition.Name == "CreateSpan");
    }

    [Fact]
    public void O3EliminatesNonescapingStaticDataViewBeforeLayout()
    {
        const string source = """
            using System;

            public static class EntryPoint
            {
                private static ReadOnlySpan<uint> Values => [10, 20];

                public static int Run(int input) => (int)Values[input];
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "ReadOnlySpanStaticDataElimination",
            source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            Optimization: FinalWasmOptimization.O3));
        var entry = compilation.Program.Methods.Values.Single(method =>
            method.Method.Definition.Name == "Run");

        var load = Assert.Single(entry.Body.Instructions.Where(instruction =>
            instruction.Operation == CilOperation.LoadStaticDataElement));
        var data = Assert.IsType<CilOperand.ReadOnlySpanData>(load.Operand);
        Assert.Equal("primitive:u4", data.ElementType.CanonicalName);
        Assert.Equal(2, data.ElementCount);
        Assert.DoesNotContain(entry.Body.Instructions, instruction =>
            instruction.Operation is
                CilOperation.LoadLocalAddress or
                CilOperation.StoreLocal or
                CilOperation.CreateReadOnlySpanFromRva);
        Assert.Equal(
            20,
            ExecuteWithNode(
                compilation.ApplicationModule,
                assets.Directory,
                1));
    }

    private const string LocalTableSource = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                ReadOnlySpan<double> table = [0.5, -0.5, 2.0];
                var picked = table[input & 1] + table[2] * table.Length;
                var total = (int)(picked * 4) + Sum(table);
                try
                {
                    return total + (int)table[input];
                }
                catch (IndexOutOfRangeException)
                {
                    return -total;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Sum(ReadOnlySpan<double> values)
            {
                var sum = 0.0;
                foreach (var value in values) sum += value;
                return (int)(sum * 10);
            }
        }
        """;

    // A span local held a byref, so it lived in the value frame; one assigned
    // once from RVA data is read as that data's address and length instead.
    [Fact]
    public void O3ReadsAnRvaSpanLocalAsItsAddressAndLength()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ReadOnlySpanRvaLocal", LocalTableSource);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            Optimization: FinalWasmOptimization.O3));
        var entry = compilation.Program.Methods.Values.Single(method =>
            method.Method.Definition.Name == "Run");

        Assert.DoesNotContain(entry.Body.Instructions, instruction =>
            instruction.Operation is CilOperation.LoadLocalAddress or CilOperation.StoreLocal &&
            instruction.Operand is CilOperand.Index local &&
            entry.Body.LocalSignatureTypes[local.Value].CanonicalName.Contains("System.ReadOnlySpan`1", StringComparison.Ordinal));
        Assert.Contains(entry.Body.Instructions, instruction =>
            instruction.Operation == CilOperation.LoadStaticFieldAddress);
        // The copy passed to Sum is the same RVA span, created where it is read.
        Assert.Single(entry.Body.Instructions, instruction =>
            instruction.Operation == CilOperation.CreateReadOnlySpanFromRva);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, true, 0, 46)]
    [InlineData(WasmTarget.Wasm32, true, 1, 42)]
    [InlineData(WasmTarget.Wasm32, true, 5, -42)]
    [InlineData(WasmTarget.Wasm64, true, 1, 42)]
    [InlineData(WasmTarget.Wasm32, false, 5, -42)]
    public void RvaSpanLocalsReadAsDesktopDoes(WasmTarget target, bool optimize, int input, int expected)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        Assert.Equal(expected, executor.Execute(new CompilationScenario(
            $"ReadOnlySpanRvaLocal_{target}_{optimize}",
            LocalTableSource,
            "EntryPoint",
            Optimize: optimize,
            target,
            input,
            [])));
    }

    [Fact]
    public void O3EliminatesEveryReachableDecimalPowersTableView()
    {
        const string source = """
            public static class EntryPoint
            {
                public static int Run(int input) =>
                    decimal.GetBits(input / 100m)[0];
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "DecimalStaticDataElimination",
            source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            Optimization: FinalWasmOptimization.O3));
        var division = Assert.Single(
            compilation.Program.Methods.Values,
            method => method.Method.Definition.Name == "VarDecDiv");

        Assert.Equal(
            3,
            division.Body.Instructions.Count(instruction =>
                instruction.Operation == CilOperation.LoadStaticDataElement));
        Assert.DoesNotContain(division.Body.Instructions, instruction =>
            instruction.Operation == CilOperation.Call &&
            instruction.Operand is CilOperand.MethodInstance method &&
            method.Value.Definition.Name == "get_UInt32Powers10");
    }
}
