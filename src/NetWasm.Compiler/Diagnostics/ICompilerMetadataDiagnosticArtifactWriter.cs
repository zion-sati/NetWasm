// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
