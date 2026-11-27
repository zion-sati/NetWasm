// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IPassThroughCatchLocalFinder
{
    // The Wasm local of a pass-through catch's local (CilExceptionRegion.PassThroughLocal), nonzero while the catch is
    // passed by in the first pass, or null for a group that is not one.
    int? Find(StructuredMethod method, StructuredExceptionGroup group, MethodEmissionContext context);
}

// A pass-through region is its group's only clause, a catch, and its local a plain int32 one, which the method's locals
// hold from LocalBase on.
internal sealed class PassThroughCatchLocalFinder : IPassThroughCatchLocalFinder
{
    public int? Find(StructuredMethod method, StructuredExceptionGroup group, MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(context);
        var passesThrough = false;
        foreach (var clause in group.Clauses)
        {
            passesThrough |= clause.PassThroughLocal is not null;
        }
        if (!passesThrough) return null;
        if (group.Clauses is not [{ Kind: CilExceptionRegionKind.Catch, PassThroughLocal: { } local }])
        {
            throw new InvalidOperationException("A pass-through region is a group's only clause, a catch.");
        }
        var locals = method.Header.LocalSignatureTypes;
        if ((uint)local >= (uint)locals.Length || locals[local].StackKind != CliValueKind.I4 ||
            context.ValueLayout.SpilledScalarLocals.Contains(local) ||
            context.AddressTakenScalarPromotions.ShadowLocals.ContainsKey(local))
        {
            throw new InvalidOperationException($"A pass-through catch's local {local} is not a plain int32 local.");
        }
        return context.LocalBase + local;
    }
}
