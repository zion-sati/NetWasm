using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusCompilerRequestWriter
{
    void Write(string path, CorpusCompilerRequest request);
}

internal sealed class CorpusCompilerRequestWriter : ICorpusCompilerRequestWriter
{
    public void Write(string path, CorpusCompilerRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(request);
        File.WriteAllText(path, JsonSerializer.Serialize(request));
    }
}
