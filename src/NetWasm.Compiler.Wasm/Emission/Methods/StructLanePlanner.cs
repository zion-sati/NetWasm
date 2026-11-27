// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Wasm.Emission.StructResults;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// The struct locals a method keeps in Wasm locals (lanes) instead of its value frame: the local a return of results
// returns (ReturnBufferLocals.FindLanes), the locals calls store their results in (CallResultLanes) and the temporaries
// built field by field for a call that takes them by their fields (ArgumentLanes).
internal sealed record StructLanePlan(ReturnLanePlan? Return, CallResultLanePlan CallResults, ArgumentLanePlan Arguments)
{
    public static StructLanePlan Empty { get; } = new(null, CallResultLanePlan.Empty, ArgumentLanePlan.Empty);

    // Every local in lanes.
    public ImmutableHashSet<int> Locals { get; } =
        CallResults.Locals.Union(Arguments.Locals.Keys).Union(Return?.Locals ?? []);
}

internal interface IStructLanePlanner
{
    StructLanePlan Plan(StructuredMethod method, MethodRootMap roots, StructResultShape? returnShape);
}

internal sealed class StructLanePlanner(IStructResultClassifier classifier, IInstanceFieldLayoutProvider layouts,
    IStructConventionCallClassifier calls, IExceptionGroupEnumerator exceptionGroups) : IStructLanePlanner
{
    private readonly IExceptionGroupEnumerator _exceptionGroups = exceptionGroups ??
        throw new ArgumentNullException(nameof(exceptionGroups));
    private readonly IStructConventionCallClassifier _calls = calls ?? throw new ArgumentNullException(nameof(calls));
    private readonly IStructResultClassifier _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    private readonly IInstanceFieldLayoutProvider _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));

    public StructLanePlan Plan(StructuredMethod method, MethodRootMap roots, StructResultShape? returnShape)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(roots);
        // A local an exception filter captures is read from the frame by the filter's funclet, so it keeps its slot.
        var filterCaptures = FilterCapturedSlots.Find(method, _exceptionGroups, roots);
        bool Captured(int local) => filterCaptures?.Contains(new CapturedSlot(false, local)) == true;
        return new(
            returnShape is null ? null : ReturnBufferLocals.FindLanes(method, roots, returnShape, Field, Captured),
            CallResultLanes.Find(method, roots, type => _classifier.Classify(type), Field, _calls.CallsBody, Captured),
            ArgumentLanes.Find(method, type => StructParameters.Shape(_classifier, type), instruction =>
                instruction.Operand is CilOperand.FieldInstance field ? _layouts.GetFieldLayout(field.Value).Offset : null,
                _calls.CallsBody, Captured));
    }

    private (int Offset, CliTypeIdentity Type)? Field(CilInstruction instruction) =>
        instruction.Operand is CilOperand.FieldInstance field
            ? (_layouts.GetFieldLayout(field.Value).Offset, field.Value.FieldType)
            : null;
}

// Lanes and arguments read in place are no roots: a lane holds its value only where nothing can collect (the lanes'
// whitelist), and an argument used only in the entry block before anything could collect is dead after it. Neither
// has storage a publication could read a reference field through.
internal static class LaneRoots
{
    public static MethodRootMap Exclude(MethodRootMap roots, IRootSlotAllocator slots, ImmutableHashSet<int> laneLocals,
        ImmutableHashSet<int> confinedArguments)
    {
        if (laneLocals.IsEmpty && confinedArguments.IsEmpty) return roots;
        bool Unrooted(RootSource source) =>
            source.Kind is RootSourceKind.Local or RootSourceKind.ManagedAddressLocal && laneLocals.Contains(source.Index) ||
            source.Kind is RootSourceKind.Argument or RootSourceKind.ManagedAddressArgument && confinedArguments.Contains(source.Index);
        var safepoints = roots.Safepoints.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value with
            {
                Roots = [.. pair.Value.Roots.Where(source => !Unrooted(source))],
                ConstructorCallRoots = [.. pair.Value.ConstructorCallRoots.Where(source => !Unrooted(source))],
            });
        return roots with
        {
            Slots = slots.Allocate([.. safepoints.Values.OrderBy(safepoint => safepoint.IlOffset)]),
            Safepoints = safepoints,
        };
    }
}
