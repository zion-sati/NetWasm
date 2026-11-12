using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

// A static field no reachable code writes reads as its default. What the fold must leave, each
// against desktop: a field a static constructor initializes, one written through a ref, by
// Interlocked, by a lambda, through an out argument, by an override reached only by dispatch, by
// Volatile.Write and in one generic instantiation; and a type's static constructor must still run
// when an unwritten field of it is first read.
public sealed class UnwrittenStaticFieldCorpusTests(CorrectnessTestRunner runner)
{
    private const string Namespace = "NetWasm.Correctness.UnwrittenStatics";

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading;

        namespace NetWasm.Correctness.UnwrittenStatics;

        // The fields nothing assigns are the subject.
        #pragma warning disable CS0649

        public enum Mode { None, Some }

        public static class EntryPoint
        {
            private static int s_never;
            private static string? s_neverName;
            private static long s_neverLong;
            private static Mode s_neverMode;
            private static bool s_neverFlag;
            private static double s_neverReal;
            private static int s_initialized = 5;
            private static int s_byRef;
            private static int s_interlocked;
            private static int s_lambda;
            private static int s_out;
            private static int s_volatile;
            internal static int s_virtual;

            public static int Trace() => 0;

            public static int Run(int input) => input switch
            {
                0 => Unwritten() + (s_neverName is null ? 10 : 20) + (int)s_neverLong + (int)s_neverMode +
                    (s_neverFlag ? 1000 : 0) + (s_neverReal == 0 ? 100 : 200),
                1 => s_initialized,
                2 => ByRef(),
                3 => Interlocked.Increment(ref s_interlocked) + s_interlocked,
                4 => Lambda(),
                5 => int.TryParse("6", out s_out) ? s_out : -1,
                6 => Virtual(new Writer()),
                7 => Volatiles(),
                8 => Generics(),
                9 => InitializedType.Value + Marker.Ran,
                10 => Environment.ExitCode,
                _ => throw new ArgumentOutOfRangeException(nameof(input)),
            };

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Unwritten() => s_never;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ByRef()
            {
                ref var slot = ref s_byRef;
                slot = 7;
                return s_byRef;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Lambda()
            {
                Action write = () => s_lambda = 4;
                write();
                return s_lambda;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Virtual(Base writer)
            {
                writer.Write();
                return s_virtual;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Volatiles()
            {
                Volatile.Write(ref s_volatile, 9);
                return Volatile.Read(ref s_volatile);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Generics()
            {
                Generic<int>.Value = 3;
                return Generic<int>.Value * 10 + Generic<string>.Value;
            }
        }

        public abstract class Base
        {
            public abstract void Write();
        }

        public sealed class Writer : Base
        {
            public override void Write() => EntryPoint.s_virtual = 8;
        }

        public static class Generic<T>
        {
            public static int Value;
        }

        public static class Marker
        {
            public static int Ran;
        }

        // An explicit static constructor runs at the first access to the type's statics.
        public static class InitializedType
        {
            public static int Value;

            static InitializedType()
            {
                Marker.Ran = 3;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void UnwrittenStaticFieldsMatchDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "UnwrittenStaticFieldCorpus" + optimization,
            Namespace,
            Source,
            [.. Enumerable.Range(0, 11)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    // The unwritten field's load is gone with the fold on and stays with it off, and both read the
    // same values.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AnUnwrittenFieldsLoadGoesAndTheProgramReadsTheSame(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("UnwrittenStatics" + target, Source);

        var folded = Compile(assets, assembly, target, null);
        var kept = Compile(assets, assembly, target,
            new WasmOptimizationOverrides(UnwrittenStaticFieldFolding: ManagedUnwrittenStaticFieldFolding.Disabled));

        Assert.Equal(0, StaticLoads(folded, "Unwritten"));
        Assert.Equal(1, StaticLoads(kept, "Unwritten"));
        foreach (var input in Enumerable.Range(0, 11))
        {
            Assert.Equal(
                CompilerTestSupport.ExecuteWithStandardWasiNode(kept.ApplicationModule, assets.Directory, input, target, kept.StaticDataEnd),
                CompilerTestSupport.ExecuteWithStandardWasiNode(folded.ApplicationModule, assets.Directory, input, target, folded.StaticDataEnd));
        }
    }

    private static int StaticLoads(CompilationResult result, string name) =>
        result.Program.Methods.Values
            .Single(method => method.Method.DeclaringType.FullName == Namespace + ".EntryPoint" &&
                method.Method.Definition.Name == name)
            .Body.Instructions
            .Count(static instruction => instruction.Operation == CilOperation.LoadStaticField);

    private static CompilationResult Compile(
        TestAssets assets,
        string assembly,
        WasmTarget target,
        WasmOptimizationOverrides? overrides) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], Namespace + ".EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.Oz, OptimizationOverrides: overrides));
}
