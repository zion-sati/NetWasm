using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// The correctness corpora compare each program with desktop .NET in a compiler
// process of their own. Here the optimization and CoreLib corpora compile in the
// test process to valid modules, on both targets at O3 and without optimization,
// so a compiler failure surfaces with its stack and coverage sees the passes.
public sealed class CorpusCompilationTests
{
    private static readonly Dictionary<string, (string Source, string Namespace, bool Unsafe)> Corpora = new()
    {
        ["Ascii"] = (AsciiCorpusTests.Source, "NetWasm.Correctness.AsciiCorpus", false),
        ["BinaryPrimitives"] = (BinaryPrimitivesCorpusTests.Source, "NetWasm.Correctness.BinaryPrimitivesCorpus", false),
        ["BitCast"] = (BitCastCorpusTests.Source, "NetWasm.Correctness.BitCast", false),
        ["FusedMultiplyAdd"] = (FusedMultiplyAddCorpusTests.Source, "NetWasm.Correctness.FusedMultiplyAdd", false),
        ["MemoryExtensions"] = (MemoryExtensionsCorpusTests.Source, "NetWasm.Correctness.MemoryExtensionsSearch", false),
        ["NativeWidth"] = (NativeWidthCorpusTests.Source, "NetWasm.Correctness.NativeWidth", false),
        ["NumericsVectorTypes"] = (NumericsVectorTypesCorpusTests.Source, "NetWasm.Correctness.NumericsVectorTypes", false),
        ["PortableNumerics"] = (PortableNumericsCorpusTests.Source, "NetWasm.Correctness.PortableNumerics", false),
        ["PrimitiveHashCode"] = (PrimitiveHashCodeCorpusTests.Source, "NetWasm.Correctness.PrimitiveHashCodes", false),
        ["SearchValues"] = (SearchValuesCorpusTests.Source, "NetWasm.Correctness.SearchValuesCorpus", false),
        ["SimdVectors"] = (SimdVectorCorpusTests.Source, "NetWasm.Correctness.SimdVectors", false),
        ["SinCos"] = (SinCosCorpusTests.Source, "NetWasm.Correctness.SinCos", false),
        ["SmallBlockCopy"] = (SmallBlockCopyCorpusTests.Source, "NetWasm.Correctness.SmallBlockCopies", false),
        ["SpanMemory"] = (SpanMemoryCorpusTests.Source, "NetWasm.Correctness.SpanMemory", true),
        ["StructCopy"] = (StructCopyCorpusTests.Source, "NetWasm.Correctness.StructCopies", false),
        ["VectorValueShapes"] = (VectorValueShapesCorpusTests.Source, "NetWasm.Correctness.VectorValueShapes", false),
    };

    public static TheoryData<string, WasmTarget, FinalWasmOptimization> Cases()
    {
        var cases = new TheoryData<string, WasmTarget, FinalWasmOptimization>();
        foreach (var name in Corpora.Keys)
        {
            cases.Add(name, WasmTarget.Wasm32, FinalWasmOptimization.O3);
            cases.Add(name, WasmTarget.Wasm64, FinalWasmOptimization.O3);
            cases.Add(name, WasmTarget.Wasm32, FinalWasmOptimization.None);
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CorpusCompilesToAValidModule(string corpus, WasmTarget target, FinalWasmOptimization preset)
    {
        var (source, ns, allowUnsafe) = Corpora[corpus];
        using var assets = TestAssets.Create();
        var assembly = allowUnsafe
            ? assets.CompileOptimizedUnsafeSource(corpus + "Corpus", source)
            : assets.CompileOptimizedSource(corpus + "Corpus", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], ns + ".EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var validated = services.GetRequiredService<IWasmTools>().Run("validate", module);
        Assert.True(validated.ExitCode == 0, validated.StandardError);
    }
}
