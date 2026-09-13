using System.Collections.Generic;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticArtifactEnumerator
{
    IReadOnlyList<string> Enumerate(string directory);
}
