using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class MemberExecutionIntrinsicEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    IMemberDescriptorLayout descriptors,
    IInstanceFieldLayoutProvider fields,
    ITypeLayoutProvider types,
    IValueLayoutProvider values,
    IRuntimeObjectLayout objects,
    IBoxedValueTypeValidator typeValidator,
    IStaticInitializationEmitter initialization,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IRootPublicationEmitter roots) : IRuntimeIntrinsicEmitter
{
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly IAddressInstructionEmitter _addresses = addresses ??
        throw new ArgumentNullException(nameof(addresses));
    private readonly IMemberDescriptorLayout _descriptors = descriptors ??
        throw new ArgumentNullException(nameof(descriptors));
    private readonly IInstanceFieldLayoutProvider _fields = fields ??
        throw new ArgumentNullException(nameof(fields));
    private readonly ITypeLayoutProvider _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly IValueLayoutProvider _values = values ??
        throw new ArgumentNullException(nameof(values));
    private readonly IRuntimeObjectLayout _objects = objects ??
        throw new ArgumentNullException(nameof(objects));
    private readonly IBoxedValueTypeValidator _typeValidator = typeValidator ??
        throw new ArgumentNullException(nameof(typeValidator));
    private readonly IStaticInitializationEmitter _initialization = initialization ??
        throw new ArgumentNullException(nameof(initialization));
    private readonly IRuntimeImportResolver _runtimeImports = runtimeImports ??
        throw new ArgumentNullException(nameof(runtimeImports));
    private readonly IImplicitExceptionEmitter _exceptions = exceptions ??
        throw new ArgumentNullException(nameof(exceptions));
    private readonly IRootPublicationEmitter _roots = roots ??
        throw new ArgumentNullException(nameof(roots));

    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ValidateSignature(request);
        MemberExecutionPlan plan = request.Instruction.Target.MemberExecution;
        if (plan.UnsupportedTarget is null)
        {
            throw RuntimeContract("bounded member execution has no compiler plan");
        }
        ValidatePlan(
            request.Intrinsic == RuntimeIntrinsic.MemberExecuteMethod,
            plan);

        _roots.Emit(request.Instruction, code);
        int descriptorLocal = request.Local(0, CliValueKind.ManagedReference);
        int receiverLocal = request.Local(1, CliValueKind.ManagedReference);
        int caseCount;
        if (request.Intrinsic == RuntimeIntrinsic.MemberExecuteMethod)
        {
            int argumentsLocal = request.Local(2, CliValueKind.ManagedReference);
            caseCount = EmitMethodCases(
                request,
                code,
                descriptorLocal,
                receiverLocal,
                argumentsLocal,
                plan);
        }
        else
        {
            caseCount = EmitFieldCases(
                request,
                code,
                descriptorLocal,
                receiverLocal,
                plan);
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)request.FunctionIndices.Resolve(plan.UnsupportedTarget))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(
                (uint)request.Local(0, CliValueKind.ManagedReference))));
        for (var index = 0; index < caseCount; index++)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private int EmitMethodCases(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int descriptorLocal,
        int receiverLocal,
        int argumentsLocal,
        MemberExecutionPlan plan)
    {
        var cases = plan.Methods.Values
            .OrderBy(entry => entry.Descriptor.CanonicalName, StringComparer.Ordinal)
            .ToArray();
        foreach (var execution in cases)
        {
            MethodInstanceModel method = execution.Descriptor;
            EmitDescriptorCase(
                code,
                descriptorLocal,
                _descriptors.GetMethodDescriptorAddress(method));
            EmitArgumentArityCheck(
                code,
                argumentsLocal,
                method.Signature.ParameterSignatureTypes.Length);
            if (!method.Definition.IsStatic)
            {
                EmitNullCheck(code, receiverLocal);
            }
            else
            {
                _initialization.Emit(
                    new(
                        method.Definition.DeclaringType,
                        method.DeclaringType,
                        request.Instruction.Target.ModuleData,
                        IsStaticMethodCall: true),
                    code,
                    request.FunctionIndices);
            }
            if (!execution.RequiresDispatch)
            {
                EmitMethodCall(
                    request,
                    code,
                    receiverLocal,
                    argumentsLocal,
                    method,
                    method.Signature.ReturnSignatureType);
            }
            else
            {
                EmitVirtualMethodCall(
                    request,
                    code,
                    receiverLocal,
                    argumentsLocal,
                    execution,
                    method.Signature.ReturnSignatureType);
            }
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        }
        return cases.Length;
    }

    private int EmitFieldCases(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int descriptorLocal,
        int receiverLocal,
        MemberExecutionPlan plan)
    {
        var cases = plan.Fields.Values
            .OrderBy(field => field.CanonicalName, StringComparer.Ordinal)
            .ToArray();
        foreach (var field in cases)
        {
            EmitDescriptorCase(
                code,
                descriptorLocal,
                _descriptors.GetFieldDescriptorAddress(field));
            EmitNullCheck(code, receiverLocal);
            var layout = _fields.GetFieldLayout(field);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)receiverLocal)));
            ManagedMemoryEmitter.EmitLoadByType(
                code,
                _layouts.Target,
                layout.Offset,
                field.FieldType,
                layout.Size);
            int valueLocal = PreserveResult(
                request,
                code,
                field.FieldType.StackKind);
            EmitObjectResult(request, code, field.FieldType, valueLocal);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        }
        return cases.Length;
    }

    private void EmitVirtualMethodCall(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int receiverLocal,
        int argumentsLocal,
        MemberMethodExecutionPlan execution,
        CliTypeIdentity resultType)
    {
        foreach (var target in execution.Targets)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)receiverLocal)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(2, 0)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(
                    _types.GetObjectLayout(target.ReceiverType).TypeId)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            EmitMethodCall(
                request,
                code,
                receiverLocal,
                argumentsLocal,
                target.Method,
                resultType,
                target.ReceiverType);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        }
        _exceptions.Emit(code, ManagedExceptionKind.InvalidCast);
        for (var index = 0; index < execution.Targets.Length; index++)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private void EmitMethodCall(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int receiverLocal,
        int argumentsLocal,
        MethodInstanceModel method,
        CliTypeIdentity resultType,
        CliTypeIdentity? receiverType = null)
    {
        if (!method.Definition.IsStatic)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)receiverLocal)));
            if (receiverType is { IsValueType: true } &&
                method.DeclaringType.IsValueType)
            {
                var receiverLayout = _values.GetValueLayout(receiverType);
                int payloadOffset = WasmTargetLayout.Align(
                    _layouts.Target.ObjectHeaderSize,
                    receiverLayout.Alignment);
                _addresses.Emit(code, payloadOffset);
                _addresses.Emit(code, AddressOperation.Add);
            }
        }
        EmitArguments(
            request,
            code,
            argumentsLocal,
            method.Signature.ParameterSignatureTypes);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)request.FunctionIndices.Resolve(method))));
        int valueLocal = PreserveResult(request, code, resultType.StackKind);
        EmitObjectResult(request, code, resultType, valueLocal);
    }

    private void EmitArguments(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int argumentsLocal,
        IReadOnlyList<CliTypeIdentity> parameters)
    {
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            EmitArgumentReference(code, argumentsLocal, index);
            if (parameter.StackKind == CliValueKind.ManagedReference)
            {
                continue;
            }

            int argumentLocal = request.Instruction.Context.ObjectTemporary;
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)argumentLocal)));
            EmitNullCheck(code, argumentLocal);
            _typeValidator.Validate(code, argumentLocal, parameter);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)argumentLocal)));
            var valueLayout = _values.GetValueLayout(parameter);
            int payloadOffset = WasmTargetLayout.Align(
                _layouts.Target.ObjectHeaderSize,
                valueLayout.Alignment);
            _addresses.Emit(code, payloadOffset);
            _addresses.Emit(code, AddressOperation.Add);
            ManagedMemoryEmitter.EmitLoadByType(
                code,
                _layouts.Target,
                0,
                parameter,
                valueLayout.Size);
        }
    }

    private void EmitArgumentReference(
        IWasmInstructionWriter code,
        int argumentsLocal,
        int index)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)argumentsLocal)));
        ManagedMemoryEmitter.EmitReferenceLoad(
            code,
            _layouts.Target,
            _objects.ArrayDataPointerOffset);
        if (index != 0)
        {
            _addresses.Emit(code, checked(index * _layouts.Target.ObjectReferenceSize));
            _addresses.Emit(code, AddressOperation.Add);
        }
        ManagedMemoryEmitter.EmitReferenceLoad(code, _layouts.Target, 0);
    }

    private void EmitArgumentArityCheck(
        IWasmInstructionWriter code,
        int argumentsLocal,
        int expectedCount)
    {
        if (expectedCount == 0)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)argumentsLocal)));
            _addresses.Emit(code, AddressOperation.EqualZero);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            EmitArgumentCountMismatch(
                code,
                argumentsLocal,
                expectedCount);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            return;
        }

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)argumentsLocal)));
        _addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitInvalidOperationAndUnreachable(code);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        EmitArgumentCountMismatch(
            code,
            argumentsLocal,
            expectedCount);
    }

    private void EmitArgumentCountMismatch(
        IWasmInstructionWriter code,
        int argumentsLocal,
        int expectedCount)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)argumentsLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)_objects.ArrayLengthOffset)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(expectedCount)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitInvalidOperationAndUnreachable(code);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitInvalidOperationAndUnreachable(
        IWasmInstructionWriter code)
    {
        _exceptions.Emit(code, ManagedExceptionKind.InvalidOperation);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
    }

    private void EmitObjectResult(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity resultType,
        int valueLocal)
    {
        int resultLocal = request.Local(0, CliValueKind.ManagedReference);
        if (resultType.StackKind == CliValueKind.ManagedReference)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)valueLocal)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)resultLocal)));
            return;
        }

        var valueLayout = _values.GetValueLayout(resultType);
        var objectLayout = _types.GetObjectLayout(resultType);
        int payloadOffset = WasmTargetLayout.Align(
            _layouts.Target.ObjectHeaderSize,
            valueLayout.Alignment);
        _addresses.Emit(code, objectLayout.Size);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(objectLayout.TypeId)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)_runtimeImports.Resolve(RuntimeImportSymbol.Allocate))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)resultLocal)));
        EmitAllocationFailureCheck(code, resultLocal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)resultLocal)));
        _addresses.Emit(code, payloadOffset);
        _addresses.Emit(code, AddressOperation.Add);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)valueLocal)));
        RestoreFloatingResult(code, resultType.StackKind);
        ManagedMemoryEmitter.EmitStoreByType(
            code,
            _layouts.Target,
            0,
            resultType,
            valueLayout.Size);
    }

    private static int PreserveResult(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        CliValueKind kind)
    {
        int local;
        if (kind == CliValueKind.ManagedReference)
        {
            local = request.Local(0, kind);
        }
        else if (kind is CliValueKind.I4 or CliValueKind.F4)
        {
            local = request.Instruction.Context.NumericTemporaryI4;
        }
        else
        {
            local = request.Instruction.Context.NumericTemporaryI8;
        }
        if (kind == CliValueKind.F4)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32ReinterpretF32));
        }
        else if (kind == CliValueKind.F8)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ReinterpretF64));
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));
        return local;
    }

    private static void RestoreFloatingResult(
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

    private void EmitDescriptorCase(
        IWasmInstructionWriter code,
        int descriptorLocal,
        int descriptorAddress)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)descriptorLocal)));
        _addresses.Emit(code, descriptorAddress);
        _addresses.Emit(code, AddressOperation.Equal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
    }

    private void EmitNullCheck(IWasmInstructionWriter code, int receiverLocal)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)receiverLocal)));
        _addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        _exceptions.Emit(code, ManagedExceptionKind.NullReference);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitAllocationFailureCheck(
        IWasmInstructionWriter code,
        int resultLocal)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)resultLocal)));
        _addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        _exceptions.Emit(code, ManagedExceptionKind.OutOfMemory);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static void ValidateSignature(RuntimeIntrinsicEmissionRequest request)
    {
        ReadOnlySpan<CliValueKind> expectedParameters = request.Intrinsic switch
        {
            RuntimeIntrinsic.MemberExecuteMethod =>
            [
                CliValueKind.ManagedReference,
                CliValueKind.ManagedReference,
                CliValueKind.ManagedReference,
            ],
            RuntimeIntrinsic.MemberReadField =>
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            _ => throw RuntimeContract(
                $"runtime intrinsic '{request.Intrinsic}' is not bounded member execution"),
        };
        if (!request.Method.Signature.ParameterTypes.AsSpan().SequenceEqual(
                expectedParameters) ||
            request.Method.Signature.ReturnType != CliValueKind.ManagedReference)
        {
            throw RuntimeContract(
                "bounded member execution contradicts its runtime signature");
        }
    }

    private static void ValidatePlan(
        bool executeMethod,
        MemberExecutionPlan plan)
    {
        if (executeMethod)
        {
            foreach (var pair in plan.Methods)
            {
                var execution = pair.Value;
                var method = execution.Descriptor;
                if (!StringComparer.Ordinal.Equals(pair.Key, method.CanonicalName) ||
                    execution.RequiresDispatch != method.Definition.IsVirtual ||
                    method.Definition.IsStatic && execution.RequiresDispatch ||
                    !execution.RequiresDispatch && !execution.Targets.IsEmpty ||
                    !method.Definition.IsStatic && method.DeclaringType.IsValueType)
                {
                    throw RuntimeContract(
                        "bounded method execution contains contradictory dispatch facts");
                }

                ValidateValueKind(method.Signature.ReturnType);
                foreach (var parameter in method.Signature.ParameterSignatureTypes)
                {
                    ValidateValueKind(parameter.StackKind);
                }
                foreach (var target in execution.Targets)
                {
                    if (target.Method.Definition.IsStatic ||
                        !DispatchResultMatches(
                            method.Signature.ReturnSignatureType,
                            target.Method.Signature.ReturnSignatureType) ||
                        !target.Method.Signature.ParameterSignatureTypes.SequenceEqual(
                            method.Signature.ParameterSignatureTypes))
                    {
                        throw RuntimeContract(
                            "bounded method execution contains a contradictory dispatch target");
                    }
                }
            }
            return;
        }
        foreach (var pair in plan.Fields)
        {
            if (!StringComparer.Ordinal.Equals(
                    pair.Key,
                    pair.Value.CanonicalName) ||
                pair.Value.Definition.IsStatic ||
                pair.Value.DeclaringType.IsValueType)
            {
                throw RuntimeContract(
                    "bounded field execution contains contradictory facts");
            }
            ValidateValueKind(pair.Value.FieldType.StackKind);
        }
    }

    private static void ValidateValueKind(CliValueKind kind)
    {
        if (kind is not (
                CliValueKind.ManagedReference or
                CliValueKind.I4 or CliValueKind.I8 or
                CliValueKind.F4 or CliValueKind.F8))
        {
            throw RuntimeContract(
                $"bounded member execution cannot preserve '{kind}'");
        }
    }

    private static bool DispatchResultMatches(
        CliTypeIdentity descriptor,
        CliTypeIdentity target) =>
        descriptor.StackKind == CliValueKind.ManagedReference
            ? target.StackKind == CliValueKind.ManagedReference
            : descriptor.Equals(target);

    private static CompilerException RuntimeContract(string message) => new(
        new CompilerDiagnostic(DiagnosticCode.RuntimeContract, message));
}
