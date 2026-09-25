namespace NetWasm.Compiler.Tests.Correctness;

internal interface IRegisteredCorpusRunner
{
    void Run(CorpusCaseManifest manifest, string cell);
    void Run(CorpusCaseManifest manifest, string cell, IEmittedAssemblyBuilder builder, int? input = null);
}

internal sealed class RegisteredCorpusRunner(
    ICorpusCaseFixtureFactory fixtures,
    IFileCorpusRunner files,
    IEmittedCorpusRunner emitted) : IRegisteredCorpusRunner
{
    public void Run(CorpusCaseManifest manifest, string cell)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.InputKind != CorpusInputKind.CSharp)
        {
            throw new ArgumentException("A source case is required by the file authoring path.", nameof(manifest));
        }
        files.Run(fixtures.Create(manifest, cell), manifest.SourceFiles);
    }

    public void Run(CorpusCaseManifest manifest, string cell, IEmittedAssemblyBuilder builder, int? input = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(builder);
        if (manifest.InputKind != CorpusInputKind.Emitted)
        {
            throw new ArgumentException("An emitted case is required by the emitted authoring path.", nameof(manifest));
        }
        emitted.Run(fixtures.Create(manifest, cell, input), builder);
    }
}
