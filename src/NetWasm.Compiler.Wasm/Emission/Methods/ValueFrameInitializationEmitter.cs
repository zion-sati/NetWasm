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
    // Wasmtime runs memory.fill out of line, which costs more than a few
    // stores, so ranges up to this size are zeroed with stores instead: sixteen
    // 16-byte stores at most. A Matrix4x4 local alone is 64 bytes.
    private const int MaximumStoredZeroBytes = 256;

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

    private StorageRange? CreateLocalRange(
        StructuredMethodHeader header,
        MethodEmissionContext context) => CreateRange(
        context.ValueLayout.LocalOffsets.Select(item => new StorageRange(
            item.Value,
            GetStorageSize(header.LocalSignatureTypes[item.Key]))));

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

        if (storage.Size <= MaximumStoredZeroBytes)
        {
            EmitZeroStores(code, context, storage);
            return;
        }

        addresses.Emit(code, context, storage.Offset);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        ManagedMemoryEmitter.EmitAddressConstant(
            code,
            layouts.Target,
            storage.Size);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
    }

    // Zeroes the range with 16-byte stores and a narrower tail, each addressed
    // from the range start through its memarg offset.
    private void EmitZeroStores(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        StorageRange storage)
    {
        for (var written = 0; written < storage.Size;)
        {
            var size = (storage.Size - written) switch
            {
                >= 16 => 16,
                >= 8 => 8,
                >= 4 => 4,
                >= 2 => 2,
                _ => 1,
            };
            addresses.Emit(code, context, storage.Offset);
            if (size == 16)
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.SimdPrefixed,
                    WasmInstructionOperand.FromBytes([(byte)WasmSimdOpcodes.V128Constant, .. new byte[16]])));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.SimdPrefixed,
                    WasmInstructionOperand.PrefixedTriple(WasmSimdOpcodes.V128Store, 0, (uint)written)));
            }
            else
            {
                code.Write(size == 8
                    ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(0))
                    : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
                ManagedMemoryEmitter.EmitStoreBySize(code, layouts.Target, written, size);
            }
            written += size;
        }
    }

    private readonly record struct StorageRange(int Offset, int Size);
}
