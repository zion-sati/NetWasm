namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticFileByteReader
{
    byte[] ReadBytes(string path);
}
