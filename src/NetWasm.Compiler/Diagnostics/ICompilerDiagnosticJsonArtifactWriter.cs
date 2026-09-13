namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticJsonArtifactWriter
{
    void WriteJson(string path, object value);
}
