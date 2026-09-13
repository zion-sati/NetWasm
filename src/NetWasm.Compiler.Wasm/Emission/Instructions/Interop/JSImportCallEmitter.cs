using NetWasm.Compiler.Wasm.Encoding;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class JSImportCallEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IJavaScriptResultDescriptorSizeResolver resultDescriptorSizes,
    IJavaScriptImportArgumentEmitter arguments,
    IHostCallbackHandleEmitter callbackHandles,
    IInteropHandleReleaser interopHandles,
    IJavaScriptImportResultEmitter results,
    IImplicitExceptionEmitter exceptions) : ICallEmitter
{
    public void Emit(CallEmissionRequest request, IWasmInstructionWriter code, IFunctionIndexResolver functionIndices)
    {
        var instruction = request.Instruction;
        var context = instruction.Context;
        var signature = request.Method.Signature;
        ManagedMemoryEmitter.EmitAddressConstant(
            code,
            layouts.Target,
            resultDescriptorSizes.Resolve(signature.ReturnSignatureType));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameEnter)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        HostCallbackDeclaration? callback = null;
        for (var index = 0; index < request.Consumed; index++)
        {
            var local = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
                context.StackLocals,
                request.ArgumentBase + index,
                instruction.Stack[request.ArgumentBase + index],
                layouts.Target);
            if (instruction.Target.HostCallbacks.TryGetValue(
                    (request.Method.Definition.Key, index),
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
                    context);
            }
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(functionIndices.Resolve(request.Method.Definition.Key)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        if (callback is not null)
        {
            interopHandles.Release(code, context);
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameLeave)))));
        exceptions.Emit(code, ManagedExceptionKind.JSException);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        results.Emit(new(
            signature,
            instruction.Stack,
            context,
            request.ArgumentBase,
            request.Consumed,
            callback,
            new InteropMarshallingTarget(instruction.Target.InteropImports)), code);
    }
}
