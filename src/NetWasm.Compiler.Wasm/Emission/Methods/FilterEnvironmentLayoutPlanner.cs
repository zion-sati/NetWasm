// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class FilterEnvironmentLayoutPlanner(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IArgumentTypeResolver argumentTypes,
    IArgumentSignatureTypeResolver types,
    IExceptionGroupEnumerator exceptionGroups) : IFilterEnvironmentLayoutPlanner
{
    public FilterEnvironmentLayout Create(
        StructuredMethod method,
        ValueFrameLayout valueLayout,
        MethodRootMap rootMap)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(valueLayout);
        ArgumentNullException.ThrowIfNull(rootMap);

        var header = method.Header;
        if (FilterCapturedSlots.Find(method, exceptionGroups, rootMap) is not { } slots)
        {
            return FilterEnvironmentLayout.Empty;
        }

        var size = valueLayout.Size;
        var rootFrameOffset = size;
        size = checked(size + layouts.Target.AddressSize);
        var parentRootFrameOffset = size;
        size = checked(size + layouts.Target.AddressSize);
        var anchorOffsets = ImmutableDictionary.CreateBuilder<int, int>();
        foreach (var (anchor, index) in rootMap.AnchorPlan.Anchors.Select((anchor, index) => (anchor, index))
                     .Where(item => item.anchor.Location.Kind == StackStorageLocationKind.Local))
        {
            size = Align(size, layouts.Target.AddressSize);
            anchorOffsets.Add(index, size);
            size = checked(size + layouts.Target.AddressSize);
        }
        var rootSlot = 0;
        var captures = ImmutableDictionary.CreateBuilder<CapturedSlot, FilterCapture>();
        foreach (var slot in slots
                     .OrderBy(slot => slot.IsArgument ? 0 : 1)
                     .ThenBy(slot => slot.Index))
        {
            var type = slot.IsArgument
                ? types.Resolve(header, slot.Index)
                : header.LocalSignatureTypes[slot.Index];
            if (slot.IsArgument &&
                argumentTypes.Resolve(header, slot.Index) == CliValueKind.ManagedAddress)
            {
                type = CliTypeIdentity.FromStackKind(CliValueKind.ManagedAddress);
            }
            int offset;
            if (!slot.IsArgument && type.StackKind == CliValueKind.ValueType)
            {
                offset = valueLayout.LocalOffsets[slot.Index];
            }
            else
            {
                var alignment = type.StackKind == CliValueKind.ValueType
                    ? values.GetValueLayout(type).Alignment
                    : layouts.Target.GetStorageAlignment(type);
                var byteSize = type.StackKind == CliValueKind.ValueType
                    ? values.GetValueLayout(type).Size
                    : layouts.Target.GetStorageSize(type);
                size = Align(size, alignment);
                offset = size;
                size = checked(size + byteSize);
            }

            var referenceOffsets = type.StackKind switch
            {
                CliValueKind.ManagedReference => [0],
                CliValueKind.ValueType => values.GetValueLayout(type).ReferenceOffsets,
                _ => [],
            };
            var roots = referenceOffsets
                .Select(referenceOffset => (referenceOffset, rootSlot++))
                .ToImmutableArray();
            captures.Add(slot, new FilterCapture(slot, type, offset, roots));
        }
        return new FilterEnvironmentLayout(
            Align(size, layouts.Target.ObjectReferenceAlignment),
            rootSlot,
            rootFrameOffset,
            captures.ToImmutable())
        {
            ParentRootFrameOffset = parentRootFrameOffset,
            AnchorOffsets = anchorOffsets.ToImmutable(),
        };
    }

    private static int Align(int value, int alignment) =>
        checked((value + alignment - 1) & -alignment);
}
