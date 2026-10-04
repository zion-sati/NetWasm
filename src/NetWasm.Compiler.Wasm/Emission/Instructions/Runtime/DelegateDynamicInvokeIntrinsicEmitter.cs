using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class DelegateDynamicInvokeIntrinsicEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    IMemberDescriptorLayout descriptors,
    ITypeLayoutProvider types,
    IValueLayoutProvider values,
    IRuntimeObjectLayout objects,
    IBoxedValueTypeValidator valueTypes,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IRootPublicationEmitter roots,
    IExceptionPayloadBlockEmitter exceptionPayloadBlocks,
    INullableTypeResolver nullableTypes,
    INullableBoxEmitter nullableBoxes,
    IValueFrameAddressEmitter valueFrames) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ValidateSignature(request);
        var plan = request.Instruction.Target.MemberExecution.DelegateInvocation ??
            throw RuntimeContract("DynamicInvoke has no compiler plan");
        ValidatePlan(plan);
        if (plan.Methods.Values.Any(method =>
                nullableTypes.Resolve(method.Signature.ReturnSignatureType) is not null) &&
            !request.Instruction.Context.ValueLayout.MemberResultOffsets.ContainsKey(
                request.Instruction.Instruction.Offset))
        {
            throw RuntimeContract("DynamicInvoke nullable results require planned return storage");
        }

        roots.Emit(request.Instruction, code);
        int descriptorLocal = request.Local(0, CliValueKind.ManagedReference);
        int receiverLocal = request.Local(1, CliValueKind.ManagedReference);
        int argumentsLocal = request.Local(2, CliValueKind.ManagedReference);
        int caseCount = 0;
        foreach (var method in plan.Methods.Values.OrderBy(
                     method => method.CanonicalName,
                     StringComparer.Ordinal))
        {
            caseCount++;
            EmitDescriptorCase(code, descriptorLocal, method);
            EmitArgumentArityCheck(
                request,
                code,
                argumentsLocal,
                method.Signature.ParameterSignatureTypes.Length,
                plan.ParameterCountTarget);
            PrepareArguments(request, code, argumentsLocal, method, plan.ArgumentTarget);
            PrepareResult(request, code, method.Signature.ReturnSignatureType);
            roots.Emit(request.Instruction, code);
            EmitWrappedInvocation(
                request,
                code,
                receiverLocal,
                argumentsLocal,
                method,
                plan.InvocationTarget);
            EmitPostInvocationResult(request, code, method.Signature.ReturnSignatureType);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        }

        EmitThrow(request, code, plan.UnsupportedTarget);
        for (var index = 0; index < caseCount; index++)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private void PrepareArguments(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int argumentsLocal,
        MethodInstanceModel method,
        MethodInstanceModel argumentTarget)
    {
        for (var index = 0; index < method.Signature.ParameterSignatureTypes.Length; index++)
        {
            var signatureType = method.Signature.ParameterSignatureTypes[index];
            var parameter = signatureType.StackKind == CliValueKind.ManagedAddress
                ? signatureType.ElementType!
                : signatureType;
            EmitArgumentReference(code, argumentsLocal, index);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned(
                    (uint)request.Instruction.Context.ObjectTemporary)));
            if (parameter.StackKind == CliValueKind.ManagedReference)
            {
                EmitReferenceValidation(
                    request,
                    code,
                    parameter,
                    argumentTarget);
                continue;
            }

            EmitDefaultBox(request, code, argumentsLocal, index, parameter);
            if (signatureType.StackKind == CliValueKind.ManagedAddress)
            {
                EmitExactValueValidation(request, code, parameter, argumentTarget);
            }
            else
            {
                valueTypes.Validate(
                    code,
                    request.Instruction.Context.ObjectTemporary,
                    parameter,
                    ManagedExceptionKind.Argument);
            }
        }
    }

    private void PrepareResult(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity resultType)
    {
        if (resultType.StackKind is CliValueKind.Void or CliValueKind.ManagedReference ||
            nullableTypes.Resolve(resultType) is not null)
        {
            return;
        }
        AllocateBox(
            code,
            resultType,
            request.Local(0, CliValueKind.ManagedReference));
    }

    private void EmitWrappedInvocation(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int receiverLocal,
        int argumentsLocal,
        MethodInstanceModel method,
        MethodInstanceModel invocationTarget)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptionPayloadBlocks.Emit(code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.TryTable,
            WasmInstructionOperand.TryTableCatch(
                WasmOpcodes.EmptyBlockType,
                0,
                0)));
        EmitTypedInvocation(request, code, receiverLocal, argumentsLocal, method);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(1)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)request.FunctionIndices.Resolve(invocationTarget))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(
                (uint)request.Local(0, CliValueKind.ManagedReference))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitTypedInvocation(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int receiverLocal,
        int argumentsLocal,
        MethodInstanceModel method)
    {
        var resultType = method.Signature.ReturnSignatureType;
        if (resultType.StackKind == CliValueKind.ValueType)
        {
            if (nullableTypes.Resolve(resultType) is not null)
            {
                EmitMemberResultAddress(request, code);
            }
            else
            {
                EmitBoxPayloadAddress(
                    code,
                    request.Local(0, CliValueKind.ManagedReference),
                    resultType);
            }
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)receiverLocal)));
        for (var index = 0; index < method.Signature.ParameterSignatureTypes.Length; index++)
        {
            EmitCallArgument(
                request,
                code,
                argumentsLocal,
                index,
                method.Signature.ParameterSignatureTypes[index]);
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)request.Instruction.Target.FunctionIndices
                    .GetDelegateInvokeHelper(method.DeclaringType.CanonicalName).Value)));
        PreserveInvocationResult(request, code, resultType);
    }

    private void EmitCallArgument(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int argumentsLocal,
        int index,
        CliTypeIdentity signatureType)
    {
        if (signatureType.StackKind == CliValueKind.ManagedAddress)
        {
            var element = signatureType.ElementType!;
            if (element.StackKind == CliValueKind.ManagedReference)
            {
                EmitArgumentSlotAddress(code, argumentsLocal, index);
            }
            else
            {
                EmitArgumentReference(code, argumentsLocal, index);
                EmitBoxPayloadAddress(code, element);
            }
            return;
        }

        EmitArgumentReference(code, argumentsLocal, index);
        if (signatureType.StackKind == CliValueKind.ManagedReference)
        {
            return;
        }
        EmitBoxPayloadAddress(code, signatureType);
        if (signatureType.StackKind != CliValueKind.ValueType)
        {
            var layout = values.GetValueLayout(signatureType);
            ManagedMemoryEmitter.EmitLoadByType(
                code,
                layouts.Target,
                0,
                signatureType,
                layout.Size);
        }
    }

    private void PreserveInvocationResult(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity resultType)
    {
        int resultLocal = request.Local(0, CliValueKind.ManagedReference);
        if (resultType.StackKind == CliValueKind.Void)
        {
            return;
        }
        if (resultType.StackKind == CliValueKind.ManagedReference)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)resultLocal)));
            return;
        }
        if (resultType.StackKind == CliValueKind.ValueType)
        {
            return;
        }

        int valueLocal = NumericTemporary(request, resultType.StackKind);
        PreserveFloatingValue(code, resultType.StackKind);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)valueLocal)));
        EmitBoxPayloadAddress(code, resultLocal, resultType);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)valueLocal)));
        RestoreFloatingValue(code, resultType.StackKind);
        var layout = values.GetValueLayout(resultType);
        ManagedMemoryEmitter.EmitStoreByType(
            code,
            layouts.Target,
            0,
            resultType,
            layout.Size);
    }

    private void EmitPostInvocationResult(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity resultType)
    {
        int resultLocal = request.Local(0, CliValueKind.ManagedReference);
        if (resultType.StackKind == CliValueKind.Void)
        {
            addresses.Emit(code, 0);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)resultLocal)));
            return;
        }
        var underlying = nullableTypes.Resolve(resultType);
        if (underlying is null)
        {
            return;
        }
        EmitMemberResultAddress(request, code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(
                (uint)request.Instruction.Context.ObjectTemporary)));
        nullableBoxes.Emit(
            code,
            underlying,
            request.Instruction.Context.ObjectTemporary,
            resultLocal);
    }

    private void EmitReferenceValidation(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity targetType,
        MethodInstanceModel argumentTarget)
    {
        int objectLocal = request.Instruction.Context.ObjectTemporary;
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)objectLocal)));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)objectLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(types.GetObjectLayout(targetType).TypeId)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)runtimeImports.Resolve(RuntimeImportSymbol.IsAssignable))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitThrow(request, code, argumentTarget);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitExactValueValidation(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity targetType,
        MethodInstanceModel argumentTarget)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(
                (uint)request.Instruction.Context.ObjectTemporary)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(types.GetObjectLayout(targetType).TypeId)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitThrow(request, code, argumentTarget);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitDefaultBox(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int argumentsLocal,
        int index,
        CliTypeIdentity parameter)
    {
        int objectLocal = request.Instruction.Context.ObjectTemporary;
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)objectLocal)));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        AllocateBox(code, parameter, objectLocal);
        EmitArgumentSlotAddress(code, argumentsLocal, index);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)objectLocal)));
        ManagedMemoryEmitter.EmitStoreBySize(
            code,
            layouts.Target,
            0,
            layouts.Target.ObjectReferenceSize);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void AllocateBox(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        int destinationLocal)
    {
        var layout = types.GetObjectLayout(type);
        addresses.Emit(code, layout.Size);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(layout.TypeId)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)runtimeImports.Resolve(RuntimeImportSymbol.Allocate))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)destinationLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)destinationLocal)));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitArgumentArityCheck(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int argumentsLocal,
        int expectedCount,
        MethodInstanceModel target)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)argumentsLocal)));
        addresses.Emit(code, AddressOperation.EqualZero);
        if (expectedCount == 0)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            EmitArgumentCountMismatch(request, code, argumentsLocal, expectedCount, target);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            return;
        }

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitThrow(request, code, target);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        EmitArgumentCountMismatch(request, code, argumentsLocal, expectedCount, target);
    }

    private void EmitArgumentCountMismatch(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int argumentsLocal,
        int expectedCount,
        MethodInstanceModel target)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)argumentsLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)objects.ArrayLengthOffset)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(expectedCount)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitThrow(request, code, target);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitDescriptorCase(
        IWasmInstructionWriter code,
        int descriptorLocal,
        MethodInstanceModel method)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)descriptorLocal)));
        addresses.Emit(code, descriptors.GetDescriptorAddress(method));
        addresses.Emit(code, AddressOperation.Equal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
    }

    private void EmitArgumentReference(
        IWasmInstructionWriter code,
        int argumentsLocal,
        int index)
    {
        EmitArgumentSlotAddress(code, argumentsLocal, index);
        ManagedMemoryEmitter.EmitReferenceLoad(code, layouts.Target, 0);
    }

    private void EmitArgumentSlotAddress(
        IWasmInstructionWriter code,
        int argumentsLocal,
        int index)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)argumentsLocal)));
        ManagedMemoryEmitter.EmitReferenceLoad(
            code,
            layouts.Target,
            objects.ArrayDataPointerOffset);
        if (index == 0)
        {
            return;
        }
        addresses.Emit(code, checked(index * layouts.Target.ObjectReferenceSize));
        addresses.Emit(code, AddressOperation.Add);
    }

    private void EmitBoxPayloadAddress(
        IWasmInstructionWriter code,
        CliTypeIdentity type)
    {
        var layout = values.GetValueLayout(type);
        addresses.Emit(
            code,
            WasmTargetLayout.Align(layouts.Target.ObjectHeaderSize, layout.Alignment));
        addresses.Emit(code, AddressOperation.Add);
    }

    private void EmitBoxPayloadAddress(
        IWasmInstructionWriter code,
        int objectLocal,
        CliTypeIdentity type)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)objectLocal)));
        EmitBoxPayloadAddress(code, type);
    }

    private void EmitMemberResultAddress(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code) =>
        valueFrames.Emit(
            code,
            request.Instruction.Context,
            request.Instruction.Context.ValueLayout.MemberResultOffsets[
                request.Instruction.Instruction.Offset]);

    private static void EmitThrow(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        MethodInstanceModel target)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)request.FunctionIndices.Resolve(target))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(
                (uint)request.Local(0, CliValueKind.ManagedReference))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
    }

    private int NumericTemporary(
        RuntimeIntrinsicEmissionRequest request,
        CliValueKind kind) =>
        kind switch
        {
            CliValueKind.I4 or CliValueKind.F4 =>
                request.Instruction.Context.NumericTemporaryI4,
            CliValueKind.NativeInt when !layouts.Target.UsesMemory64 =>
                request.Instruction.Context.NumericTemporaryI4,
            _ => request.Instruction.Context.NumericTemporaryI8,
        };

    private static void PreserveFloatingValue(
        IWasmInstructionWriter code,
        CliValueKind kind)
    {
        if (kind == CliValueKind.F4)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32ReinterpretF32));
        }
        else if (kind == CliValueKind.F8)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ReinterpretF64));
        }
    }

    private static void RestoreFloatingValue(
        IWasmInstructionWriter code,
        CliValueKind kind)
    {
        if (kind == CliValueKind.F4)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32ReinterpretI32));
        }
        else if (kind == CliValueKind.F8)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64ReinterpretI64));
        }
    }

    private static void ValidateSignature(RuntimeIntrinsicEmissionRequest request)
    {
        if (!request.Method.Signature.ParameterTypes.AsSpan().SequenceEqual(
                [
                    CliValueKind.ManagedReference,
                    CliValueKind.ManagedReference,
                    CliValueKind.ManagedReference,
                ]) ||
            request.Method.Signature.ReturnType != CliValueKind.ManagedReference)
        {
            throw RuntimeContract("DynamicInvoke contradicts its runtime signature");
        }
    }

    private void ValidatePlan(DelegateDynamicInvokePlan plan)
    {
        foreach (var pair in plan.Methods)
        {
            var method = pair.Value;
            if (!StringComparer.Ordinal.Equals(pair.Key, method.CanonicalName) ||
                method.Definition.Name != "Invoke" ||
                method.Definition.IsStatic ||
                method.DeclaringType.ContainsGenericParameters)
            {
                throw RuntimeContract("DynamicInvoke contains contradictory delegate facts");
            }
            ValidateResult(method.Signature.ReturnSignatureType);
            foreach (var parameter in method.Signature.ParameterSignatureTypes)
            {
                ValidateParameter(parameter);
            }
        }
        ValidateSupport(plan.UnsupportedTarget, 0);
        ValidateSupport(plan.ArgumentTarget, 0);
        ValidateSupport(plan.ParameterCountTarget, 0);
        ValidateSupport(plan.InvocationTarget, 1);
    }

    private void ValidateResult(CliTypeIdentity type)
    {
        if (type.StackKind == CliValueKind.Void)
        {
            return;
        }
        ValidateValue(type, allowNullable: true);
    }

    private void ValidateParameter(CliTypeIdentity type)
    {
        if (type.StackKind != CliValueKind.ManagedAddress)
        {
            ValidateValue(type, allowNullable: false);
            return;
        }
        ValidateValue(
            type.ElementType ?? throw RuntimeContract(
                "DynamicInvoke by-reference parameter has no element type"),
            allowNullable: false);
    }

    private void ValidateValue(CliTypeIdentity type, bool allowNullable)
    {
        if (type.ContainsGenericParameters ||
            type.StackKind is not (
                CliValueKind.ManagedReference or
                CliValueKind.I4 or CliValueKind.I8 or
                CliValueKind.F4 or CliValueKind.F8 or
                CliValueKind.NativeInt or CliValueKind.ValueType) ||
            !allowNullable && nullableTypes.Resolve(type) is not null)
        {
            throw RuntimeContract(
                $"DynamicInvoke cannot preserve '{type.CanonicalName}'");
        }
    }

    private static void ValidateSupport(MethodInstanceModel method, int parameters)
    {
        if (!method.Definition.IsStatic ||
            method.Signature.ParameterSignatureTypes.Length != parameters ||
            method.Signature.ReturnType != CliValueKind.ManagedReference)
        {
            throw RuntimeContract("DynamicInvoke support target has an invalid signature");
        }
    }

    private static CompilerException RuntimeContract(string message) => new(
        new CompilerDiagnostic(DiagnosticCode.RuntimeContract, message));
}
