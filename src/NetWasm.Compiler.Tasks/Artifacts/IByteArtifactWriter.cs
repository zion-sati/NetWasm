namespace NetWasm.Compiler.Tasks.Artifacts;

internal interface IByteArtifactWriter
{
    void Write(string path, byte[] bytes);
}
