// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class StaticInitializationDependencyDecorator(
    IReachabilityInstructionAnalyzer instructions,
    ITypeFinder types) : IReachabilityInstructionAnalyzer
{
    public ReachabilityInstructionAnalysis Analyze(ReachabilityInstructionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = instructions.Analyze(request);
        if (request.Method.Definition.Name != ".cctor")
            return result;

        return result with
        {
            Types = result.Types.Add(types.FindType("System.Exception").Key),
        };
    }
}
