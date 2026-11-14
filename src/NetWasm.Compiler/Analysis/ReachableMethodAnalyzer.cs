using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class ReachableMethodAnalyzer(
    IMethodBodyReader methodBodies,
    ITypeRepository types,
    IFieldRepository fields,
    IMethodRepository methods,
    IMethodSpecializer specializer,
    IManagedMethodOptimizer optimizer,
    IMethodBodyProver proofs,
    IImplicitExceptionDiscovery exceptionDiscovery,
    ITypedStackValidatorFactory stackValidators,
    IControlFlowGraphBuilder graphBuilder,
    IReachabilityInstructionAnalyzer instructionAnalyzer) : IReachableMethodAnalyzer
{
    public ReachableMethodAnalysis Analyze(ReachableMethodRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The specializer's first rule validates the original method's stacks, so its folding
        // cannot conceal invalid ones.
        var body = request.RewrittenBody ?? specializer.Rewrite(methodBodies.ReadMethodBody(request.Method) with
        {
            MethodInstance = request.Method,
        });
        var graph = graphBuilder.Build(body);
        var validated = stackValidators.Create(types, fields, methods).Validate(graph);
        var managed = new ManagedMethodBody(request.Method, validated);
        // A body a whole-program reduction rewrote plans its proofs again, for the instructions it now has.
        var optimized = request.RewrittenBody is null ? optimizer.Optimize(managed) : proofs.Prove(body);
        var original = body;
        if (!ReferenceEquals(optimized, body))
        {
            body = optimized;
            graph = graphBuilder.Build(body);
            validated = stackValidators.Create(types, fields, methods).Validate(graph);
            managed = new(request.Method, validated);
        }
        var exceptions = ImmutableArray.CreateBuilder<ReachabilityExceptionRequirement>();
        foreach (var instruction in body.Instructions)
        {
            exceptions.AddRange(exceptionDiscovery.Discover(instruction));
        }
        var catchTypes = body.ExceptionRegions
            .Where(region => region.CatchType is not null)
            .Select(region => region.CatchType!.Value)
            .ToImmutableArray();
        var instructions = instructionAnalyzer.Analyze(new(request.Method, body));
        var analysis = new ReachableMethodAnalysis(
            request.Method,
            managed,
            catchTypes,
            exceptions.ToImmutable(),
            instructions)
        {
            RequiredTypes = RemovedConstructions(request.Method, original, body, instructions),
        };
        // Fingerprinted here, on the worker, rather than as the closure applies it.
        return analysis with { Contribution = MethodContributionFingerprint.Of(analysis) };
    }

    // Managed optimization may remove an allocation (an unused newobj); the types the original body constructed and
    // the optimized one does not stay required. The original body is analysed again only when a newobj went.
    private ImmutableArray<CliTypeIdentity> RemovedConstructions(
        MethodInstanceModel method,
        CilMethodBody original,
        CilMethodBody optimized,
        ReachabilityInstructionAnalysis optimizedFacts)
    {
        if (ReferenceEquals(original, optimized) || Count(optimized) >= Count(original))
        {
            return [];
        }

        var before = instructionAnalyzer.Analyze(new(method, original));
        var after = optimizedFacts.ConstructedTypes.Concat(optimizedFacts.AllocatedTypes).ToHashSet();
        return [.. before.ConstructedTypes.Concat(before.AllocatedTypes).Where(type => !after.Contains(type)).Distinct()];

        static int Count(CilMethodBody body) =>
            body.Instructions.Count(instruction => instruction.Operation == CilOperation.NewObject);
    }
}
