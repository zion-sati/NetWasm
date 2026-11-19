// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ValueFrameInitializationEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IValueFrameAddressEmitter addresses) : IValueFrameInitializationEmitter
{
    public void Initialize(
        IWasmInstructionWriter code,
        StructuredMethodHeader header,
        MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(context);

        EmitRange(code, context, CreateLocalRange(header, context));
        EmitRange(code, context, CreateFilterLocalRange(header, context));
        EmitRange(code, context, CreateRange(context.FilterEnvironment.AnchorOffsets.Values
            .Select(offset => new StorageRange(offset, layouts.Target.AddressSize))));
        foreach (var (offset, size) in context.ValueFrameReferences.EntryClears)
            EmitRange(code, context, new StorageRange(offset, size));
    }

    // Under localsinit every local starts zeroed. Without it (SkipLocalsInit) a local starts with
    // what its frame held, as on .NET, except storage the collector traces: a local that holds a
    // reference or a byref is a root wherever its address reaches, so it starts zeroed either way.
    private StorageRange? CreateLocalRange(
        StructuredMethodHeader header,
        MethodEmissionContext context) => CreateRange(
        context.ValueLayout.LocalOffsets
            .Where(item => header.InitializesLocals || HoldsReferences(header.LocalSignatureTypes[item.Key]))
            .Select(item => new StorageRange(
                item.Value,
                GetStorageSize(header.LocalSignatureTypes[item.Key]))));

    private bool HoldsReferences(CliTypeIdentity type) => type.StackKind switch
    {
        CliValueKind.ValueType => values.GetValueLayout(type) is var layout &&
            (layout.ContainsReferences || !layout.ByReferenceOffsets.IsEmpty),
        CliValueKind.ManagedReference or CliValueKind.ManagedAddress => true,
        _ => false,
    };

    private StorageRange? CreateFilterLocalRange(
        StructuredMethodHeader header,
        MethodEmissionContext context) => CreateRange(
        context.FilterEnvironment.Captures.Values
            .Where(capture => !capture.Slot.IsArgument &&
                (!context.ValueLayout.LocalOffsets.TryGetValue(
                    capture.Slot.Index,
                    out var localOffset) ||
                    localOffset != capture.Offset))
            .Select(capture => new StorageRange(
                capture.Offset,
                GetStorageSize(header.LocalSignatureTypes[capture.Slot.Index]))));

    private int GetStorageSize(CliTypeIdentity type) =>
        type.StackKind == CliValueKind.ValueType
            ? values.GetValueLayout(type).Size
            : layouts.Target.GetStorageSize(type);

    private static StorageRange? CreateRange(IEnumerable<StorageRange> ranges)
    {
        var materialized = ranges.ToArray();
        if (materialized.Length == 0)
        {
            return null;
        }

        var start = materialized.Min(range => range.Offset);
        var end = materialized.Max(range => checked(range.Offset + range.Size));
        return new(start, checked(end - start));
    }

    private void EmitRange(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        StorageRange? range)
    {
        if (range is not { } storage)
        {
            return;
        }

        FrameRangeZeroing.Emit(code, layouts.Target, writer => addresses.Emit(writer, context, storage.Offset), storage.Size);
    }

    private readonly record struct StorageRange(int Offset, int Size);
}
