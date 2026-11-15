// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.EntryPoints;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerEntryDiagnosticArtifactWriter
{
    void WriteEntry(CompilerOptions options, ISymbolFormatter symbols, CompilationEntry entry);
}
