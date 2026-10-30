using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A struct element copied out of an array, a span or a pointer and read field
// by field loads only the fields it reads, and no whole copy: a struct of
// declared size too, an element changed after its copy, and a reference
// field. A sized struct whose whole value is copied again keeps its padding,
// and an element of a null array or out of range still throws.
public sealed class StructElementCopyCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;

        public static unsafe class EntryPoint
        {
            private struct S3
            {
                public byte A, B, C;
            }

            [StructLayout(LayoutKind.Sequential, Size = 29)]
            private struct S29
            {
                public byte A;
            }

            [StructLayout(LayoutKind.Sequential, Size = 24)]
            private struct Wide
            {
                public int A;
            }

            private struct Named
            {
                public string Name;
                public int Count;
            }

            private static S3[] Triples(int seed)
            {
                var items = new S3[37];
                for (var i = 0; i < items.Length; i++)
                    items[i] = new S3 { A = (byte)(seed + i), B = (byte)(i * 3), C = (byte)(seed ^ i) };
                return items;
            }

            private static S29[] Padded(int seed)
            {
                var items = new S29[11];
                var bytes = MemoryMarshal.AsBytes(items.AsSpan());
                for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)(seed * 5 + i * 13);
                return items;
            }

            private static Wide[] Wides(int seed)
            {
                var items = new Wide[5];
                var bytes = MemoryMarshal.AsBytes(items.AsSpan());
                for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)(seed * 3 + i * 7);
                return items;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SumArray(S3[] items)
            {
                var sum = 0;
                foreach (var item in items) sum += item.A | 1;
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SumSpan(ReadOnlySpan<S29> items)
            {
                var sum = 0;
                foreach (var item in items) sum = sum * 3 + item.A;
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Snapshot(S3[] items)
            {
                var sum = 0;
                for (var i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    items[i].A = 99;
                    items[i].C = 7;
                    sum += item.A * 7 + item.C;
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Names(Named[] items)
            {
                var sum = 0;
                for (var i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    var garbage = new int[64 + i];
                    GC.KeepAlive(garbage);
                    if (i % 5 == 0) GC.Collect();
                    sum += item.Name.Length * 31 + item.Count;
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Tail(in Wide item) => Unsafe.Add(ref Unsafe.As<Wide, byte>(ref Unsafe.AsRef(in item)), 20);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Padding(ReadOnlySpan<Wide> items, int rounds)
            {
                var first = items[0];
                var sum = 0;
                for (var round = 0; round < rounds; round++)
                {
                    first.A += round * 5 + 1;
                    sum = sum * 3 + first.A;
                }
                var copy = first;
                return sum * 7 + Tail(copy);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Pointer(S3* items, int count)
            {
                var sum = 0;
                for (var i = 0; i < count; i++)
                {
                    var item = items[i];
                    sum += item.B;
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NullArray(S3[]? items, int index)
            {
                try
                {
                    var item = items![index];
                    return item.C * 3 + item.A;
                }
                catch (NullReferenceException)
                {
                    return -11;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int OutOfRange(S3[] items, int index)
            {
                try
                {
                    var item = items[index];
                    return item.B * 5 + item.C;
                }
                catch (IndexOutOfRangeException)
                {
                    return -13;
                }
            }

            public static int Run(int input)
            {
                var seed = input % 1000;
                switch (input / 1000)
                {
                    case 0: return SumArray(Triples(seed));
                    case 1: return SumSpan(Padded(seed));
                    case 2: return Snapshot(Triples(seed));
                    case 3:
                        var named = new Named[23];
                        for (var i = 0; i < named.Length; i++) named[i] = new Named { Name = new string('x', seed % 7 + i), Count = i * seed };
                        return Names(named);
                    case 4: return Padding(Wides(seed), seed % 9 + 2);
                    case 5:
                        var triples = Triples(seed);
                        fixed (S3* items = triples) return Pointer(items, triples.Length);
                    case 6: return NullArray(seed % 2 == 0 ? null : Triples(seed), seed % 37);
                    default: return OutOfRange(Triples(seed), seed);
                }
            }
        }
        """;

    private static readonly string[] FieldWise = ["Names", "NullArray", "OutOfRange", "Pointer", "Snapshot", "SumArray", "SumSpan"];

    // As desktop .NET computes them.
    public static TheoryData<int, int> Cases => new()
    {
        { 3, 795 },
        { 37, 2053 },
        { 200, 8085 },
        { 1003, 4062852 },
        { 1037, 13242502 },
        { 1200, 17404665 },
        { 2003, 6108 },
        { 2200, 64272 },
        { 3003, 10741 },
        { 3200, 61295 },
        { 4003, -1907483017 },
        { 4037, 412687296 },
        { 5003, 1998 },
        { 6003, 6 },
        { 6036, -11 },
        { 7003, 45 },
        { 7036, 540 },
        { 7037, -13 },
    };

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void O3LoadsOnlyTheFieldsACopiedElementReads(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource($"StructElementCopy_{target}", Source);

        var split = Compile(assets, assembly, target, FinalWasmOptimization.O3, null);
        var whole = Compile(assets, assembly, target, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(StructScalarReplacement: ManagedStructScalarReplacement.Disabled));

        Assert.Equal(["Padding"], WholeElementLoads(split));
        Assert.Equal([.. FieldWise.Append("Padding").Order()], WholeElementLoads(whole));
        foreach (var (input, expected) in Cases.Select(row => ((int)row[0], (int)row[1])))
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(
                split.ApplicationModule, assets.Directory, input, target, split.StaticDataEnd));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CopiedElementsMatchDesktopAtOz(int input, int expected)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource("StructElementCopyOz", Source);
        var result = Compile(assets, assembly, WasmTarget.Wasm32, FinalWasmOptimization.Oz, null);

        Assert.Equal(expected, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, input, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // The EntryPoint methods that still load a whole S3, S29, Wide or Named.
    private static string[] WholeElementLoads(CompilationResult result) =>
        [.. result.Program.Methods.Values
            .Where(static method => method.Method.DeclaringType.FullName == "EntryPoint" &&
                method.Body.Instructions.Any(static instruction =>
                    instruction is
                    {
                        Operation: CilOperation.LoadObject or CilOperation.LoadArrayElement,
                        Operand: CilOperand.TypeIdentity { Value: var type },
                    } &&
                    type.ToString().Split('+')[^1] is "S3" or "S29" or "Wide" or "Named"))
            .Select(static method => method.Method.Definition.Name)
            .Order()];

    private static CompilationResult Compile(
        TestAssets assets,
        string assembly,
        WasmTarget target,
        FinalWasmOptimization optimization,
        WasmOptimizationOverrides? overrides) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: optimization, OptimizationOverrides: overrides));
}
