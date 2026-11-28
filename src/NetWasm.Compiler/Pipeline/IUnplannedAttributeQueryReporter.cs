// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Pipeline;

internal interface IUnplannedAttributeQueryReporter
{
    // Warns for each attribute query of the program the compiler could not answer (NW1019).
    void Report(ReachableProgram program);
}
