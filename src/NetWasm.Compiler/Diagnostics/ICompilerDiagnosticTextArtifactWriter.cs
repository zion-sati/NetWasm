namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticTextArtifactWriter
{
    void WriteText(string path, string value);
}
