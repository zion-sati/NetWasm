using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// ArrayLoops.Copy<T>(destination, source, start, end) copies the elements'
// bytes as NativeMemory.Copy does, which is right when both are one array.
internal sealed class ArrayLoopCopyIntrinsicEmitter(
    IRuntimeObjectLayout objects,
    IValueLayoutProvider values) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var size = values.GetValueLayout(request.Method.MethodArguments[0]).Size;
        var target = request.Target;
        var destination = request.Local(0, CliValueKind.ManagedReference);
        var source = request.Local(1, CliValueKind.ManagedReference);
        var start = request.Local(2, CliValueKind.I4);
        var end = request.Local(3, CliValueKind.I4);
        // The arrays' and start's slots, which the copy no longer reads.
        var destinationAddress = request.Local(0, CliValueKind.NativeInt);
        var sourceAddress = request.Local(1, CliValueKind.NativeInt);
        var byteCount = request.Local(2, CliValueKind.NativeInt);
        ArrayLoopEmitter.Emit(request, code, objects, [destination, source], start, end, () =>
        {
            ArrayLoopEmitter.ElementAddress(code, target, destination, start, size, destinationAddress);
            ArrayLoopEmitter.ElementAddress(code, target, source, start, size, sourceAddress);
            ArrayLoopEmitter.ByteCount(code, target, start, end, size, byteCount);
            SmallBlockEmitter.EmitCopy(code, target, inline: true, destinationAddress, sourceAddress, byteCount);
        });
    }
}
