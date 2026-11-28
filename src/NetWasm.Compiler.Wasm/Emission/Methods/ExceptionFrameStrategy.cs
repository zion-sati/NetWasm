// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// The protected block a frame step is for. The metadata is needed only to enter a frame the runtime registers.
internal sealed record ExceptionFrameRequest(
    StructuredMethod Method,
    StructuredExceptionGroup Group,
    MethodEmissionContext Context,
    IEntryLookup<StructuredExceptionGroupKey, ExceptionGroupMetadata>? Metadata);

// How a protected block keeps what the first pass of exception dispatch and its landing pad need.
internal interface IExceptionFrameStrategy
{
    // Whether the block keeps the frame arenas' marks of its start in locals (RegionMarkLocals).
    bool KeepsMarks { get; }

    void Emit(IWasmInstructionWriter code, ExceptionFrameStep step, ExceptionFrameRequest request);
}

internal interface IExceptionFrameStrategySelector
{
    // reachesFilters: whether the program reaches a protected block with a filter (WasmEmissionRequest.ReachesFilters).
    IExceptionFrameStrategy Select(StructuredExceptionGroup group, bool reachesFilters);
}

// A block without catch clauses keeps its marks in locals; a block with filters registers its frame. In a program that
// reaches a filter, a pass-through catch registers while its local is zero and a catch without filters registers while
// a frame with filters is registered, so the first pass sees them; in one that reaches none, no first pass runs a
// filter, so neither ever registers.
internal sealed class ExceptionFrameStrategySelector(
    IExceptionFrameStrategy marked,
    IExceptionFrameStrategy passThrough,
    IExceptionFrameStrategy unregisteredPassThrough,
    IExceptionFrameStrategy filterGuarded,
    IExceptionFrameStrategy unregistered,
    IExceptionFrameStrategy registered) : IExceptionFrameStrategySelector
{
    private readonly IExceptionFrameStrategy _marked = marked ?? throw new ArgumentNullException(nameof(marked));
    private readonly IExceptionFrameStrategy _passThrough = passThrough ?? throw new ArgumentNullException(nameof(passThrough));
    private readonly IExceptionFrameStrategy _unregisteredPassThrough =
        unregisteredPassThrough ?? throw new ArgumentNullException(nameof(unregisteredPassThrough));
    private readonly IExceptionFrameStrategy _filterGuarded =
        filterGuarded ?? throw new ArgumentNullException(nameof(filterGuarded));
    private readonly IExceptionFrameStrategy _unregistered = unregistered ?? throw new ArgumentNullException(nameof(unregistered));
    private readonly IExceptionFrameStrategy _registered = registered ?? throw new ArgumentNullException(nameof(registered));

    public IExceptionFrameStrategy Select(StructuredExceptionGroup group, bool reachesFilters)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.Clauses.All(clause => clause.Kind is CilExceptionRegionKind.Finally or CilExceptionRegionKind.Fault))
        {
            return _marked;
        }
        if (group.Clauses.Any(clause => clause.PassThroughLocal is not null))
        {
            return reachesFilters ? _passThrough : _unregisteredPassThrough;
        }
        if (group.Clauses.All(clause => clause.Kind == CilExceptionRegionKind.Catch))
        {
            return reachesFilters ? _filterGuarded : _unregistered;
        }
        return _registered;
    }
}
