using NetWasm.Compiler.Analysis;
using System;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Caching.Frontend;

internal sealed class FrontendArtifactHydrator(
    IControlFlowGraphBuilder graphBuilder,
    IStructuredMethodFactory structuredMethods) : IFrontendArtifactHydrator
{
    private readonly IControlFlowGraphBuilder _graphBuilder =
        graphBuilder ?? throw new ArgumentNullException(nameof(graphBuilder));
    private readonly IStructuredMethodFactory _structuredMethodFactory = structuredMethods ??
        throw new ArgumentNullException(nameof(structuredMethods));

    public FrontendArtifact Hydrate(FrontendArtifactSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Analysis);
        ArgumentNullException.ThrowIfNull(snapshot.StructuredMethod);

        var analysis = snapshot.Analysis;
        ArgumentNullException.ThrowIfNull(analysis.Method);
        ArgumentNullException.ThrowIfNull(analysis.Body);
        ArgumentNullException.ThrowIfNull(analysis.Instructions);

        var graph = _graphBuilder.Build(analysis.Body);
        var validated = new ValidatedControlFlowGraph(
            graph,
            analysis.EntryStacks,
            analysis.InstructionEntryStacks);
        var body = new ManagedMethodBody(analysis.Method, validated);
        return new(
            new(
                analysis.Method,
                body,
                analysis.CatchTypes,
                analysis.Exceptions,
                analysis.Instructions),
            _structuredMethodFactory.Create(snapshot.StructuredMethod));
    }
}
