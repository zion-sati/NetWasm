namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusRunDirectoryFactory
{
    string Create();
}

internal sealed class CorpusRunDirectoryFactory : ICorpusRunDirectoryFactory
{
    public string Create() => Directory.CreateTempSubdirectory("netwasm-corpus-").FullName;
}
