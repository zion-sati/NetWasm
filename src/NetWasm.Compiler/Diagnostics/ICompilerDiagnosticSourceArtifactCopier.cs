using System.IO;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticSourceArtifactCopier
{
    void CopySource(FileInfo source, string destination);
}
