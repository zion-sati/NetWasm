// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticFileByteReader
{
    byte[] ReadBytes(string path);
}
