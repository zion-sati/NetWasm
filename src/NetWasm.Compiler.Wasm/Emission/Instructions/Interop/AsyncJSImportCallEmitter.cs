using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class AsyncJSImportCallEmitter(
    ITargetLayout layouts,
    ITypeLayoutProvider typeLayouts,
    IValueLayoutProvider valueLayouts,
    IInstanceFieldLayoutProvider fieldLayouts,
    IRuntimeImportResolver runtimeImports,
    IJavaScriptImportArgumentEmitter arguments,
    IImplicitExceptionEmitter exceptions) : ICallEmitter
{
    public void Emit(CallEmissionRequest request, IWasmInstructionWriter code, IFunctionIndexResolver functionIndices)
    {
        var instruction = request.Instruction;
        var context = instruction.Context;
        var binding = instruction.Target.JavaScriptAsyncBindings[
            request.Method.Definition.Key];
        var taskLayout = typeLayouts.GetObjectLayout(binding.TaskType);

        EmitAddressConstant(code, taskLayout.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(taskLayout.TypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.Allocate)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(context.ObjectTemporary))));
        EmitReferenceFailure(code, context.ObjectTemporary);

        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(1)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.RootFrameEnter)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.ObjectTemporary))));
        ManagedMemoryEmitter.EmitStoreBySize(
            code,
            layouts.Target,
            0,
            layouts.Target.ObjectReferenceSize);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.ObjectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.HandleNew)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(context.InteropResult))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.RootFrameLeave)))));
        EmitHandleFailure(code, context.InteropResult);

        EmitAddressConstant(code, sizeof(int));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameEnter)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        for (var index = 0; index < request.Consumed; index++)
        {
            var local = StackLocal(
                context,
                request.ArgumentBase + index,
                instruction.Stack[request.ArgumentBase + index]);
            arguments.Emit(
                code,
                request.Method.Signature.ParameterSignatureTypes[index],
                local,
                context);
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropResult))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(functionIndices.Resolve(request.Method.Definition.Key)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropResult))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.HandleRelease)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameLeave)))));
        exceptions.Emit(code, ManagedExceptionKind.JSException);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameLeave)))));

        instruction.Stack.RemoveRange(request.ArgumentBase, request.Consumed);
        if (binding.Return.Kind == JavaScriptAsyncReturnKind.Task)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.ObjectTemporary))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(StackLocal(
                context,
                request.ArgumentBase,
                CliValueKind.ManagedReference)))));
            instruction.Stack.Add(CliValueKind.ManagedReference);
            return;
        }

        var destinationOffset = context.ValueLayout.TemporaryOffsets[
            instruction.Instruction.Offset];
        EmitValueAddress(code, context, destinationOffset);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        EmitAddressConstant(code, valueLayouts.GetValueLayout(
            request.Method.Signature.ReturnSignatureType).Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
        EmitValueAddress(code, context, destinationOffset);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.ObjectTemporary))));
        var taskField = fieldLayouts.GetFieldLayout(
            binding.ValueTaskTaskField!);
        ManagedMemoryEmitter.EmitStoreBySize(
            code,
            layouts.Target,
            taskField.Offset,
            layouts.Target.ObjectReferenceSize);
        EmitValueAddress(code, context, destinationOffset);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(StackLocal(
            context,
            request.ArgumentBase,
            CliValueKind.ValueType)))));
        instruction.Stack.Add(CliValueKind.ValueType);
    }

    private void EmitReferenceFailure(IWasmInstructionWriter code, int local)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(local))));
        if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64EqualZero));
        else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitHandleFailure(IWasmInstructionWriter code, int local)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(local))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitValueAddress(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        int offset)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.ValueFrame))));
        if (offset == 0)
        {
            return;
        }
        EmitAddressConstant(code, offset);
        if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
        else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
    }

    private void EmitAddressConstant(IWasmInstructionWriter code, int value)
    {
        if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value)));
        else code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
    }

    private int StackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            context.StackLocals,
            slot,
            type,
            layouts.Target);
}
