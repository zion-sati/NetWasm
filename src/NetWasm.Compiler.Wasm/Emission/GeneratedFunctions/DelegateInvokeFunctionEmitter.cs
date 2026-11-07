using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed record DelegateInvokeTarget(
    ImmutableArray<ManagedDelegateBinding> Bindings,
    IEntryLookup<string, StaticInitializerGuard> StaticInitializerGuards,
    bool InlineArrayData = false,
    bool InlineMulticastDispatch = false);

// A delegate's Invoke: a single-cast delegate calls its target and returns, rooting
// nothing, since nothing is read once the target runs. A delegate with an invocation
// list is handed to the type's multicast function, whose loop, roots and locals stay out
// of this function, so a single-cast call keeps a small frame.
internal sealed class DelegateInvokeFunctionEmitter(
    ITargetLayout layouts,
    IRuntimeObjectLayout objects,
    IManagedMethodFunctionTypeResolver functionTypes,
    IDelegateInvocationTargetSelector targets,
    IGeneratedFunctionWriterFactory writers,
    IDelegateDispatchEmitter dispatch) : IDelegateInvokeFunctionEmitter
{
    public byte[] Emit(
        MethodInstanceModel invoke,
        DelegateInvokeTarget targetProgram,
        IFunctionIndexResolver functionIndices,
        WasmFunctionIndex multicastFunction)
    {
        var signature = DelegateInvokeSignature.Of(invoke);
        var parameterCount = functionTypes.Resolve(invoke).Parameters.Length;
        var resultLocal = parameterCount;
        var initializationFrameLocal = resultLocal + 1;
        var methodIdLocal = initializationFrameLocal + 1;

        var output = writers.Create();
        var code = output.Instructions;
        // A list rather than an ImmutableArray spread: NativeAOT-LLVM, which compiles this
        // compiler for the browser, crashes on that (https://github.com/dotnet/runtimelab/issues/3329).
        List<CliValueKind> locals = [signature.ResultType, CliValueKind.ManagedAddress];
        if (targets.Select(invoke, targetProgram.Bindings).Length > DelegateDispatch.SequentialTargets)
        {
            locals.Add(CliValueKind.I4);
        }
        WasmLocalDeclarationWriter.Write(output.Bytes, locals, layouts.Target);

        WriteLocalGet(code, signature.Receiver);
        code.Write(WasmInstruction.WithOperand(
            layouts.Target.UsesMemory64 ? WasmOpcodes.I64Load : WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(layouts.Target.UsesMemory64 ? 3u : 2u,
                checked((uint)objects.DelegateInvocationListOffset))));
        code.Write(WasmInstruction.NoOperand(
            layouts.Target.UsesMemory64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        dispatch.Emit(code, new(invoke, targetProgram, signature.Receiver, resultLocal, initializationFrameLocal, methodIdLocal),
            functionIndices);
        if (signature.ReturnsScalar)
        {
            WriteLocalGet(code, resultLocal);
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        for (var parameter = 0; parameter < parameterCount; parameter++)
        {
            WriteLocalGet(code, parameter);
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(checked((uint)multicastFunction.Value))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return output.Snapshots.Read();
    }

    private static void WriteLocalGet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(checked((uint)index))));
}
