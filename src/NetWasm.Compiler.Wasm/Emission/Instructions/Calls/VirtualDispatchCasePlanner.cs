using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

// A run of receiver type ids, LowTypeId through HighTypeId, that one target serves.
internal sealed record VirtualDispatchCase(DispatchTargetModel Target, int LowTypeId, int HighTypeId);

internal interface IVirtualDispatchCasePlanner
{
    // The checked targets as cases ordered by type id, given every receiver the site can have.
    ImmutableArray<VirtualDispatchCase> Plan(
        ImmutableArray<DispatchTargetModel> checkedTargets,
        ImmutableArray<DispatchTargetModel> receivers);
}

// Targets whose code is the same share a case, so a dispatch emits that code once for each run
// of their receivers' type ids that no other receiver of the site interrupts; an id no receiver
// has never reaches the dispatch. A target's code depends on its receiver only where it reads a
// value type's fields or takes its payload address: the value type Equals and GetHashCode
// intrinsics, and a method a value type declares. Every enum's IConvertible, Equals, GetHashCode
// and ToString is Enum's, whose intrinsics test the enum type themselves, so one case serves
// them all where one case each made a dispatch's code grow with the square of the enum count.
internal sealed class VirtualDispatchCasePlanner(
    ITypeLayoutProvider typeLayouts,
    IRuntimeIntrinsicRegistry intrinsics) : IVirtualDispatchCasePlanner
{
    private readonly ITypeLayoutProvider _typeLayouts =
        typeLayouts ?? throw new ArgumentNullException(nameof(typeLayouts));
    private readonly IRuntimeIntrinsicRegistry _intrinsics =
        intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));

    public ImmutableArray<VirtualDispatchCase> Plan(
        ImmutableArray<DispatchTargetModel> checkedTargets,
        ImmutableArray<DispatchTargetModel> receivers)
    {
        var occupied = receivers.Select(TypeId).Distinct().Order().ToArray();
        var cases = new List<VirtualDispatchCase>();
        foreach (var group in checkedTargets.GroupBy(Key))
        {
            var members = group.Select(target => (Id: TypeId(target), Target: target))
                .DistinctBy(member => member.Id)
                .OrderBy(member => member.Id)
                .ToArray();
            var first = members[0];
            var high = first.Id;
            foreach (var member in members.Skip(1))
            {
                if (occupied[Array.BinarySearch(occupied, high) + 1] != member.Id)
                {
                    cases.Add(new(first.Target, first.Id, high));
                    first = member;
                }
                high = member.Id;
            }
            cases.Add(new(first.Target, first.Id, high));
        }
        return [.. cases.OrderBy(dispatchCase => dispatchCase.LowTypeId)];
    }

    private int TypeId(DispatchTargetModel target) =>
        _typeLayouts.GetObjectLayout(target.ReceiverType).TypeId;

    private (string Method, CliTypeIdentity? Receiver) Key(DispatchTargetModel target) =>
        (target.Method.CanonicalName, DependsOnReceiver(target) ? target.ReceiverType : null);

    private bool DependsOnReceiver(DispatchTargetModel target) =>
        _intrinsics.TryGetIntrinsic(target.Method.Definition.Key, out var intrinsic) &&
            RuntimeIntrinsicKinds.DependsOnReceiverType(intrinsic) ||
        target.ReceiverType.IsValueType && target.Method.DeclaringType.IsValueType;
}
