// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerLayoutDiagnosticArtifactWriter
{
    void WriteLayouts(CompilerOptions options, ManagedLayoutSnapshot layouts);
}
