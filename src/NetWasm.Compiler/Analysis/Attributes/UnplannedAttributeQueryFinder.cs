// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// Reads the program the reductions settled on: each reached reference to an attribute query API still there is one the
// lowering and the run-time Type reducer did not answer, wherever inlining or a reduction left it, and a call a
// reduction folded away is gone. The query APIs' own bodies forward to one another, and are reached only through a
// reference this reports, so they are not read.
internal sealed class UnplannedAttributeQueryFinder(
    ICalledMethodResolver calls,
    IAttributeQueryClassifier queries,
    IAttributeProvenanceAnalyzer provenance,
    IAttributeQueryFallbackClassifier fallbacks,
    IUnplannedAttributeQueryDescriber describer) : IUnplannedAttributeQueryFinder
{
    public ImmutableArray<UnplannedAttributeQuery> Find(ReachableProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var runtimeTypes = program.RuntimeTypes.Concat(program.AllocatedTypes).ToImmutableHashSet();
        var found = ImmutableArray.CreateBuilder<UnplannedAttributeQuery>();
        foreach (var body in program.Methods.Values.Concat(program.ConstructedMethods.Values)
            .DistinctBy(static body => body.Method.CanonicalName))
        {
            if (AttributeQueryApis.IsQuery(body.Method))
            {
                continue;
            }
            ImmutableDictionary<int, ImmutableArray<AttributeValueProof>>? proofs = null;
            foreach (var instruction in body.Body.Instructions)
            {
                if (Referenced(instruction) is not { } method || !AttributeQueryApis.IsQuery(method))
                {
                    continue;
                }
                var call = instruction.Operation is CilOperation.Call or CilOperation.CallVirtual
                    ? queries.Classify(method)
                    : null;
                ImmutableArray<AttributeValueProof>? stack = null;
                if (call is not null)
                {
                    proofs ??= provenance.Analyze(body.ControlFlow);
                    stack = proofs.TryGetValue(instruction.Offset, out var proof) ? proof : null;
                }
                var reference = new AttributeQueryReference(instruction, method, call, stack);
                if (fallbacks.Classify(reference, runtimeTypes) is { } fallback)
                {
                    found.Add(describer.Describe(body, reference, fallback));
                }
            }
        }
        return found.ToImmutable();
    }

    // The query a call, an ldftn or ldvirtftn, or a method handle's ldtoken names.
    private MethodInstanceModel? Referenced(CilInstruction instruction) => instruction.Operation switch
    {
        CilOperation.Call or CilOperation.CallVirtual or CilOperation.LoadFunction or CilOperation.LoadVirtualFunction =>
            calls.Resolve(instruction),
        CilOperation.LoadMethodToken when instruction.Operand is CilOperand.MethodInstance handle => handle.Value,
        _ => null,
    };
}
