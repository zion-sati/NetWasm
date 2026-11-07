using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// Calls a multicast delegate's entries in order. Its invoke function calls this only for
// a delegate with an invocation list, so a single-cast call keeps none of this function's
// locals or roots. The list holds at least two single-cast entries (Combine and Remove make
// a single result the delegate itself), and the delegate and the references its arguments
// hold stay rooted while the entries run. Each entry is dispatched here, a second copy of
// the invoke function's dispatch, or handed to the invoke function, which takes the same
// parameters with the entry in the delegate's place.
internal sealed class DelegateMulticastFunctionEmitter(
    ITargetLayout layouts,
    IRuntimeObjectLayout objects,
    IRuntimeImportResolver runtimeImports,
    IManagedMethodFunctionTypeResolver functionTypes,
    IDelegateInvocationTargetSelector targets,
    IGeneratedFunctionWriterFactory writers,
    IDelegateArgumentRootEmitter roots,
    IDelegateDispatchEmitter dispatch) : IDelegateMulticastFunctionEmitter
{
    public byte[] Emit(
        MethodInstanceModel invoke,
        DelegateInvokeTarget targetProgram,
        IFunctionIndexResolver functionIndices,
        WasmFunctionIndex invokeFunction)
    {
        var signature = DelegateInvokeSignature.Of(invoke);
        var parameterCount = functionTypes.Resolve(invoke).Parameters.Length;
        var resultLocal = parameterCount;
        var rootFrameLocal = resultLocal + 1;
        var invocationListLocal = rootFrameLocal + 1;
        var invocationCountLocal = invocationListLocal + 1;
        var invocationIndexLocal = invocationCountLocal + 1;
        var currentDelegateLocal = invocationIndexLocal + 1;
        var initializationFrameLocal = currentDelegateLocal + 1;
        var methodIdLocal = initializationFrameLocal + 1;

        var output = writers.Create();
        var code = output.Instructions;
        // A list rather than an ImmutableArray spread: NativeAOT-LLVM, which compiles this
        // compiler for the browser, crashes on that (https://github.com/dotnet/runtimelab/issues/3329).
        List<CliValueKind> locals =
        [
            signature.ResultType,
            CliValueKind.ManagedAddress,
            CliValueKind.ManagedReference,
            CliValueKind.I4,
            CliValueKind.I4,
            CliValueKind.ManagedReference,
        ];
        if (targetProgram.InlineMulticastDispatch)
        {
            locals.Add(CliValueKind.ManagedAddress);
            if (targets.Select(invoke, targetProgram.Bindings).Length > DelegateDispatch.SequentialTargets)
            {
                locals.Add(CliValueKind.I4);
            }
        }
        WasmLocalDeclarationWriter.Write(output.Bytes, locals, layouts.Target);

        roots.Emit(code, invoke, rootFrameLocal);
        WriteLocalGet(code, signature.Receiver);
        EmitReferenceLoad(code, objects.DelegateInvocationListOffset);
        WriteLocalSet(code, invocationListLocal);
        WriteLocalGet(code, signature.Receiver);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, checked((uint)objects.DelegateInvocationCountOffset))));
        WriteLocalSet(code, invocationCountLocal);
        WriteI32Constant(code, 0);
        WriteLocalSet(code, invocationIndexLocal);
        EmitInvocationListEntry(code, invocationListLocal, invocationIndexLocal, currentDelegateLocal,
            targetProgram.InlineArrayData);

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Loop,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        if (targetProgram.InlineMulticastDispatch)
        {
            dispatch.Emit(code, new(invoke, targetProgram, currentDelegateLocal, resultLocal, initializationFrameLocal, methodIdLocal),
                functionIndices);
        }
        else
        {
            for (var parameter = 0; parameter < parameterCount; parameter++)
            {
                WriteLocalGet(code, parameter == signature.Receiver ? currentDelegateLocal : parameter);
            }
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned(checked((uint)invokeFunction.Value))));
            if (signature.ReturnsScalar)
            {
                WriteLocalSet(code, resultLocal);
            }
        }
        WriteLocalGet(code, invocationIndexLocal);
        WriteI32Constant(code, 1);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalTee,
            WasmInstructionOperand.Unsigned((uint)invocationIndexLocal)));
        WriteLocalGet(code, invocationCountLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.BranchIf,
            WasmInstructionOperand.Unsigned(1)));
        EmitInvocationListEntry(code, invocationListLocal, invocationIndexLocal, currentDelegateLocal,
            targetProgram.InlineArrayData);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        // An exception leaves the root frame in place for its catcher to rewind.
        WriteLocalGet(code, rootFrameLocal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(checked((uint)runtimeImports.Resolve(RuntimeImportSymbol.RootFrameLeave)))));
        if (signature.ReturnsScalar)
        {
            WriteLocalGet(code, resultLocal);
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return output.Snapshots.Read();
    }

    // The list's entry at the index, which becomes the delegate invoked next.
    private void EmitInvocationListEntry(
        IWasmInstructionWriter code, int invocationListLocal, int invocationIndexLocal, int currentDelegateLocal,
        bool inlineArrayData)
    {
        ManagedMemoryEmitter.EmitArrayElementAddress(
            code,
            layouts.Target,
            invocationListLocal,
            invocationIndexLocal,
            inlineArrayData,
            layouts.Target.ObjectReferenceSize);
        EmitReferenceLoad(code, 0);
        WriteLocalSet(code, currentDelegateLocal);
    }

    private void EmitReferenceLoad(IWasmInstructionWriter code, int offset) =>
        code.Write(WasmInstruction.WithOperand(
            layouts.Target.UsesMemory64 ? WasmOpcodes.I64Load : WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(layouts.Target.UsesMemory64 ? 3u : 2u, checked((uint)offset))));

    private static void WriteLocalGet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(checked((uint)index))));

    private static void WriteLocalSet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(checked((uint)index))));

    private static void WriteI32Constant(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));
}
