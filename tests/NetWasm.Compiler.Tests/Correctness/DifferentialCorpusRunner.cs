using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class DifferentialCorpusRunner(
    ICorpusRunDirectoryFactory directories,
    IRoslynCorpusCompiler compiler,
    ICompiledCorpusRunner compiled,
    ICorpusMatrixExpander matrices,
    ICorpusRunDirectoryCleaner cleanup) : IDifferentialCorpusRunner
{
    public void Run(CorpusFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var profiles = fixture.Matrix is { } selection
            ? matrices.Expand(CorpusInputKind.CSharp, selection).Select(cell => cell.Profile).Distinct().ToImmutableArray()
            : CilProfiles.Roslyn;
        var root = directories.Create();
        try
        {
            foreach (var profile in profiles)
            {
                var directory = Path.Combine(root, profile.ToString());
                compiled.Run(compiler.Compile(fixture, profile, directory));
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
