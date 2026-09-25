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
