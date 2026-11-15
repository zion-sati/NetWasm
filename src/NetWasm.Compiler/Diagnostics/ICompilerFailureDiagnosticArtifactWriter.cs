// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerFailureDiagnosticArtifactWriter
{
    void WriteFailure(CompilerOptions options, string stage, Exception exception);
}
