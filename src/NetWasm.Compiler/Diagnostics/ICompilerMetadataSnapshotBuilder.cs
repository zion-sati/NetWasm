// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerMetadataSnapshotBuilder
{
    CompilerMetadataArtifact BuildMetadata(
        MetadataCompilationSnapshot metadata,
        IMethodBodyReader bodies,
        NetWasm.Compiler.Core.ISymbolFormatter symbols);
}
