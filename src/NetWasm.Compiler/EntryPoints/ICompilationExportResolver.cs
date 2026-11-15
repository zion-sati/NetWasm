// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
