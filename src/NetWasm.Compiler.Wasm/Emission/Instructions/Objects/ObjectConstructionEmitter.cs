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
    IStringConstructionEmitter strings) :
    InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new InstructionCommand(
            CilOperation.NewObject,
            InstructionFamily.AllocationBoxingTypes,
            EmitNewObject),
    ];

    private void EmitNewObject(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        roots.Emit(request, code);
        var constructorInstance = request.Instruction.Operand as CilOperand.MethodInstance;
        var constructor = constructorInstance?.Value.Definition ??
                          methods.GetMethod(GetEntity(request.Instruction));
        var signature = constructorInstance?.Value.Signature ?? constructor.Signature;
        var declaringType = constructorInstance?.Value.DeclaringType ??
                            types.Resolve(constructor.DeclaringType);
        var argumentBase = request.Stack.Count - signature.ParameterTypes.Length;
        if (symbols.Format(constructor.DeclaringType) == "System.String")
        {
            strings.Emit(request, code, signature, argumentBase);
            return;
        }
        if (typeClassifier.IsDelegateType(constructor.DeclaringType))
        {
            EmitDelegate(request, code, signature, declaringType, argumentBase);
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
            functionIndices);
    }

    private void EmitDelegate(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        MethodSignatureModel signature,
        CliTypeIdentity delegateType,
        int argumentBase)
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
        var layout = typeLayouts.GetObjectLayout(delegateType);
        addresses.Emit(code, layout.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(layout.TypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.Allocate)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        EmitAllocationFailureCheck(code, request.Context.ObjectTemporary);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            argumentBase,
            CliValueKind.ManagedReference)))));
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
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        addresses.Emit(code, value.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        EmitArguments(request, code, signature, argumentBase);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(GetConstructorIndex(request, constructor, functionIndices)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            argumentBase,
            CliValueKind.ValueType)))));
        ReplaceArgumentsWithResult(
            request,
            code,
            argumentBase,
            signature.ParameterTypes.Length,
            CliValueKind.ValueType);
    }

    private void EmitReferenceType(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        MethodDefinitionModel constructor,
        MethodSignatureModel signature,
        CliTypeIdentity declaringType,
        int argumentBase,
        bool isConstructed,
        IFunctionIndexResolver functionIndices)
    {
        var layout = isConstructed
            ? typeLayouts.GetObjectLayout(declaringType)
            : typeLayouts.GetObjectLayout(constructor.DeclaringType);
        roots.Emit(request, code);
        addresses.Emit(code, layout.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(layout.TypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.Allocate)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        EmitAllocationFailureCheck(code, request.Context.ObjectTemporary);
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

    private void EmitAllocationFailureCheck(IWasmInstructionWriter code, int objectLocal)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory);
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
