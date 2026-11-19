// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Pipeline;

internal interface IMissingMemberReporter
{
    void Report(ReachableProgram program);
}
