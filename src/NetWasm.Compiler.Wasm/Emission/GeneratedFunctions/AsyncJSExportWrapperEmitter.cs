using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSExportWrapperEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider valueLayouts,
    IRuntimeImportResolver runtimeImports,
    IRuntimeStateInitializer runtimeInitialization,
    IImplicitExceptionEmitter exceptions,
    IReferenceComparisonEmitter references,
    IGeneratedFunctionWriterFactory writers,
    IJavaScriptReferenceArgumentEmitter referenceArguments,
    IJavaScriptValueFramePlanner valueFramePlanner,
    IJavaScriptValueFrameEmitter valueFrames) : IAsyncJSExportWrapperEmitter
{
    public byte[] Emit(
        MethodDefinitionModel method,
        JavaScriptAsyncMethodBinding binding,
        RuntimeInitializationPlan initialization,
        bool hasFinalizers,
        IFunctionIndexResolver functionIndices,
        EntityKey? argumentFactory = null,
        InteropImportPlan? interopImports = null)
    {
        // At a JavaScript boundary a nullable parameter arrives as two Wasm values.
        var parameterLocals = interopImports is not null
            ? InteropTypeClassifier.TransportLocals(method.Signature.ParameterSignatureTypes, 0)
            : [.. Enumerable.Range(0, method.Signature.ParameterTypes.Length + 1)];
        var wrapperParameterCount = argumentFactory is null
            ? parameterLocals[^1]
            : 0;
        var taskLocal = wrapperParameterCount;
        var handleLocal = taskLocal + 1;
        var rootFrameLocal = handleLocal + 1;
        var valueFrameLocal = rootFrameLocal + 1;
        var finalizerExceptionLocal = valueFrameLocal + 1;
        var exceptionFrameLocal = finalizerExceptionLocal + 1;
        // String, array, and JSObject parameters arrive as host handles. They become
        // managed objects in an argument root frame that lives until the method returns.
        var referenceParameters = argumentFactory is null && interopImports is not null
            ? method.Signature.ParameterSignatureTypes
                .Select((type, index) => (Type: type, Index: index))
                .Where(parameter => InteropTypeClassifier.IsJavaScriptReference(parameter.Type))
                .ToArray()
            : [];
        var rootSlots = referenceParameters
            .Select((parameter, slot) => (parameter.Index, Slot: slot))
            .ToDictionary(parameter => parameter.Index, parameter => parameter.Slot);
        var argumentFrameLocal = exceptionFrameLocal + 1;
        var temporaryI4 = argumentFrameLocal + 1;
        var objectTemporary = temporaryI4 + 1;
        var argumentLocal = objectTemporary + 1;
        // Date and nullable parameters become structs in a value frame that lives until
        // the method returns its task. The result is read from the task later.
        var converted = argumentFactory is null && interopImports is not null
            ? valueFramePlanner.Plan(method.Signature.ParameterSignatureTypes, CliTypeIdentity.FromStackKind(CliValueKind.Void))
            : null;
        var convertedFrameLocal = referenceParameters.Length == 0 ? argumentFrameLocal : argumentLocal + 1;
        var code = writers.Create();
        WasmLocalDeclarationWriter.Write(
            code.Bytes,
            [
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                .. (referenceParameters.Length == 0
                    ? Array.Empty<CliValueKind>()
                    : [CliValueKind.ManagedAddress, CliValueKind.I4, CliValueKind.ManagedReference,
                        CliValueKind.ManagedReference]),
                .. (converted is null ? Array.Empty<CliValueKind>() : [CliValueKind.ManagedAddress]),
            ],
            layouts.Target);
        runtimeInitialization.Initialize(code, initialization);
        // The exception frame marks the arenas as the host call found them. A
        // failure rewinds every frame the call left behind and propagates unchanged.
        ManagedMemoryEmitter.EmitAddressConstant(code.Instructions, layouts.Target, 0);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        CallRuntime(code, RuntimeImportSymbol.ExceptionFrameEnter, initialization);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)exceptionFrameLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType((byte)WasmValueType.ExnRef)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.TryTable,
            WasmInstructionOperand.TryTableCatchAllRef(WasmOpcodes.EmptyBlockType, 0)));
        if (referenceParameters.Length != 0)
        {
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(referenceParameters.Length)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.RootFrameEnter, initialization.RuntimeImportSelection))));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)argumentFrameLocal)));
            var target = new InteropMarshallingTarget(interopImports!);
            foreach (var parameter in referenceParameters)
            {
                referenceArguments.Emit(code.Instructions, parameter.Type, parameterLocals[parameter.Index], argumentLocal,
                    temporaryI4, objectTemporary, target, functionIndices.ManagedThrowFunction);
                ManagedMemoryEmitter.EmitRootSlotStore(code.Instructions, layouts.Target,
                    argumentFrameLocal, rootSlots[parameter.Index], argumentLocal);
            }
        }
        if (converted is not null)
            valueFrames.Emit(
                code.Instructions, new JavaScriptValueFrameEnter(converted, convertedFrameLocal, [.. parameterLocals], interopImports!));
        if (binding.Return.Kind == JavaScriptAsyncReturnKind.ValueTask)
        {
            var valueLayout = valueLayouts.GetValueLayout(
                method.Signature.ReturnSignatureType);
            ManagedMemoryEmitter.EmitAddressConstant(
                code.Instructions,
                layouts.Target,
                valueLayout.Size);
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.ValueFrameEnter, initialization.RuntimeImportSelection))));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)valueFrameLocal)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)valueFrameLocal)));
            EmitArguments(
                code,
                method.Signature.ParameterTypes.Length,
                argumentFactory,
                functionIndices,
                rootSlots,
                argumentFrameLocal,
                converted,
                convertedFrameLocal,
                parameterLocals);
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(
                    method.Key))));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)valueFrameLocal)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(
                    binding.ValueTaskAsTask!))));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)taskLocal)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)valueFrameLocal)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.ValueFrameLeave, initialization.RuntimeImportSelection))));
        }
        else
        {
            EmitArguments(
                code,
                method.Signature.ParameterTypes.Length,
                argumentFactory,
                functionIndices,
                rootSlots,
                argumentFrameLocal,
                converted,
                convertedFrameLocal,
                parameterLocals);
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(
                    method.Key))));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)taskLocal)));
        }
        if (converted is not null)
            valueFrames.Emit(code.Instructions, new JavaScriptValueFrameLeave(convertedFrameLocal));
        // Normal completion leaves the argument frame and skips the handler.
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        if (referenceParameters.Length != 0)
            LeaveFrame(code, argumentFrameLocal, initialization);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)exceptionFrameLocal)));
        CallRuntime(code, RuntimeImportSymbol.ExceptionFrameLeave, initialization);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(1)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)exceptionFrameLocal)));
        CallRuntime(code, RuntimeImportSymbol.ExceptionFrameUnwind, initialization);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.ThrowRef));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        EmitReferenceNullCheck(code, taskLocal, functionIndices.ManagedThrowFunction);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.RootFrameEnter, initialization.RuntimeImportSelection))));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)rootFrameLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)rootFrameLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)taskLocal)));
        ManagedMemoryEmitter.EmitStoreBySize(
            code.Instructions,
            layouts.Target,
            0,
            layouts.Target.ObjectReferenceSize);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)taskLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.HandleNew, initialization.RuntimeImportSelection))));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)handleLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)rootFrameLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.RootFrameLeave, initialization.RuntimeImportSelection))));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)handleLocal)));
        references.Emit(
            code.Instructions,
            ReferenceComparison.EqualZero,
            CliValueKind.I4);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code.Instructions, ManagedExceptionKind.OutOfMemory, functionIndices.ManagedThrowFunction);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        if (hasFinalizers)
        {
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.FinalizerSafepoint, initialization.RuntimeImportSelection))));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalTee,
                WasmInstructionOperand.Unsigned((uint)finalizerExceptionLocal)));
            references.Emit(code.Instructions, ReferenceComparison.EqualZero);
            code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)finalizerExceptionLocal)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Throw,
                WasmInstructionOperand.Unsigned(0)));
            code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)handleLocal)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }

    private void CallRuntime(
        GeneratedFunctionWriterLease code,
        RuntimeImportSymbol symbol,
        RuntimeInitializationPlan initialization) =>
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                symbol, initialization.RuntimeImportSelection))));

    private void LeaveFrame(
        GeneratedFunctionWriterLease code,
        int frameLocal,
        RuntimeInitializationPlan initialization)
    {
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)frameLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.RootFrameLeave, initialization.RuntimeImportSelection))));
    }

    private void EmitArguments(
        GeneratedFunctionWriterLease code,
        int count,
        EntityKey? argumentFactory,
        IFunctionIndexResolver functionIndices,
        Dictionary<int, int> rootSlots,
        int argumentFrameLocal,
        JavaScriptValueFrame? converted,
        int convertedFrameLocal,
        int[] parameterLocals)
    {
        if (argumentFactory is { } factory)
        {
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(factory))));
            return;
        }

        for (var index = 0; index < count; index++)
        {
            if (rootSlots.TryGetValue(index, out var slot))
            {
                code.Instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)argumentFrameLocal)));
                ManagedMemoryEmitter.EmitLoadBySize(
                    code.Instructions,
                    layouts.Target,
                    slot * layouts.Target.ObjectReferenceSize,
                    layouts.Target.ObjectReferenceSize);
                continue;
            }
            if (converted?.Parameters.TryGetValue(index, out var valueSlot) == true)
            {
                valueFrames.Emit(code.Instructions, new JavaScriptValueFrameAddress(convertedFrameLocal, valueSlot.Offset));
                continue;
            }
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)parameterLocals[index])));
        }
    }

    private void EmitReferenceNullCheck(
        GeneratedFunctionWriterLease code,
        int local,
        OptionalFunctionIndex throwFunction)
    {
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));
        references.Emit(code.Instructions, ReferenceComparison.EqualZero);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code.Instructions, ManagedExceptionKind.NullReference, throwFunction);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

}
