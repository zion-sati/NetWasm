using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// ArrayLoops.Fill<T>(array, start, end, value): one-byte elements fill as
// NativeMemory.Fill does; wider ones store vectors of the value.
internal sealed class ArrayLoopFillIntrinsicEmitter(
    IRuntimeObjectLayout objects,
    IValueLayoutProvider values) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var element = request.Method.MethodArguments[0];
        var size = values.GetValueLayout(element).Size;
        var target = request.Target;
        var array = request.Local(0, CliValueKind.ManagedReference);
        var start = request.Local(1, CliValueKind.I4);
        var end = request.Local(2, CliValueKind.I4);
        var value = request.Local(3, element.StackKind);
        // The array's and start's slots, which the work no longer reads.
        var destination = request.Local(0, CliValueKind.NativeInt);
        var byteCount = request.Local(1, CliValueKind.NativeInt);
        ArrayLoopEmitter.Emit(request, code, objects, [array], start, end, () =>
        {
            ArrayLoopEmitter.ElementAddress(code, target, array, start, size, destination);
            ArrayLoopEmitter.ByteCount(code, target, start, end, size, byteCount);
            if (size == 1)
            {
                SmallBlockEmitter.EmitFill(code, target, inline: true, destination, value, byteCount);
                return;
            }
            SmallBlockEmitter.EmitPatternFill(code, target, destination, byteCount, size, () =>
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)value)));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.SimdPrefixed, WasmInstructionOperand.Prefixed(
                    (element.StackKind, size) switch
                    {
                        (CliValueKind.F4, _) => WasmSimdOpcodes.F32x4Splat,
                        (CliValueKind.F8, _) => WasmSimdOpcodes.F64x2Splat,
                        (_, 2) => WasmSimdOpcodes.I16x8Splat,
                        (_, 4) => WasmSimdOpcodes.I32x4Splat,
                        _ => WasmSimdOpcodes.I64x2Splat,
                    })));
            });
        });
    }
}
