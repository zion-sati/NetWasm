using System.Collections.Generic;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal interface ICompilationExportResolver
{
    IReadOnlyList<CompilationExportCandidate> Resolve(
        MetadataCompilationSnapshot metadata,
        IMethodFinder methods,
        CompilerOptions options);
}
