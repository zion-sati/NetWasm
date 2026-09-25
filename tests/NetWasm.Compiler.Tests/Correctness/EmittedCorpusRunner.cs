using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface IEmittedAssemblyBuilder
{
    ImmutableArray<byte> Build();
}

internal interface IEmittedCorpusRunner
{
    void Run(CorpusFixture fixture, IEmittedAssemblyBuilder builder);
}

internal sealed class EmittedCorpusRunner(
    ICorpusAssemblyWriter assemblies,
    ICompiledCorpusRunner compiled,
    ICorpusMatrixExpander matrices,
    ICorpusRunDirectoryCleaner cleanup) : IEmittedCorpusRunner
{
    public void Run(CorpusFixture fixture, IEmittedAssemblyBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(builder);
        if (fixture.Source != string.Empty || fixture.AdditionalSources.IsDefault || !fixture.AdditionalSources.IsEmpty ||
            fixture.OracleMode != OracleMode.SameIl ||
            fixture.SameSourceReason is not null ||
            fixture.FrozenOracleEvidencePath is not null)
        {
            throw new ArgumentException(
                "Emitted fixtures require same-IL execution without inline source or frozen evidence.", nameof(fixture));
        }
        if (fixture.Matrix is { } selection)
        {
            matrices.Expand(CorpusInputKind.Emitted, selection);
        }
        var persisted = assemblies.Write(builder.Build());
        try
        {
            compiled.Run(new CorpusCompilation(
                fixture,
                CilProfile.Emitted,
                persisted.Artifact,
                persisted.Artifact,
                persisted.Directory));
            cleanup.Clean(persisted.Directory);
        }
        catch (Exception failure)
        {
            failure.Data["CorpusRunDirectory"] = persisted.Directory;
            throw;
        }
    }
}
