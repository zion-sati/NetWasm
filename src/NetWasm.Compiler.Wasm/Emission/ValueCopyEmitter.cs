using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IValueCopyEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        int destinationLocal,
        int size,
        int alignment,
        Action<IWasmInstructionWriter> emitSource,
        MethodEmissionContext? inlineContext = null);
}

internal sealed class BulkValueCopyEmitter(ITargetLayout layouts) : IValueCopyEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        int destinationLocal,
        int size,
        int alignment,
        Action<IWasmInstructionWriter> emitSource,
        MethodEmissionContext? inlineContext = null)
    {
        ValueCopyContract.Validate(
            code, destinationLocal, size, alignment, emitSource);
        emitSource(code);
        ManagedMemoryEmitter.EmitAddressConstant(code, layouts.Target, size);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedTriple(
                WasmOpcodes.MemoryCopy, 0, 0)));
    }
}

internal sealed class SmallValueCopyEmitter(
    IValueCopyEmitter bulk) : IValueCopyEmitter
{
    private const int InlineSize = 16;

    public void Emit(
        IWasmInstructionWriter code,
        int destinationLocal,
        int size,
        int alignment,
        Action<IWasmInstructionWriter> emitSource,
        MethodEmissionContext? inlineContext = null)
    {
        ValueCopyContract.Validate(
            code, destinationLocal, size, alignment, emitSource);
        if (size != InlineSize)
        {
            // Where the method inlines small blocks, other sizes copy in exact
            // chunks as known-size copies do.
            if (inlineContext is not null && SmallBlockEmitter.CanInlineKnownSizeCopy(inlineContext, size))
            {
                emitSource(code);
                SmallBlockEmitter.EmitInlineKnownSizeCopy(code, inlineContext, size);
                return;
            }
            bulk.Emit(code, destinationLocal, size, alignment, emitSource);
            return;
        }

        // One 16-byte copy: a vector value stored whole by SIMD code then
        // forwards whole to the copy's load.
        emitSource(code);
        var operand = WasmInstructionOperand.PrefixedTriple(
            WasmSimdOpcodes.V128Load, GetEncodedAlignment(alignment), 0);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.SimdPrefixed, operand));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.PrefixedTriple(
                WasmSimdOpcodes.V128Store, GetEncodedAlignment(alignment), 0)));
    }

    private static uint GetEncodedAlignment(int alignment)
    {
        return alignment switch
        {
            >= 16 => 4,
            >= sizeof(long) => 3,
            >= sizeof(int) => 2,
            >= sizeof(short) => 1,
            _ => 0,
        };
    }
}

internal static class ValueCopyContract
{
    public static void Validate(
        IWasmInstructionWriter code,
        int destinationLocal,
        int size,
        int alignment,
        Action<IWasmInstructionWriter> emitSource)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationLocal);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if (alignment <= 0 || alignment > 16 ||
            (alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }
        ArgumentNullException.ThrowIfNull(emitSource);
    }
}
