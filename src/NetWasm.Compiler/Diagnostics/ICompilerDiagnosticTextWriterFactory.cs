// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.IO;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticTextWriterFactory
{
    TextWriter Create(string path);
}
