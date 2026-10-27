using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerMetadataSnapshotBuilder
{
    CompilerMetadataArtifact BuildMetadata(
        MetadataCompilationSnapshot metadata,
        IMethodBodyReader bodies,
        NetWasm.Compiler.Core.ISymbolFormatter symbols);
}
