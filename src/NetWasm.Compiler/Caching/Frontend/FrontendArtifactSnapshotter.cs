using NetWasm.Compiler.Analysis;
using System;

namespace NetWasm.Compiler.Caching.Frontend;

internal sealed class FrontendArtifactSnapshotter : IFrontendArtifactSnapshotter
{
    public FrontendArtifactSnapshot Capture(FrontendArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(artifact.Analysis);
        ArgumentNullException.ThrowIfNull(artifact.StructuredMethod);

        var analysis = artifact.Analysis;
        return new(
            new(
                analysis.Method,
                analysis.Body.Body,
                analysis.Body.ControlFlow.EntryStacks,
                analysis.Body.ControlFlow.InstructionEntryStacks,
                analysis.CatchTypes,
                analysis.Exceptions,
                analysis.Instructions),
            new(
                artifact.StructuredMethod.Header,
                artifact.StructuredMethod.EntryBlock,
                artifact.StructuredMethod.Blocks,
                artifact.StructuredMethod.Body,
                artifact.StructuredMethod.TopLevelExceptionGroups,
                artifact.StructuredMethod.ExceptionGroups,
                artifact.StructuredMethod.InstructionEntryStacks));
    }
}
