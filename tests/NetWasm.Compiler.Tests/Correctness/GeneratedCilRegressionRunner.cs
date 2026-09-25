using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class GeneratedCilRegressionRunner(
    ICorpusRunDirectoryFactory directories,
    IRoslynCorpusCompiler compiler,
    IMethodBodyPatcher patcher,
    ICompiledCorpusRunner compiled,
    IOracleModePolicyRegistry oracleModes,
    ICorpusRunDirectoryCleaner cleanup) : IGeneratedCilRegressionRunner
{
    public void RunRaw(int seed, ReadOnlySpan<byte> cil, ImmutableArray<int> inputs)
    {
        if (cil.IsEmpty || inputs.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "a generated CIL regression needs code and at least one input");
        }
        var fixture = RandomCilFixtureFactory.Create(seed) with
        {
            Inputs = inputs,
        };
        var root = directories.Create();
        try
        {
            foreach (var profile in CilProfiles.Roslyn)
            {
                var directory = Path.Combine(root, profile.ToString());
                var baseline = compiler.Compile(fixture, profile, directory);
                var patched = patcher.Patch(
                    baseline.Desktop.AssemblyPath,
                    fixture.EntryType,
                    "Run",
                    cil,
                    maxStack: 8);
                var artifact = baseline.Desktop with
                {
                    AssemblySha256 = patched.AssemblySha256,
                    CompilerOptions = baseline.Desktop.CompilerOptions.Add(
                        $"generated-regression-seed:{seed}"),
                };
                var compilation = baseline with
                {
                    Desktop = artifact,
                    NetWasm = artifact,
                };
                oracleModes.Get(OracleMode.SameIl).ValidateCompilation(compilation);
                compiled.Run(compilation);
            }
            cleanup.Clean(root);
        }
        catch (Exception failure)
        {
            failure.Data["CorpusRunDirectory"] = root;
            throw;
        }
    }
}
