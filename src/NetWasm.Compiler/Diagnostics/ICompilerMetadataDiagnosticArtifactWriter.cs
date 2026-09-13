using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerMetadataDiagnosticArtifactWriter
{
    void WriteMetadata(
        CompilerOptions options,
        MetadataCompilationSnapshot metadata,
        IMethodBodyReader bodies,
        ISymbolFormatter symbols);
}
