namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticFileExistenceReader
{
    bool Exists(string path);
}
