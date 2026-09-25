using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusSourceArtifactWriter
{
    CorpusSourceArtifact Write(CorpusSourceFile source, string directory);
}

internal sealed class CorpusSourceArtifactWriter(ICorpusSourceNamesVerifier names) : ICorpusSourceArtifactWriter
{
    private static readonly UTF8Encoding SourceEncoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public CorpusSourceArtifact Write(CorpusSourceFile source, string directory)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Content);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        names.Verify([source.Name]);
        var bytes = SourceEncoding.GetBytes(source.Content);
        var path = Path.Combine(directory, source.Name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            stream.Write(bytes);
        }
        return new(source.Name, path, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
}
