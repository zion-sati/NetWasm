using System;
using System.Collections.Frozen;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// A call to an intrinsic the safepoint classifier does not name publishes no
// roots, so the code the intrinsic emits must not collect: a collection there
// would free what the caller still holds, and the memory's next object would
// stand in for it. Such an intrinsic writes through a writer whose calls to the
// runtime functions that can collect stop the compilation instead.
internal sealed class SafepointCheckedWriterFactory(
    IRuntimeAllocationSafepointClassifier safepoints,
    ICollectingRuntimeImportResolver collecting) : ISafepointCheckedWriterFactory
{
    public IWasmInstructionWriter Create(IWasmInstructionWriter code, RuntimeIntrinsicEmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        return safepoints.Classify(request.Method)
            ? code
            : new CheckedWriter(code, request, collecting.Resolve(request.Instruction.Target.RuntimeImportSelection));
    }

    private sealed class CheckedWriter(
        IWasmInstructionWriter code,
        RuntimeIntrinsicEmissionRequest request,
        FrozenDictionary<uint, RuntimeImportSymbol> collecting) : IWasmInstructionWriter
    {
        public void Write(WasmInstruction instruction)
        {
            if (instruction.Opcode == WasmOpcodes.Call && collecting.TryGetValue(instruction.Operand.UnsignedValue, out var symbol))
            {
                throw new InvalidOperationException(
                    $"Runtime intrinsic {request.Intrinsic} for {request.Method.CanonicalName} calls the runtime's {symbol}, " +
                    "which can collect, but a call to it is not a safepoint.");
            }
            code.Write(instruction);
        }
    }
}
