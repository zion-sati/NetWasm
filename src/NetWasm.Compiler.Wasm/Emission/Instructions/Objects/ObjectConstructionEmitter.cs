using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class ObjectConstructionEmitter(
    IMethodRepository methods,
    ISymbolFormatter symbols,
    ITypeClassifier typeClassifier,
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    ITypeLayoutProvider typeLayouts,
    IValueLayoutProvider values,
    IRuntimeObjectLayout objects,
    ICilTypeIdentityResolver types,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IRootPublicationEmitter roots,
    INativeIntegerConstructionEmitter nativeIntegerConstructions,
    IStaticInitializationEmitter initialization,
    IStaticInitializerGuardResolver guards,
    IDelegateTargetCheckSelector delegateTargets) :
    InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new InstructionCommand(
            CilOperation.NewObject,
            InstructionFamily.AllocationBoxingTypes,
            EmitNewObject)
        {
            PublishesRoots = true,
        },
    ];

    private void EmitNewObject(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        var constructorInstance = request.Instruction.Operand as CilOperand.MethodInstance;
        var constructor = constructorInstance?.Value.Definition ??
                          methods.GetMethod(GetEntity(request.Instruction));
        var signature = constructorInstance?.Value.Signature ?? constructor.Signature;
        var declaringType = constructorInstance?.Value.DeclaringType ??
                            types.Resolve(constructor.DeclaringType);
        var initializationRequest = new StaticInitializationEmissionRequest(constructor.DeclaringType,
            declaringType, request.Target.StaticInitializerGuards, IsMethodInvocation: true);
        // newobj publishes its safepoint's roots itself (PublishesRoots): first,
        // or only on the slow path of an allocation that bumps its run inline.
        var deferRoots = DefersRoots(request, constructor, declaringType,
            constructorInstance is not null, initializationRequest);
        if (!deferRoots) roots.Emit(request, code);
        initialization.Emit(initializationRequest, code, functionIndices);
        var argumentBase = request.Stack.Count - signature.ParameterTypes.Length;
        if (symbols.Format(constructor.DeclaringType) == "System.String")
        {
            throw new InvalidOperationException(
                "A string constructor reached emission; StringConstructorRewriter calls String.Ctor instead.");
        }
        if (typeClassifier.IsDelegateType(constructor.DeclaringType))
        {
            EmitDelegate(request, code, signature, declaringType, argumentBase, deferRoots);
            return;
        }
        if (declaringType.StackKind == CliValueKind.NativeInt)
        {
            nativeIntegerConstructions.Emit(
                new NativeIntegerConstructionRequest(request, signature, argumentBase),
                code);
            return;
        }
        if (declaringType.IsValueType)
        {
            EmitValueType(
                request, code, constructor,
                signature,
                declaringType,
                argumentBase,
                functionIndices);
            return;
        }
        EmitReferenceType(
            request, code, constructor,
            signature,
            declaringType,
            argumentBase,
            constructorInstance is not null,
            deferRoots,
            functionIndices);
    }

    // An object allocated by bumping its run inline publishes the instruction's
    // roots only when the bump falls back to the collector. That needs nothing
    // else in the instruction to collect before it, so no static initializer
    // guard, and no handler or filter of this method that could see the frame's
    // roots if the constructor throws.
    private bool DefersRoots(
        InstructionEmissionRequest request,
        MethodDefinitionModel constructor,
        CliTypeIdentity declaringType,
        bool isConstructed,
        StaticInitializationEmissionRequest initializationRequest)
    {
        var context = request.Context;
        if (!request.Target.InlineObjectAllocation || declaringType.IsValueType ||
            context.ActiveExceptionGroup is not null || context.IsFilterFunclet ||
            context.FilterEnvironment.RootSlotCount != 0 || guards.TryResolve(initializationRequest, out _))
        {
            return false;
        }
        if (typeClassifier.IsDelegateType(constructor.DeclaringType)) return true;
        var layout = isConstructed
            ? typeLayouts.GetObjectLayout(declaringType)
            : typeLayouts.GetObjectLayout(constructor.DeclaringType);
        return !request.Target.FinalizableTypeIds.Contains(layout.TypeId);
    }

    // Allocates the object into the object temporary, throwing when memory is
    // exhausted; with deferred roots, the bump's fallback publishes them first.
    private void EmitAllocation(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        ObjectLayout layout,
        bool deferRoots)
    {
        var objectTemporary = request.Context.ObjectTemporary;
        if (deferRoots)
        {
            EmitInlineAllocation(request, code, layout);
            return;
        }
        if (request.Target.FinalizableTypeIds.Contains(layout.TypeId))
        {
            addresses.Emit(code, layout.Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(layout.TypeId)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(
                RuntimeImportSymbol.AllocateFinalizable,
                request.Target.RuntimeImportSelection)))));
        }
        else
        {
            ObjectAllocationCall.Emit(code, addresses, runtimeImports, request.Target, layout);
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)objectTemporary)));
        EmitAllocationFailureCheck(code, objectTemporary, request.Context.ManagedThrowFunction);
    }

    // The run bump, inline: when the collector keeps runs and end >= cursor +
    // slot, the object is the run's zeroed slot at cursor, so the cursor advances
    // and the object takes its type id; otherwise the roots are published and the
    // fallback refills the run. The runtime calls fold to constants and one load
    // once inlined: for a collector without runs the bump folds away, and for one
    // with runs the fast path's only branch is the run's bound check.
    private void EmitInlineAllocation(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        ObjectLayout layout)
    {
        var target = layouts.Target;
        var objectTemporary = request.Context.ObjectTemporary;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        ObjectAllocationCall.EmitRunsEnabled(code, runtimeImports, request.Target);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.BranchIf, WasmInstructionOperand.Unsigned(0)));
        ObjectAllocationCall.EmitRun(code, addresses, runtimeImports, request.Target, layout);
        ManagedMemoryEmitter.EmitLoadBySize(code, target, target.ObjectReferenceSize, target.ObjectReferenceSize);
        ObjectAllocationCall.EmitRun(code, addresses, runtimeImports, request.Target, layout);
        ManagedMemoryEmitter.EmitLoadBySize(code, target, 0, target.ObjectReferenceSize);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)objectTemporary)));
        ObjectAllocationCall.EmitSlot(code, addresses, runtimeImports, request.Target, layout);
        addresses.Emit(code, AddressOperation.Add);
        code.Write(WasmInstruction.NoOperand(target.UsesMemory64
            ? WasmOpcodes.I64LessThanUnsigned
            : WasmOpcodes.I32LessThanUnsigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.BranchIf, WasmInstructionOperand.Unsigned(0)));
        ObjectAllocationCall.EmitRun(code, addresses, runtimeImports, request.Target, layout);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)objectTemporary)));
        ObjectAllocationCall.EmitSlot(code, addresses, runtimeImports, request.Target, layout);
        addresses.Emit(code, AddressOperation.Add);
        ManagedMemoryEmitter.EmitStoreBySize(code, target, 0, target.ObjectReferenceSize);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)objectTemporary)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(layout.TypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Store, WasmInstructionOperand.Memory(2, 0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Branch, WasmInstructionOperand.Unsigned(1)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        roots.Emit(request, code);
        ObjectAllocationCall.EmitFallback(code, addresses, runtimeImports, request.Target, layout);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)objectTemporary)));
        EmitAllocationFailureCheck(code, objectTemporary, request.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitDelegate(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        MethodSignatureModel signature,
        CliTypeIdentity delegateType,
        int argumentBase,
        bool deferRoots)
    {
        if (signature.ParameterTypes.Length != 2 ||
            signature.ParameterTypes[0] != CliValueKind.ManagedReference ||
            signature.ParameterTypes[1] != CliValueKind.NativeInt)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedMetadata,
                "a delegate constructor must have the runtime (object, native int) shape",
                delegateType.CanonicalName,
                request.Instruction.Offset));
        }
        var target = GetStackLocal(request.Context, argumentBase, CliValueKind.ManagedReference);
        if (delegateTargets.Select(request))
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)target)));
            code.Write(WasmInstruction.NoOperand(layouts.Target.UsesMemory64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            exceptions.Emit(code, ManagedExceptionKind.Argument, request.Context.ManagedThrowFunction);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        EmitAllocation(request, code, typeLayouts.GetObjectLayout(delegateType), deferRoots);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)target)));
        ManagedMemoryEmitter.EmitStoreBySize(
            code,
            layouts.Target,
            objects.DelegateTargetOffset,
            layouts.Target.ObjectReferenceSize);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            argumentBase + 1,
            CliValueKind.NativeInt)))));
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Store, WasmInstructionOperand.Memory(2, (uint)(objects.DelegateMethodIdOffset))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            argumentBase,
            CliValueKind.ManagedReference)))));
        ReplaceArgumentsWithResult(
            request,
            code,
            argumentBase,
            signature.ParameterTypes.Length,
            CliValueKind.ManagedReference);
    }

    private void EmitValueType(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        MethodDefinitionModel constructor,
        MethodSignatureModel signature,
        CliTypeIdentity declaringType,
        int argumentBase,
        IFunctionIndexResolver functionIndices)
    {
        var value = values.GetValueLayout(declaringType);
        var offset = request.Context.ValueLayout.TemporaryOffsets[
            request.Instruction.Offset];
        EmitValueFrameAddress(code, request.Context, offset);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        if (!SmallBlockEmitter.TryEmitKnownSizeZero(code, request.Context.ObjectTemporary, value.Size))
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
            addresses.Emit(code, value.Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        EmitArguments(request, code, signature, argumentBase);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(GetConstructorIndex(request, constructor, functionIndices)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        // A vector is a v128 value, read back from the storage its constructor filled.
        var kind = declaringType.StackKind == CliValueKind.V128 ? CliValueKind.V128 : CliValueKind.ValueType;
        if (kind == CliValueKind.V128)
        {
            ManagedMemoryEmitter.EmitVectorLoad(code, 0, value.Size, scratch: request.Context.VectorScratch);
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            argumentBase,
            kind)))));
        ReplaceArgumentsWithResult(
            request,
            code,
            argumentBase,
            signature.ParameterTypes.Length,
            kind);
    }

    private void EmitReferenceType(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        MethodDefinitionModel constructor,
        MethodSignatureModel signature,
        CliTypeIdentity declaringType,
        int argumentBase,
        bool isConstructed,
        bool deferRoots,
        IFunctionIndexResolver functionIndices)
    {
        var layout = isConstructed
            ? typeLayouts.GetObjectLayout(declaringType)
            : typeLayouts.GetObjectLayout(constructor.DeclaringType);
        EmitAllocation(request, code, layout, deferRoots);
        roots.Emit(request, code, constructorCall: true);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        EmitArguments(request, code, signature, argumentBase);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(GetConstructorIndex(request, constructor, functionIndices)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            argumentBase,
            CliValueKind.ManagedReference)))));
        ReplaceArgumentsWithResult(
            request,
            code,
            argumentBase,
            signature.ParameterTypes.Length,
            CliValueKind.ManagedReference);
    }

    private void EmitArguments(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        MethodSignatureModel signature,
        int argumentBase)
    {
        for (var index = 0; index < signature.ParameterTypes.Length; index++)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                request.Context,
                argumentBase + index,
                request.Stack[argumentBase + index])))));
        }
    }

    private static int GetConstructorIndex(
        InstructionEmissionRequest request,
        MethodDefinitionModel constructor,
        IFunctionIndexResolver functionIndices)
    {
        if (request.Instruction.Operand is
            CilOperand.MethodInstance { Value.IsConstructed: true } instance)
        {
            return functionIndices.Resolve(instance.Value.CanonicalName);
        }

        return functionIndices.Resolve(constructor.Key);
    }

    private void EmitValueFrameAddress(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        int offset)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.ValueFrame))));
        if (offset == 0)
        {
            return;
        }
        addresses.Emit(code, offset);
        addresses.Emit(code, AddressOperation.Add);
    }

    private void EmitAllocationFailureCheck(
        IWasmInstructionWriter code,
        int objectLocal,
        OptionalFunctionIndex throwFunction)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);

    private static EntityKey GetEntity(CilInstruction instruction) =>
        instruction.Operand is CilOperand.Entity entity
            ? entity.Key
            : throw new InvalidOperationException(
                $"instruction {instruction.Operation} has no entity operand");

    private static void ReplaceArgumentsWithResult(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        int argumentBase,
        int argumentCount,
        CliValueKind result)
    {
        request.Stack.RemoveRange(argumentBase, argumentCount);
        request.Stack.Add(result);
    }
}
