// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// GC's generation members follow the collector the program is compiled for, which the compiler folds into CoreLib:
// GC.MaxGeneration is a constant, and only a program for a collector with generations asks the runtime for an
// object's generation.
public sealed class CollectorGenerationMembersCompilationTests
{
    private const string Generation = "gc_object_generation";

    [Theory]
    [InlineData(null, 0)]
    [InlineData("Compact", 0)]
    [InlineData("NetWasm", 0)]
    [InlineData("Bump", 0)]
    [InlineData("NetWasmGen", 1)]
    public void MaxGenerationIsTheCollectorsAndOnlyNetWasmGenAsksForAnObjectsGeneration(string? collector, int maxGeneration)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("GenerationMembers", Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [])
        {
            Collector = collector is null ? null : RuntimeCollector.Named(collector),
        });

        Assert.Equal(maxGeneration == 1,
            WasmModuleInspection.ReadFunctionImportNames(compilation.ApplicationModule).Contains(Generation));
        Assert.Equal(maxGeneration == 1, compilation.RuntimeFeatures.Contains(NetWasmRuntimeFeatureIds.ObjectGeneration));
        // The oracle host's collector has no generations, so every object is in generation 0 and every count is 0.
        Assert.Equal(100 * maxGeneration + 42,
            ExecuteWithStandardWasiNode(compilation.ApplicationModule, assets.Directory, 0, WasmTarget.Wasm32,
                compilation.StaticDataEnd));
    }

    // GC.Collect(0) collects the youngest generation alone: under NetWasmGen a minor collection, through the
    // runtime function only such a program imports; under any other collector every object, as GC.Collect() does.
    [Theory]
    [InlineData(null, false)]
    [InlineData("Compact", false)]
    [InlineData("NetWasm", false)]
    [InlineData("Bump", false)]
    [InlineData("NetWasmGen", true)]
    public void CollectingGenerationZeroIsAYoungCollectionOnlyUnderNetWasmGen(string? collector, bool young)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("YoungCollection", CollectingSource);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [])
        {
            Collector = collector is null ? null : RuntimeCollector.Named(collector),
        });

        var imports = WasmModuleInspection.ReadFunctionImportNames(compilation.ApplicationModule);
        Assert.Equal(young, imports.Contains("gc_collect_young"));
        Assert.Contains("collect", imports);
        Assert.Equal(young, compilation.RuntimeFeatures.Contains(NetWasmRuntimeFeatureIds.YoungCollection));
        Assert.Equal(42,
            ExecuteWithStandardWasiNode(compilation.ApplicationModule, assets.Directory, 0, WasmTarget.Wasm32,
                compilation.StaticDataEnd));
    }

    private const string CollectingSource = """
        using System;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                GC.Collect(0);
                GC.Collect(0, GCCollectionMode.Forced, blocking: true, compacting: false);
                GC.Collect(1);
                return 42 + input;
            }
        }
        """;

    private const string Source = """
        using System;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var kept = new object();
                var generation = GC.GetGeneration(kept);
                var counts = GC.CollectionCount(0) + GC.CollectionCount(GC.MaxGeneration) + GC.CollectionCount(2);
                GC.KeepAlive(kept);
                return 100 * GC.MaxGeneration + 42 + generation + counts + input;
            }
        }
        """;
}
