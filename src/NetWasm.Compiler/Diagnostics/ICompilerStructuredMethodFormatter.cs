// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow;
using StructuredMethod = global::NetWasm.Compiler.ControlFlow.Structured.StructuredMethod;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerStructuredMethodFormatter
{
    string FormatStructure(StructuredMethod method);
}
