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
    IImplicitExceptionDiscovery exceptionDiscovery,
    ITypedStackValidatorFactory stackValidators,
    IControlFlowGraphBuilder graphBuilder,
    IReachabilityInstructionAnalyzer instructionAnalyzer) : IReachableMethodAnalyzer
{
    public ReachableMethodAnalysis Analyze(ReachableMethodRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = specializer.Rewrite(methodBodies.ReadMethodBody(request.Method)) with
        {
            MethodInstance = request.Method,
        };
        var exceptions = ImmutableArray.CreateBuilder<ReachabilityExceptionRequirement>();
        foreach (var instruction in body.Instructions)
        {
            exceptions.AddRange(exceptionDiscovery.Discover(instruction));
        }
        var catchTypes = body.ExceptionRegions
            .Where(region => region.CatchType is not null)
            .Select(region => region.CatchType!.Value)
            .ToImmutableArray();
        var graph = graphBuilder.Build(body);
        var validated = stackValidators.Create(types, fields, methods).Validate(graph);
        return new(
            request.Method,
            new ManagedMethodBody(request.Method, validated),
            catchTypes,
            exceptions.ToImmutable(),
            instructionAnalyzer.Analyze(new(request.Method, body)));
    }
}
