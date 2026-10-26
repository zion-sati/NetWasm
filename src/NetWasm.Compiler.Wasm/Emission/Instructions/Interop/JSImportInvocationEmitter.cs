using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class JSImportInvocationEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IJavaScriptResultDescriptorSizeResolver resultDescriptorSizes,
    IJavaScriptImportArgumentEmitter arguments,
    IHostCallbackHandleEmitter callbackHandles,
    IInteropHandleReleaser interopHandles,
    IJavaScriptImportResultEmitter results,
    IImplicitExceptionEmitter exceptions,
    IStackLocalResolver stackLocals) : IJSImportInvocationEmitter
{
    public void Emit(
        JSImportInvocation invocation,
        IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        if (JavaScriptDirectImport.Applies(invocation.Method.Definition))
        {
            EmitDirect(invocation, code, functionIndices);
            return;
        }
        var context = invocation.Context;
        var signature = invocation.Method.Signature;
        ManagedMemoryEmitter.EmitAddressConstant(
            code,
            layouts.Target,
            resultDescriptorSizes.Resolve(signature.ReturnSignatureType));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameEnter)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        HostCallbackDeclaration? callback = null;
        for (var index = 0; index < invocation.Consumed; index++)
        {
            var local = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
                context.StackLocals,
                invocation.ArgumentBase + index,
                invocation.Stack[invocation.ArgumentBase + index],
                layouts.Target);
            if (invocation.Target.HostCallbacks.TryGetValue(
                    (invocation.Method.Definition.Key, index),
                    out var value))
            {
                callback = value;
                callbackHandles.Emit(code, local, context);
            }
            else
            {
                arguments.Emit(
                    code,
                    signature.ParameterSignatureTypes[index],
                    local,
                    context,
                    invocation.Target.InteropImports);
            }
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(functionIndices.Resolve(new InteropImportKey(invocation.Method.Definition.Key))))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        if (callback is not null)
        {
            interopHandles.Release(code, context);
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameLeave)))));
        exceptions.Emit(code, ManagedExceptionKind.JSException, invocation.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        results.Emit(new(
            signature,
            invocation.Stack,
            context,
            invocation.ArgumentBase,
            invocation.Consumed,
            callback,
            new InteropMarshallingTarget(invocation.Target.InteropImports),
            invocation.Offset), code);
    }

    // The arguments are the import's parameters and its result is the call's
    // result. Anything the host throws unwinds to the catch_all, which raises
    // JSException as a failed status does:
    //   block (result t)
    //     block
    //       try_table (result t) (catch_all 0)  arguments, call  end
    //       br 1
    //     end
    //     throw JSException
    //   end
    private void EmitDirect(
        JSImportInvocation invocation,
        IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        var context = invocation.Context;
        var signature = invocation.Method.Signature;
        var resultType = signature.ReturnType == CliValueKind.Void
            ? WasmOpcodes.EmptyBlockType
            : (byte)WasmValueTypes.FromCli(signature.ReturnType, layouts.Target);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(resultType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.TryTable, WasmInstructionOperand.TryTableCatchAll(resultType, 0)));
        for (var index = 0; index < invocation.Consumed; index++)
        {
            arguments.Emit(
                code,
                signature.ParameterSignatureTypes[index],
                WasmLocalLayoutPlanner.GetEvaluationStackLocal(
                    context.StackLocals,
                    invocation.ArgumentBase + index,
                    invocation.Stack[invocation.ArgumentBase + index],
                    layouts.Target),
                context,
                invocation.Target.InteropImports);
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(functionIndices.Resolve(new InteropImportKey(invocation.Method.Definition.Key))))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Branch, WasmInstructionOperand.Unsigned(1)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        exceptions.Emit(code, ManagedExceptionKind.JSException, context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        invocation.Stack.RemoveRange(invocation.ArgumentBase, invocation.Consumed);
        if (signature.ReturnType != CliValueKind.Void)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)stackLocals.Resolve(
                context, invocation.ArgumentBase, signature.ReturnType))));
            invocation.Stack.Add(signature.ReturnType);
        }
    }
}
