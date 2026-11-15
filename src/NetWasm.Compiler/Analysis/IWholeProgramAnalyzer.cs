// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

public interface IWholeProgramAnalyzer
{
    ReachableProgram Analyze(
        MethodDefinitionModel? entryPoint,
        IEnumerable<ProgramExport> requestedExports,
        ReachabilityRoots? roots = null);
}
