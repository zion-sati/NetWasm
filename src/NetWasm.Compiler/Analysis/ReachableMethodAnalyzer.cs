// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
        // A body whose virtual await a reduction turned back into reads of the task through its address
        // (VirtualDirectCallProgramReducer) is optimized again, so those reads inline and the task splits.
        // So is one whose GetType() a reduction named exactly (SoleAllocatedTypeProgramReducer): the branch it
        // decides goes, and struct locals and arguments that only that branch kept whole split.
        var optimized = request.RewrittenBody is null || ReadsAwaitedTaskAtAddress(body) || NamesObjectTypeExactly(body)
            ? optimizer.Optimize(managed)
            : proofs.Prove(body);
        var original = body;
        var originalGraph = graph;
        if (!ReferenceEquals(optimized, body))
        {
            body = optimized;
            graph = graphBuilder.Build(body);
            validated = stackValidators.Create(types, fields, methods).Validate(graph);
            managed = new(request.Method, validated);
        }
        // What only unreachable code names is not reached (UnreachableCodeEraser).
        var unoptimized = ReferenceEquals(original, body);
        var reachable = UnreachableCodeEraser.Erase(body, graph);
        if (!ReferenceEquals(reachable, body))
        {
            body = reachable;
            graph = graphBuilder.Build(body);
            validated = stackValidators.Create(types, fields, methods).Validate(graph);
            managed = new(request.Method, validated);
        }
        // Erasure keeps the terminators that shape unreachable blocks; they never run, so they throw nothing.
        var exceptions = ImmutableArray.CreateBuilder<ReachabilityExceptionRequirement>();
        foreach (var instruction in body.Instructions)
        {
            if (!validated.InstructionEntryStacks.ContainsKey(instruction.Offset)) continue;
            exceptions.AddRange(exceptionDiscovery.Discover(instruction));
        }
        // A handler runs only when its try holds reachable code: the structurer lays out no other region.
        var regions = body.ExceptionRegions
            .Where(region => graph.ReachableBlocks.Contains(graph.GetBlockAtOffset(region.HandlerOffset).Index))
            .ToImmutableArray();
        var catchTypes = regions
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
            ReachableRegions = regions,
            RequiredTypes = RemovedConstructions(
                request.Method,
                unoptimized ? body : UnreachableCodeEraser.Erase(original, originalGraph),
                body,
                instructions),
        };
        // Fingerprinted here, on the worker, rather than as the closure applies it.
        return analysis with { Contribution = MethodContributionFingerprint.Of(analysis) };
    }

    // Managed optimization may remove an allocation (an unused newobj); the types the original body's reachable code
    // constructed and the optimized one does not stay required. The original body is analyzed again only when a newobj
    // went.
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

    private static bool NamesObjectTypeExactly(CilMethodBody body) =>
        body.Instructions.Any(instruction => CilTypeMaterialization.IsExactObjectType(instruction));

    private static bool ReadsAwaitedTaskAtAddress(CilMethodBody body) => body.Instructions.Any(instruction =>
        instruction is { Operation: CilOperation.Call, Operand: CilOperand.MethodInstance { Value: var method } } &&
        method.Definition.Name is "IsCompletedAt" or "GetResultAt");
}
