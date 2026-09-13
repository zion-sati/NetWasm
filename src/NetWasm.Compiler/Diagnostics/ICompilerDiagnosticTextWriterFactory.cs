using System.IO;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticTextWriterFactory
{
    TextWriter Create(string path);
}
