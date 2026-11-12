using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface ICilTryReentryLowerer
{
    LoweredTryReentryBody Lower(
        ImmutableArray<CilInstruction> instructions,
        ImmutableArray<CilExceptionRegion> exceptionRegions,
        int localCount);
}

// IL may enter a try other than by falling into it: a branch from outside it to its first
// instruction, and a catch's leave to any instruction of its own try (ECMA-335 III.3.46).
// Structured control flow enters a block only by falling into it, so each such try gets an entry
// point just before it, inside every block enclosing it, that falls into it, and outside branches
// to its first instruction go there. A catch's leave into its try stores the target's number in
// an entry local and leaves to the entry point; the try then begins with a dispatch that reads the
// local, resets it and branches to the target, or falls through when the local is 0, as it is on
// every other entry.
internal sealed class CilTryReentryLowerer : ICilTryReentryLowerer
{
    public LoweredTryReentryBody Lower(
        ImmutableArray<CilInstruction> instructions,
        ImmutableArray<CilExceptionRegion> exceptionRegions,
        int localCount)
    {
        if (exceptionRegions.IsEmpty)
        {
            return new(instructions, exceptionRegions, [], AdditionalMaxStack: 0);
        }
        var tries = exceptionRegions.Select(TryOf).Distinct().ToArray();
        var reentries = FindReentries(instructions, exceptionRegions);

        // The tries a branch or a dispatch enters at their first instruction, and the re-entered
        // ones. Any other leave keeps its target: one that enters a try is invalid.
        var entered = new HashSet<Zone>(reentries.Values.Select(reentry => reentry.Try));
        foreach (var instruction in instructions)
        {
            if (instruction.Operand is CilOperand.BranchTarget { Offset: var target } &&
                instruction.Operation != CilOperation.Leave &&
                Entered(tries, Zones(exceptionRegions, instruction.Offset), target) is { } zone)
            {
                entered.Add(zone);
            }
        }
        foreach (var reentry in reentries.Values)
        {
            if (Entered(tries, Inside(tries, reentry.Try), reentry.Target) is { } zone)
            {
                entered.Add(zone);
            }
        }
        if (entered.Count == 0)
        {
            return new(instructions, exceptionRegions, [], AdditionalMaxStack: 0);
        }

        // Tries starting at one instruction nest, so their entry points run outermost first. Each
        // re-entered try has an entry local and a dispatch of its targets.
        var zones = entered.OrderBy(zone => zone.Start).ThenByDescending(zone => zone.End).ToArray();
        var zonesAt = zones.GroupBy(zone => zone.Start).ToDictionary(group => group.Key, group => group.ToArray());
        var dispatched = zones.Where(zone => reentries.Values.Any(reentry => reentry.Try == zone)).ToArray();
        var locals = dispatched.Select((zone, index) => (zone, Local: localCount + index))
            .ToDictionary(item => item.zone, item => item.Local);
        var targets = dispatched.ToDictionary(zone => zone, zone => reentries.Values
            .Where(reentry => reentry.Try == zone).Select(reentry => reentry.Target).Distinct().ToArray());

        // Where each instruction and each try's entry point land.
        var own = new Dictionary<int, int>();
        var entry = new Dictionary<Zone, int>();
        var count = 0;
        foreach (var instruction in instructions)
        {
            foreach (var zone in zonesAt.GetValueOrDefault(instruction.Offset, []))
            {
                entry[zone] = count;
                count += 1 + (targets.TryGetValue(zone, out var zoneTargets) ? zoneTargets.Length * 6 + 1 : 0);
            }
            own[instruction.Offset] = count;
            count += reentries.ContainsKey(instruction.Offset) ? 3 : 1;
        }
        own[instructions[^1].NextOffset] = count;

        // A transfer to an offset from code in the given blocks: the entry point of the outermost
        // try starting there that the code is outside, or the instruction itself.
        int Resolve(IReadOnlySet<Zone> source, int target)
        {
            foreach (var zone in zonesAt.GetValueOrDefault(target, []))
            {
                if (!source.Contains(zone))
                {
                    return entry[zone];
                }
            }
            return own[target];
        }

        var result = ImmutableArray.CreateBuilder<CilInstruction>(count);
        foreach (var instruction in instructions)
        {
            foreach (var zone in zonesAt.GetValueOrDefault(instruction.Offset, []))
            {
                Add(CilOperation.Nop, new CilOperand.None(), instruction);
                if (!targets.TryGetValue(zone, out var zoneTargets))
                {
                    continue;
                }
                var inside = Inside(tries, zone);
                var local = new CilOperand.Index(locals[zone]);
                var cases = result.Count + zoneTargets.Length * 3 + 1;
                for (var index = 0; index < zoneTargets.Length; index++)
                {
                    Add(CilOperation.LoadLocal, local, instruction);
                    Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(index + 1), instruction);
                    Add(CilOperation.BranchIfEqual, new CilOperand.BranchTarget(cases + index * 3), instruction);
                }
                Add(CilOperation.Branch, new CilOperand.BranchTarget(Resolve(inside, zone.Start)), instruction);
                foreach (var target in zoneTargets)
                {
                    Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(0), instruction);
                    Add(CilOperation.StoreLocal, local, instruction);
                    Add(CilOperation.Branch, new CilOperand.BranchTarget(Resolve(inside, target)), instruction);
                }
            }
            if (reentries.TryGetValue(instruction.Offset, out var leave))
            {
                Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(Array.IndexOf(targets[leave.Try], leave.Target) + 1),
                    instruction);
                Add(CilOperation.StoreLocal, new CilOperand.Index(locals[leave.Try]), instruction);
                Add(CilOperation.Leave, new CilOperand.BranchTarget(entry[leave.Try]), instruction);
                continue;
            }
            Add(instruction.Operation, instruction.Operand is CilOperand.BranchTarget branch
                ? new CilOperand.BranchTarget(instruction.Operation == CilOperation.Leave
                    ? own[branch.Offset]
                    : Resolve(Zones(exceptionRegions, instruction.Offset), branch.Offset))
                : instruction.Operand, instruction);
        }

        return new(
            result.MoveToImmutable(),
            [.. exceptionRegions.Select(region => region with
            {
                TryOffset = Start(TryOf(region)),
                TryLength = End(region.TryOffset + region.TryLength) - Start(TryOf(region)),
                HandlerOffset = Start(HandlerOf(region)),
                HandlerLength = End(region.HandlerOffset + region.HandlerLength) - Start(HandlerOf(region)),
                FilterOffset = region.FilterOffset is int filter ? Start(new(filter, region.HandlerOffset, ZoneKind.Filter)) : null,
            })],
            [.. dispatched.Select(_ => CliTypeIdentity.FromStackKind(CliValueKind.I4))],
            AdditionalMaxStack: dispatched.Length == 0 ? 0 : 2);

        // An entered try starts just after its entry point; a block enclosing an entered try that
        // starts where it does starts at that try's entry point, outside the try; a block nested
        // in it starts after it.
        int Start(Zone block)
        {
            foreach (var zone in zonesAt.GetValueOrDefault(block.Start, []))
            {
                if (zone == block)
                {
                    return entry[zone] + 1;
                }
                if (block.End > zone.End)
                {
                    return entry[zone];
                }
            }
            return own[block.Start];
        }

        // A block ending where an entered try starts ends before its entry point.
        int End(int blockEnd) =>
            zonesAt.TryGetValue(blockEnd, out var starting) ? entry[starting[0]] : own[blockEnd];

        void Add(CilOperation operation, CilOperand operand, CilInstruction source) => result.Add(
            new CilInstruction(result.Count, result.Count + 1, operation, operand)
            {
                OriginalOffset = source.SourceOffset,
            });
    }

    // The outermost try starting at the target that the transfer's source is outside.
    private static Zone? Entered(Zone[] tries, IReadOnlySet<Zone> source, int target)
    {
        Zone? entered = null;
        foreach (var zone in tries)
        {
            if (zone.Start == target && !source.Contains(zone) &&
                (entered is not { } current || zone.End > current.End))
            {
                entered = zone;
            }
        }
        return entered;
    }

    // The tries code just inside a try's first instruction is in: the try and those enclosing it.
    private static HashSet<Zone> Inside(Zone[] tries, Zone zone) =>
        [.. tries.Where(candidate => candidate == zone || candidate.Contains(zone))];

    // Each leave from a catch or filter handler to an instruction of that handler's own try,
    // directly in it or at the start of a try nested in it.
    private static Dictionary<int, Reentry> FindReentries(
        ImmutableArray<CilInstruction> instructions,
        ImmutableArray<CilExceptionRegion> exceptionRegions)
    {
        var reentries = new Dictionary<int, Reentry>();
        foreach (var instruction in instructions)
        {
            if (instruction is not { Operation: CilOperation.Leave, Operand: CilOperand.BranchTarget { Offset: var target } })
            {
                continue;
            }
            var source = Zones(exceptionRegions, instruction.Offset);
            var entered = Zones(exceptionRegions, target).Except(source).ToArray();
            if (entered.Length == 0)
            {
                continue;
            }
            foreach (var region in exceptionRegions)
            {
                var tryZone = TryOf(region);
                if (region.Kind is CilExceptionRegionKind.Catch or CilExceptionRegionKind.Filter &&
                    source.Contains(HandlerOf(region)) && entered.Contains(tryZone) &&
                    entered.All(zone => zone == tryZone ||
                        zone.Kind == ZoneKind.Try && zone.Start == target && tryZone.Contains(zone)))
                {
                    reentries[instruction.Offset] = new(tryZone, target);
                    break;
                }
            }
        }
        return reentries;
    }

    private static HashSet<Zone> Zones(ImmutableArray<CilExceptionRegion> exceptionRegions, int offset)
    {
        var zones = new HashSet<Zone>();
        foreach (var region in exceptionRegions)
        {
            Add(TryOf(region));
            Add(HandlerOf(region));
            if (region.FilterOffset is int filter)
            {
                Add(new Zone(filter, region.HandlerOffset, ZoneKind.Filter));
            }
        }
        return zones;

        void Add(Zone zone)
        {
            if (zone.Start <= offset && offset < zone.End)
            {
                zones.Add(zone);
            }
        }
    }

    private static Zone TryOf(CilExceptionRegion region) =>
        new(region.TryOffset, region.TryOffset + region.TryLength, ZoneKind.Try);

    private static Zone HandlerOf(CilExceptionRegion region) =>
        new(region.HandlerOffset, region.HandlerOffset + region.HandlerLength, ZoneKind.Handler);

    private enum ZoneKind
    {
        Try,
        Handler,
        Filter,
    }

    private readonly record struct Zone(int Start, int End, ZoneKind Kind)
    {
        public bool Contains(Zone other) => Start <= other.Start && other.End <= End && this != other;
    }

    private readonly record struct Reentry(Zone Try, int Target);
}

internal sealed record LoweredTryReentryBody(
    ImmutableArray<CilInstruction> Instructions,
    ImmutableArray<CilExceptionRegion> ExceptionRegions,
    ImmutableArray<CliTypeIdentity> AddedLocals,
    int AdditionalMaxStack);
