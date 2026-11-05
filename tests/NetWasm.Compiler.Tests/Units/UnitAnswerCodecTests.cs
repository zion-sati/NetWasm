using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Pipeline;
using NetWasm.Compiler.Wasm.Units;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Units;

// A store keeps each recorded answer's value; reading it back and writing it again must
// give the bytes kept, for every kind the early cutoff replays.
public sealed class UnitAnswerCodecTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void EveryKeptAnswerReadsBackAsItWasWritten(WasmTarget target, bool stackTrace)
    {
        using var assets = TestAssets.Create();
        const string Name = "answers";
        var memory = new UnitStoreMemory();
        var options = new CompilerOptions(assets.CompileOptimizedSource("UnitAnswers", CompiledUnitRestoreTests.BaseSource),
            [assets.CoreLib], "EntryPoint", "Run", [], Target: target, EmitStackTrace: stackTrace,
            Optimization: FinalWasmOptimization.O0)
        {
            IntermediateOutputPath = Name,
            CompiledUnits = true,
        };
        var services = new ServiceCollection().AddNetWasmCompiler();
        services.UseUnitMemory(memory);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<INetWasmCompiler>().Compile(options);

        using var lease = provider.GetRequiredService<IMetadataCompilationLoader>().Load(options.EntryAssemblyPath, options.ReferencePaths);
        var methods = provider.GetRequiredService<IMethodRepositoryFactory>().Create(lease.Snapshot);
        var fields = provider.GetRequiredService<IFieldRepositoryFactory>().Create(lease.Snapshot);
        using var store = new MemoryUnitStoreLoader(memory).Load(Name);
        var kept = new Dictionary<UnitQueryKind, int>();
        var bare = new Dictionary<UnitQueryKind, int>();
        foreach (var segment in store.Segments)
        {
            if (segment is null) continue;
            for (var id = 0; id < segment.QueryCount; id++)
            {
                var query = segment.Query(id);
                if (query.Value.IsEmpty)
                {
                    bare[query.Kind] = bare.GetValueOrDefault(query.Kind) + 1;
                    continue;
                }
                kept[query.Kind] = kept.GetValueOrDefault(query.Kind) + 1;
                var key = query.Key.ToString();
                var value = UnitAnswerCodec.Decode(query.Kind, key, query.Value, methods, fields);
                var again = UnitAnswerCodec.Encode(query.Kind, key, value);
                Assert.True(again is not null && query.Value.SequenceEqual(again), $"{query.Kind} '{query.Key.ToString()}' reads back differently");
            }
        }
        Assert.True(kept.Count > 10, "too few kinds kept values: " + string.Join(", ", kept.Keys));
        // The whole-program plans are kept once per compile, beside its module.
        Assert.Equal([UnitQueryKind.TargetPlans], bare.Keys);
    }
}

// A compile keeps its whole-program plans beside its module; read back, they must be the
// plans the compile emitted against.
public sealed class UnitPlanCodecTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void KeptPlansReadBackAsTheCompileUsedThem(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string Name = "plans";
        var memory = new UnitStoreMemory();
        var options = new CompilerOptions(assets.CompileOptimizedSource("UnitPlans", CompiledUnitRestoreTests.BaseSource),
            [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: FinalWasmOptimization.O0)
        {
            IntermediateOutputPath = Name,
            CompiledUnits = true,
        };
        var services = new ServiceCollection().AddNetWasmCompiler();
        services.UseUnitMemory(memory);
        CapturingUnitStates? captured = null;
        services.Replace(ServiceDescriptor.Singleton<ICompiledUnitStateFactory>(provider =>
            captured = new(ActivatorUtilities.CreateInstance<CompiledUnitStateFactory>(provider))));
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<INetWasmCompiler>().Compile(options);
        var used = captured?.Last?.Target;
        Assert.NotNull(used);

        var kept = memory.Kept(Name);
        Assert.NotNull(kept);
        using var lease = provider.GetRequiredService<IMetadataCompilationLoader>().Load(options.EntryAssemblyPath, options.ReferencePaths);
        var methods = provider.GetRequiredService<IMethodRepositoryFactory>().Create(lease.Snapshot);
        var fields = provider.GetRequiredService<IFieldRepositoryFactory>().Create(lease.Snapshot);
        var decoded = UnitPlanCodec.Decode(kept.Plans, methods, fields);
        Assert.NotNull(decoded);
        Assert.Equal(kept.Plans, UnitPlanCodec.Encode(decoded.Target, decoded.RuntimeObjects, decoded.Program));
        Assert.NotEmpty(decoded.Program.AllocatedTypes);
        Assert.NotEmpty(decoded.Program.DirectMethods);
        var plans = decoded.Target;
        Assert.Equal(UnitTargetFingerprint.Of(used), UnitTargetFingerprint.Of(plans));
        var (expected, actual) = (used.FunctionIndices, plans.FunctionIndices);
        Assert.Equal(expected.DirectMethods.OrderBy(pair => pair.Key.ToString()), actual.DirectMethods.OrderBy(pair => pair.Key.ToString()));
        Assert.Equal(expected.ConstructedMethods.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actual.ConstructedMethods.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(expected.DelegateInvokeHelpers.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actual.DelegateInvokeHelpers.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(expected.ImportedMethods.OrderBy(pair => pair.Key.ToString()), actual.ImportedMethods.OrderBy(pair => pair.Key.ToString()));
        Assert.Equal(expected.JSImportForwarders.OrderBy(pair => pair.Key.ToString()), actual.JSImportForwarders.OrderBy(pair => pair.Key.ToString()));
        Assert.Equal(expected.ManagedThrowFunctionIndex, actual.ManagedThrowFunctionIndex);
        Assert.Equal(expected.ManagedRethrowFunctionIndex, actual.ManagedRethrowFunctionIndex);
        Assert.Equal(used.ModuleData.StaticDataEnd, plans.ModuleData.StaticDataEnd);
    }

    private sealed class CapturingUnitStates(ICompiledUnitStateFactory inner) : ICompiledUnitStateFactory
    {
        public CompiledUnitState? Last { get; private set; }

        public CompiledUnitState? Create(CompilerOptions options, MetadataCompilationSnapshot metadata,
            CompilationInputObservation? inputs, ReachableProgram program,
            NetWasm.Compiler.ComponentModel.Optimization.BranchPrefixDuplicationPolicy branchPrefixDuplication,
            NetWasm.Compiler.ComponentModel.Optimization.DispatcherEliminationPolicy dispatcherElimination) =>
            Last = inner.Create(options, metadata, inputs, program, branchPrefixDuplication, dispatcherElimination);
    }
}
