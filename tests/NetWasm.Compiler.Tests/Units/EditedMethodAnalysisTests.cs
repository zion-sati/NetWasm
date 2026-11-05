using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Pipeline;
using NetWasm.Compiler.Wasm.Units;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Units;

// The early cutoff re-analyses only edited methods. At O0's defaults no analysis crosses
// methods, so a method analysed alone must equal its analysis within the whole program.
public sealed class EditedMethodAnalysisTests
{
    [Fact]
    public void EveryMethodAnalysedAloneEqualsItsWholeProgramAnalysis()
    {
        using var assets = TestAssets.Create();
        var options = new CompilerOptions(assets.CompileOptimizedSource("EditedMethods", CompiledUnitRestoreTests.BaseSource),
            [assets.CoreLib], "EntryPoint", "Run", [], Optimization: FinalWasmOptimization.O0);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var program = services.GetRequiredService<INetWasmCompiler>().Compile(options).Program;
        using var lease = services.GetRequiredService<IMetadataCompilationLoader>().Load(options.EntryAssemblyPath, options.ReferencePaths);
        var preparation = services.GetRequiredService<ICompilationPreparationBuilder>().Prepare(lease.Snapshot, options);

        var definitions = program.Methods
            .Where(pair => !pair.Value.Method.IsConstructed && pair.Value.Method.Definition.GenericArity == 0)
            .Select(pair => pair.Key)
            .ToArray();
        var edited = services.GetRequiredService<IEditedMethodAnalyzer>()
            .Analyze(lease.Snapshot, preparation, definitions, program.AllocatedTypes);
        var alone = edited.Methods;

        Assert.True(definitions.Length > 50, $"only {definitions.Length} methods were compared");
        Assert.Equal(definitions.Length, alone.Length);
        Assert.Empty(edited.Unanalysed);
        var differing = alone
            .Where(analysis => Fingerprint(analysis.Body) != Fingerprint(program.Methods[analysis.Method.Definition.Key]))
            .Select(analysis => analysis.Method.CanonicalName)
            .ToArray();
        Assert.True(differing.Length == 0,
            $"{differing.Length} of {definitions.Length} methods analyse differently alone: " +
            string.Join(", ", differing.Take(8)));

        // Their sites, resolved against the program's allocated types, are the program's.
        var callers = alone.Select(analysis => analysis.Method.CanonicalName + "@").ToArray();
        bool Ours(string key) => callers.Any(caller => key.StartsWith(caller, StringComparison.Ordinal));
        var dispatch = program.DispatchCallSites.Where(pair => Ours(pair.Key)).ToArray();
        var typeTests = program.TypeTestSites.Where(pair => Ours(pair.Key)).ToArray();
        Assert.NotEmpty(dispatch);
        Assert.Equal(dispatch.Length, edited.Sites.DispatchCallSites.Count);
        Assert.Equal(typeTests.Length, edited.Sites.TypeTestSites.Count);
        foreach (var (key, site) in dispatch)
            Assert.True(Fingerprint(site) == Fingerprint(edited.Sites.DispatchCallSites[key]), $"dispatch site {key} differs");
        foreach (var (key, site) in typeTests)
            Assert.True(Fingerprint(site) == Fingerprint(edited.Sites.TypeTestSites[key]), $"type test {key} differs");
    }

    private static UnitFingerprint Fingerprint(NetWasm.Compiler.Core.DispatchCallSiteModel site)
    {
        var builder = new UnitFingerprintBuilder();
        builder.Add(site);
        return builder.Finish();
    }

    private static UnitFingerprint Fingerprint(NetWasm.Compiler.Core.TypeTestSiteModel site)
    {
        var builder = new UnitFingerprintBuilder();
        builder.Add(site);
        return builder.Finish();
    }

    private static UnitFingerprint Fingerprint(NetWasm.Compiler.ControlFlow.ManagedMethodBody body)
    {
        var builder = new UnitFingerprintBuilder();
        builder.Add(body);
        return builder.Finish();
    }
}
