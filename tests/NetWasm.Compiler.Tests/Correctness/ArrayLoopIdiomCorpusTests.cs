using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

// A loop that stores one value into consecutive array elements, or copies
// elements between arrays at the same index, first does its in-bounds
// iterations in bulk. Every element type and every size the fill's chunks and
// loop handle match desktop, and so do the loops that throw part way: the
// elements stored before the throw, the exception and the counter a catch sees.
[Collection(CorrectnessTestGroup.Name)]
public sealed class ArrayLoopIdiomCorpusTests(CorrectnessTestRunner runner)
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace NetWasm.Correctness.ArrayLoopIdioms;

        public enum Shade : short { None, Light = 7, Dark = -3 }

        public static class EntryPoint
        {
            private static readonly int[] Sizes = [0, 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17, 31, 32, 33, 63, 64, 65, 100, 1000];

            public static int Run(int input)
            {
                var hash = 17;
                foreach (var size in Sizes)
                {
                    hash = Mix(hash, input switch
                    {
                        0 => FillBytes(size, (byte)(size + 3)),
                        1 => FillChars(size),
                        2 => FillShorts(size, (short)-size),
                        3 => FillInts(size, size * 7919),
                        4 => FillLongs(size, (long)size << 40 | 5),
                        5 => FillFloats(size, size * 0.25f),
                        6 => FillDoubles(size, -size * 1.5),
                        7 => FillBools(size),
                        8 => FillEnums(size),
                        9 => FillNativeInts(size, size - 50),
                        10 => FillPrefix(size),
                        11 => FillThrowing(size),
                        12 => FillNull(size),
                        13 => FillFrom(size, -1),
                        14 => FillFrom(size, size + 2),
                        15 => FillInclusive(size),
                        16 => CopyInts(size),
                        17 => CopyChars(size),
                        18 => CopyDoubles(size),
                        19 => CopySame(size),
                        20 => CopyThrowing(size),
                        _ => CopyNull(size),
                    });
                }
                return hash;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FillBytes(int size, byte value)
            {
                var array = new byte[size + 4];
                var end = size + 2;
                for (var j = 2; j < end; j++) array[j] = value;
                return Hash(array);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FillChars(int size)
            {
                var array = new char[size];
                for (var j = 0; j < array.Length; j++) array[j] = ' ';
                return Hash(array);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FillShorts(int size, short value)
            {
                var array = new short[size + 1];
                var end = size + 1;
                for (var j = 1; j < end; j++) array[j] = value;
                return Hash(array);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FillInts(int size, int value)
            {
                var array = new int[size + 3];
                for (var j = 3; j < array.Length; j++) array[j] = value;
                return Hash(array);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FillLongs(int size, long value)
            {
                var array = new long[size];
                for (var j = 0; j < size; j++) array[j] = value;
                return Hash(array);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FillFloats(int size, float value)
            {
                var array = new float[size];
                for (var j = 0; j < array.Length; j++) array[j] = value;
                return Hash(array);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FillDoubles(int size, double value)
            {
                var array = new double[size + 1];
                for (var j = 1; j < array.Length; j++) array[j] = value;
                return Hash(array);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FillBools(int size)
            {
                var array = new bool[size];
                for (var j = 0; j < array.Length; j++) array[j] = true;
                return Hash(array);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FillEnums(int size)
            {
                var array = new Shade[size];
                for (var j = 0; j < array.Length; j++) array[j] = Shade.Dark;
                return Hash(array);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FillNativeInts(int size, nint value)
            {
                var array = new nint[size];
                for (var j = 0; j < array.Length; j++) array[j] = value;
                // nint hashes differ between 32- and 64-bit targets.
                var hash = size;
                foreach (var element in array) hash = Mix(hash, (int)element);
                return hash;
            }

            // Only the first half is filled.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FillPrefix(int size)
            {
                var array = new int[size];
                var end = size / 2;
                for (var j = 0; j < end; j++) array[j] = -1;
                return Hash(array);
            }

            // The loop runs past the array's end: the elements before it are
            // stored, then the store at j == Length throws.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FillThrowing(int size)
            {
                var array = new ushort[size];
                var end = size + 5;
                var j = 0;
                try
                {
                    for (j = 1; j < end; j++) array[j] = 0xabcd;
                }
                catch (IndexOutOfRangeException)
                {
                    return Mix(Hash(array), j + 1000);
                }
                return Hash(array);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FillNull(int size)
            {
                int[]? array = size % 3 == 0 ? null : new int[size];
                var j = 0;
                try
                {
                    for (j = 0; j < size; j++) array![j] = 9;
                }
                catch (NullReferenceException)
                {
                    return j + 2000;
                }
                return Hash(array!);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FillFrom(int size, int start)
            {
                var array = new int[size];
                var j = 0;
                try
                {
                    for (j = start; j < size; j++) array[j] = 4;
                }
                catch (IndexOutOfRangeException)
                {
                    return Mix(Hash(array), j + 3000);
                }
                return Mix(Hash(array), j);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FillInclusive(int size)
            {
                var array = new int[1001];
                for (var j = 0; j <= 999; j++) array[j] = size;
                return Hash(array);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CopyInts(int size)
            {
                var source = Pattern(size + 2);
                var destination = new int[size + 2];
                var end = size + 2;
                for (var j = 2; j < end; j++) destination[j] = source[j];
                return Hash(destination);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CopyChars(int size)
            {
                var source = new char[size];
                for (var index = 0; index < size; index++) source[index] = (char)('a' + index % 26);
                var destination = new char[size];
                for (var j = 0; j < destination.Length; j++) destination[j] = source[j];
                return Hash(destination);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CopyDoubles(int size)
            {
                var source = new double[size];
                for (var index = 0; index < size; index++) source[index] = index * -0.5;
                var destination = new double[size];
                for (var j = 0; j < size; j++) destination[j] = source[j];
                return Hash(destination);
            }

            // One array is both: the copy changes nothing.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CopySame(int size)
            {
                var array = Pattern(size);
                for (var j = 0; j < array.Length; j++) array[j] = array[j];
                return Hash(array);
            }

            // The destination is shorter: the copy stops at its end and throws there.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CopyThrowing(int size)
            {
                var source = Pattern(size + 3);
                var destination = new int[size];
                var j = 0;
                try
                {
                    for (j = 0; j < source.Length; j++) destination[j] = source[j];
                }
                catch (IndexOutOfRangeException)
                {
                    return Mix(Hash(destination), j + 4000);
                }
                return Hash(destination);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CopyNull(int size)
            {
                int[]? source = size % 2 == 0 ? null : Pattern(size);
                var destination = new int[size];
                var j = 0;
                try
                {
                    for (j = 0; j < size; j++) destination[j] = source![j];
                }
                catch (NullReferenceException)
                {
                    return j + 5000;
                }
                return Hash(destination);
            }

            private static int[] Pattern(int size)
            {
                var array = new int[size];
                for (var index = 0; index < size; index++) array[index] = index * 31 - 7;
                return array;
            }

            private static int Hash<T>(T[] values)
            {
                var hash = values.Length;
                foreach (var value in values) hash = Mix(hash, value!.GetHashCode());
                return hash;
            }

            private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

            public static int Trace() => 0;
        }
        """;

    [Fact]
    public void ArrayLoopIdiomsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "ArrayLoopIdiomCorpus",
            "NetWasm.Correctness.ArrayLoopIdioms",
            Source,
            [.. Enumerable.Range(0, 22)])
        {
            Optimization = FinalWasmOptimization.O3,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EveryFillAndCopyLoopRunsInBulkAtO3(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ArrayLoopIdioms" + target, Source);

        var bulk = Compile(assets, assembly, target, null);
        var kept = Compile(assets, assembly, target,
            new WasmOptimizationOverrides(ArrayLoopIdioms: ManagedArrayLoopIdioms.Disabled));

        string[] loops =
        [
            "CopyChars", "CopyDoubles", "CopyInts", "CopyNull", "CopySame", "CopyThrowing", "FillBools", "FillBytes",
            "FillChars", "FillDoubles", "FillEnums", "FillFloats", "FillFrom", "FillInclusive", "FillInts", "FillLongs",
            "FillNativeInts", "FillNull", "FillPrefix", "FillShorts", "FillThrowing",
        ];
        Assert.Equal(loops, Bulk(bulk));
        Assert.Empty(Bulk(kept));
        foreach (var input in (int[])[1, 11, 20])
        {
            Assert.Equal(
                CompilerTestSupport.ExecuteWithStandardWasiNode(kept.ApplicationModule, assets.Directory, input, target, kept.StaticDataEnd),
                CompilerTestSupport.ExecuteWithStandardWasiNode(bulk.ApplicationModule, assets.Directory, input, target, bulk.StaticDataEnd));
        }
    }

    // The EntryPoint methods that call ArrayLoops.
    private static string[] Bulk(CompilationResult result) =>
        [.. result.Program.Methods.Values
            .Where(static method => method.Method.DeclaringType.FullName == "NetWasm.Correctness.ArrayLoopIdioms.EntryPoint" &&
                method.Body.Instructions.Any(static instruction => instruction.Operand is CilOperand.MethodInstance
                {
                    Value.DeclaringType.FullName: "System.Runtime.CompilerServices.ArrayLoops",
                }))
            .Select(static method => method.Method.Definition.Name)
            .Order()];

    private static CompilationResult Compile(TestAssets assets, string assembly, WasmTarget target,
        WasmOptimizationOverrides? overrides) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "NetWasm.Correctness.ArrayLoopIdioms.EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3, OptimizationOverrides: overrides));
}
