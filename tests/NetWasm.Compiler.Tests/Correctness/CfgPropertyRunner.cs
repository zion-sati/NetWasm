namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class CfgPropertyRunner(
    ICorpusRunDirectoryFactory directories,
    IRoslynCorpusCompiler compiler,
    IDesktopOracleRunner desktop,
    IOracleComparer comparer,
    ICompiledCorpusComparisonRunner compiled,
    ICorpusRunDirectoryCleaner cleanup) : ICfgPropertyRunner
{
    public void Run(CfgPropertyCase property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var root = directories.Create();
        try
        {
            foreach (var profile in CilProfiles.Roslyn)
            {
                var directory = Path.Combine(root, profile.ToString());
                var compilation = compiler.Compile(property.Fixture with { ExecuteWasm64 = true }, profile, directory);
                var desktopResults = desktop.Run(compilation);
                foreach (var input in property.Fixture.Inputs)
                {
                    RequireEquivalent(
                        property,
                        profile,
                        input,
                        property.Expected[input],
                        desktopResults[input],
                        "reference interpreter versus desktop");
                }
                compiled.RunAgainstOracle(compilation, property.Expected);
            }
            cleanup.Clean(root);
        }
        catch (Exception failure)
        {
            failure.Data["CorpusRunDirectory"] = root;
            throw;
        }
    }

    private void RequireEquivalent(
        CfgPropertyCase property,
        CilProfile profile,
        int input,
        OracleObservation expected,
        OracleObservation actual,
        string boundary)
    {
        var comparison = comparer.Compare(expected, actual);
        if (!comparison.Equivalent)
        {
            throw new InvalidOperationException(
                $"{property.Name} {profile} input {input} {boundary}: " +
                comparison.Message);
        }
    }
}
