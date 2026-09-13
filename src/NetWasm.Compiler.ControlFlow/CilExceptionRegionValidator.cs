using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow;

internal sealed class CilExceptionRegionValidator : ICilExceptionRegionValidator
{
    public void Validate(CilMethodBody body, int methodEnd)
    {
        ArgumentNullException.ThrowIfNull(body);
        var boundaries = body.Instructions
            .Select(instruction => instruction.Offset)
            .Append(methodEnd)
            .ToImmutableHashSet();
        var filterRanges = new List<(int Start, int End)>();
        foreach (var region in body.ExceptionRegions)
        {
            ValidateRange(region.TryOffset, region.TryLength, "try");
            ValidateRange(region.HandlerOffset, region.HandlerLength, "handler");
            if (RangesOverlap(
                    region.TryOffset,
                    region.TryOffset + region.TryLength,
                    region.HandlerOffset,
                    region.HandlerOffset + region.HandlerLength))
            {
                throw Invalid(body, region.HandlerOffset, "try and handler ranges overlap");
            }

            switch (region.Kind)
            {
                case CilExceptionRegionKind.Catch:
                    if (region.CatchType is null || region.FilterOffset is not null)
                    {
                        throw Invalid(
                            body,
                            region.HandlerOffset,
                            "catch clause has invalid token or filter offset");
                    }
                    break;
                case CilExceptionRegionKind.Filter:
                    ValidateFilter(region);
                    break;
                case CilExceptionRegionKind.Finally:
                case CilExceptionRegionKind.Fault:
                    if (region.CatchType is not null || region.FilterOffset is not null)
                    {
                        throw Invalid(
                            body,
                            region.HandlerOffset,
                            $"{region.Kind.ToString().ToLowerInvariant()} clause has a " +
                            "catch token or filter offset");
                    }
                    ValidateCleanupTerminator(region);
                    break;
                default:
                    throw Invalid(body, region.HandlerOffset, "unknown exception region kind");
            }
        }

        ValidateTransfers(body, filterRanges, CreateZones(body));

        void ValidateFilter(CilExceptionRegion region)
        {
            if (region.CatchType is not null || region.FilterOffset is not int filterOffset)
            {
                throw Invalid(
                    body,
                    region.HandlerOffset,
                    "filter clause has invalid token or missing filter offset");
            }
            if (!boundaries.Contains(filterOffset) ||
                filterOffset < body.Instructions[0].Offset ||
                filterOffset >= region.HandlerOffset ||
                filterOffset < region.TryOffset + region.TryLength)
            {
                throw Invalid(body, filterOffset, "filter range is invalid");
            }
            var filter = InstructionsIn(body, filterOffset, region.HandlerOffset);
            if (filter[^1].Operation != CilOperation.EndFilter)
            {
                throw Invalid(body, filterOffset, "filter must end with endfilter");
            }
            foreach (var instruction in filter)
            {
                if (instruction.Operation is CilOperation.Return or CilOperation.Leave or
                    CilOperation.Rethrow or CilOperation.EndFinally)
                {
                    throw Invalid(
                        body,
                        instruction.Offset,
                        "filter contains a forbidden control transfer");
                }
                foreach (var target in BranchTargets(instruction))
                {
                    if (target < filterOffset || target >= region.HandlerOffset)
                    {
                        throw Invalid(
                            body,
                            instruction.Offset,
                            "branch exits an exception filter");
                    }
                }
            }
            filterRanges.Add((filterOffset, region.HandlerOffset));
        }

        void ValidateCleanupTerminator(CilExceptionRegion region)
        {
            var handler = InstructionsIn(
                body,
                region.HandlerOffset,
                checked(region.HandlerOffset + region.HandlerLength));
            if (handler.Length == 0 || handler[^1].Operation is not
                    (CilOperation.EndFinally or CilOperation.Throw))
            {
                throw Invalid(
                    body,
                    region.HandlerOffset,
                    $"{region.Kind.ToString().ToLowerInvariant()} must end with endfinally");
            }
        }

        void ValidateRange(int offset, int length, string name)
        {
            var end = (long)offset + length;
            if (length <= 0 || offset < body.Instructions[0].Offset || end > methodEnd ||
                !boundaries.Contains(offset) || !boundaries.Contains((int)end))
            {
                throw Invalid(body, offset, $"{name} range is invalid");
            }
        }
    }

    private static void ValidateTransfers(
        CilMethodBody body,
        List<(int Start, int End)> filterRanges,
        ImmutableArray<ExceptionZone> zones)
    {
        foreach (var instruction in body.Instructions)
        {
            var sourceZones = ZonesAt(zones, instruction.Offset);
            var inFilter = filterRanges.Any(range =>
                instruction.Offset >= range.Start && instruction.Offset < range.End);
            if (instruction.Operation == CilOperation.EndFilter && !inFilter)
            {
                throw Invalid(
                    body,
                    instruction.Offset,
                    "endfilter appears outside an exception filter");
            }
            if (instruction.Operation == CilOperation.EndFinally &&
                !sourceZones.Any(zone => zone.Kind is ExceptionZoneKind.Finally or
                    ExceptionZoneKind.Fault))
            {
                throw Invalid(
                    body,
                    instruction.Offset,
                    "endfinally appears outside a finally or fault handler");
            }
            if (instruction.Operation == CilOperation.Rethrow &&
                !sourceZones.Any(zone => zone.Kind == ExceptionZoneKind.Catch))
            {
                throw Invalid(
                    body,
                    instruction.Offset,
                    "rethrow appears outside a catch handler");
            }
            if (instruction.Operation == CilOperation.Return && sourceZones.Length != 0)
            {
                throw Invalid(
                    body,
                    instruction.Offset,
                    "return exits a protected exception region");
            }

            foreach (var target in BranchTargets(instruction))
            {
                var targetZones = ZonesAt(zones, target);
                if (instruction.Operation == CilOperation.Leave)
                {
                    ValidateLeave(body, instruction, sourceZones, targetZones);
                    continue;
                }
                if (SameZones(sourceZones, targetZones))
                {
                    continue;
                }
                // ValidateFilter rejects transfers originating inside a filter,
                // so every transfer reaching this point starts outside one.
                if (filterRanges.Any(range => target >= range.Start && target < range.End))
                {
                    throw Invalid(
                        body,
                        instruction.Offset,
                        "branch enters an exception filter");
                }
                throw Invalid(
                    body,
                    instruction.Offset,
                    "branch crosses an exception-region boundary without leave");
            }
        }
    }

    private static void ValidateLeave(
        CilMethodBody body,
        CilInstruction instruction,
        ImmutableArray<ExceptionZone> source,
        ImmutableArray<ExceptionZone> target)
    {
        if (source.Any(zone =>
                (zone.Kind is ExceptionZoneKind.Filter or
                    ExceptionZoneKind.Finally or ExceptionZoneKind.Fault) &&
                !target.Contains(zone)))
        {
            throw Invalid(
                body,
                instruction.Offset,
                "leave originates in a filter, finally, or fault region");
        }
        if (source.Length == 0 || target.Length >= source.Length ||
            target.Any(zone => !source.Contains(zone)))
        {
            throw Invalid(
                body,
                instruction.Offset,
                "leave target does not exit to an enclosing exception region");
        }
    }

    private static ImmutableArray<ExceptionZone> CreateZones(CilMethodBody body)
    {
        var zones = ImmutableArray.CreateBuilder<ExceptionZone>();
        foreach (var tryRange in body.ExceptionRegions
                     .Select(region => (region.TryOffset, region.TryLength))
                     .Distinct())
        {
            zones.Add(new(
                ExceptionZoneKind.Try,
                tryRange.TryOffset,
                checked(tryRange.TryOffset + tryRange.TryLength),
                tryRange.TryOffset,
                tryRange.TryLength));
        }
        for (var index = 0; index < body.ExceptionRegions.Length; index++)
        {
            var region = body.ExceptionRegions[index];
            if (region.FilterOffset is int filterOffset)
            {
                zones.Add(new(
                    ExceptionZoneKind.Filter,
                    filterOffset,
                    region.HandlerOffset,
                    index,
                    0));
            }
            zones.Add(new(
                // Validate has already rejected unknown kinds. The remaining
                // enum values map to handler zone values, with the two catch
                // forms sharing one zone kind.
                (ExceptionZoneKind)(2 + Math.Max(0, (int)region.Kind - 1)),
                region.HandlerOffset,
                checked(region.HandlerOffset + region.HandlerLength),
                index,
                1));
        }
        return zones.ToImmutable();
    }

    private static ImmutableArray<ExceptionZone> ZonesAt(
        ImmutableArray<ExceptionZone> zones,
        int offset) =>
        [.. zones.Where(zone => offset >= zone.Start && offset < zone.End)];

    private static bool SameZones(
        ImmutableArray<ExceptionZone> left,
        ImmutableArray<ExceptionZone> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }
        foreach (var zone in left)
        {
            if (!right.Any(candidate =>
                    candidate.Kind == zone.Kind &&
                    candidate.Start == zone.Start &&
                    candidate.End == zone.End &&
                    candidate.Identity == zone.Identity &&
                    candidate.Part == zone.Part))
            {
                return false;
            }
        }
        return true;
    }

    private static ImmutableArray<int> BranchTargets(CilInstruction instruction) =>
        instruction.Operand switch
        {
            CilOperand.BranchTarget target => [target.Offset],
            CilOperand.SwitchTargets targets => targets.Offsets,
            _ => [],
        };

    private static CilInstruction[] InstructionsIn(
        CilMethodBody body,
        int start,
        int end) => body.Instructions
        .Where(instruction => instruction.Offset >= start && instruction.Offset < end)
        .ToArray();

    private static bool RangesOverlap(
        int leftStart,
        int leftEnd,
        int rightStart,
        int rightEnd) => leftStart < rightEnd && rightStart < leftEnd;

    private static CompilerException Invalid(
        CilMethodBody body,
        int offset,
        string message) => new(
        new CompilerDiagnostic(
            DiagnosticCode.InvalidCil,
            message,
            body.Method.Name,
            offset));

    private enum ExceptionZoneKind
    {
        Try,
        Filter,
        Catch,
        Finally,
        Fault,
    }

    private readonly record struct ExceptionZone(
        ExceptionZoneKind Kind,
        int Start,
        int End,
        int Identity,
        int Part);
}
