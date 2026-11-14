using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.MemberExecution;

// A method case: the arguments' count is checked, then a constructor allocates and runs, a method runs on the
// receiver (a virtual one on the receiver's type), and the result becomes the call's.
internal sealed class MemberMethodCaseEmitter(
    IMemberDescriptorLayout descriptors,
    IMemberDescriptorCaseEmitter descriptorCases,
    IMemberArgumentCountCheckEmitter argumentCounts,
    IMemberArgumentEmitter arguments,
    IMemberReferenceCheckEmitter referenceChecks,
    IMemberObjectAllocator allocator,
    IBoxPayloadAddressEmitter payloads,
    IMemberResultBoxEmitter results,
    IMemberNullableResultEmitter nullableResults,
    IValueFrameAddressEmitter valueFrames,
    IStaticInitializationEmitter initialization,
    IMethodInitializationTriggerClassifier initializationTriggers,
    INullableTypeResolver nullableTypes,
    ITypeLayoutProvider types,
    IImplicitExceptionEmitter exceptions,
    IAddressInstructionEmitter addresses) : IMemberMethodCaseEmitter
{
    private readonly IMemberDescriptorLayout _descriptors = descriptors ??
        throw new ArgumentNullException(nameof(descriptors));
    private readonly IMemberDescriptorCaseEmitter _descriptorCases = descriptorCases ??
        throw new ArgumentNullException(nameof(descriptorCases));
    private readonly IMemberArgumentCountCheckEmitter _argumentCounts = argumentCounts ??
        throw new ArgumentNullException(nameof(argumentCounts));
    private readonly IMemberArgumentEmitter _arguments = arguments ??
        throw new ArgumentNullException(nameof(arguments));
    private readonly IMemberReferenceCheckEmitter _referenceChecks = referenceChecks ??
        throw new ArgumentNullException(nameof(referenceChecks));
    private readonly IMemberObjectAllocator _allocator = allocator ??
        throw new ArgumentNullException(nameof(allocator));
    private readonly IBoxPayloadAddressEmitter _payloads = payloads ??
        throw new ArgumentNullException(nameof(payloads));
    private readonly IMemberResultBoxEmitter _results = results ??
        throw new ArgumentNullException(nameof(results));
    private readonly IMemberNullableResultEmitter _nullableResults = nullableResults ??
        throw new ArgumentNullException(nameof(nullableResults));
    private readonly IValueFrameAddressEmitter _valueFrames = valueFrames ??
        throw new ArgumentNullException(nameof(valueFrames));
    private readonly IStaticInitializationEmitter _initialization = initialization ??
        throw new ArgumentNullException(nameof(initialization));
    private readonly IMethodInitializationTriggerClassifier _initializationTriggers = initializationTriggers ??
        throw new ArgumentNullException(nameof(initializationTriggers));
    private readonly INullableTypeResolver _nullableTypes = nullableTypes ??
        throw new ArgumentNullException(nameof(nullableTypes));
    private readonly ITypeLayoutProvider _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly IImplicitExceptionEmitter _exceptions = exceptions ??
        throw new ArgumentNullException(nameof(exceptions));
    private readonly IAddressInstructionEmitter _addresses = addresses ??
        throw new ArgumentNullException(nameof(addresses));

    public int EmitCases(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, MemberExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(plan);
        int descriptorLocal = request.Local(0, CliValueKind.ManagedReference);
        int receiverLocal = request.Local(1, CliValueKind.ManagedReference);
        int argumentsLocal = request.Local(2, CliValueKind.ManagedReference);
        var cases = plan.Methods.Values
            .OrderBy(entry => entry.Descriptor.CanonicalName, StringComparer.Ordinal)
            .ToArray();
        foreach (var execution in cases)
        {
            MethodInstanceModel method = execution.Descriptor;
            _descriptorCases.Open(
                code,
                descriptorLocal,
                _descriptors.GetDescriptorAddress(method));
            _argumentCounts.Check(
                code,
                argumentsLocal,
                method.Signature.ParameterSignatureTypes.Length, request.Instruction.Context.ManagedThrowFunction);
            if (IsConstructor(method))
            {
                EmitConstructorCall(request, code, argumentsLocal, method);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
                continue;
            }
            if (!method.Definition.IsStatic)
            {
                // A value type's method runs on the box's contents, so the box must hold the method's type.
                _referenceChecks.Check(code, receiverLocal,
                    method.DeclaringType.IsValueType ? method.DeclaringType : null,
                    request.Instruction.Context.ManagedThrowFunction);
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

    private static bool IsConstructor(MethodInstanceModel method) =>
        method.Definition.Name == ".ctor" && !method.Definition.IsStatic;

    // New: the object, or a struct's box, is allocated and rooted, then the constructor runs on it.
    private void EmitConstructorCall(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int argumentsLocal,
        MethodInstanceModel constructor)
    {
        if (_initializationTriggers.Classify(constructor.Definition))
        {
            _initialization.Emit(new(constructor.Definition.DeclaringType,
                constructor.DeclaringType, request.Instruction.Target.StaticInitializerGuards,
                IsMethodInvocation: true), code, request.FunctionIndices);
        }
        int resultLocal = request.Local(0, CliValueKind.ManagedReference);
        _allocator.Allocate(request, code, constructor.DeclaringType, resultLocal, rooted: true);
        if (constructor.DeclaringType.IsValueType)
        {
            _payloads.Emit(code, resultLocal, constructor.DeclaringType);
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)resultLocal)));
        }
        _arguments.Emit(
            request,
            code,
            argumentsLocal,
            constructor.Signature.ParameterSignatureTypes);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)request.FunctionIndices.Resolve(constructor))));
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
        _exceptions.Emit(code, ManagedExceptionKind.InvalidCast, request.Instruction.Context.ManagedThrowFunction);
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
        if (_initializationTriggers.Classify(method.Definition))
        {
            _initialization.Emit(new(method.Definition.DeclaringType,
                method.DeclaringType, request.Instruction.Target.StaticInitializerGuards,
                IsMethodInvocation: true), code, request.FunctionIndices);
        }
        var underlying = _nullableTypes.Resolve(resultType);
        // A struct result goes to a box allocated (and rooted) before the call, which the callee writes.
        var boxedResult = underlying is null && resultType.StackKind == CliValueKind.ValueType;
        int resultLocal = request.Local(0, CliValueKind.ManagedReference);
        if (underlying is not null)
        {
            EmitMemberResultAddress(request, code);
        }
        else if (boxedResult)
        {
            _allocator.Allocate(request, code, resultType, resultLocal, rooted: true);
            _payloads.Emit(code, resultLocal, resultType);
        }
        if (!method.Definition.IsStatic)
        {
            var owner = receiverType ?? method.DeclaringType;
            if (owner.IsValueType && method.DeclaringType.IsValueType)
            {
                _payloads.Emit(code, receiverLocal, owner);
            }
            else
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)receiverLocal)));
            }
        }
        _arguments.Emit(
            request,
            code,
            argumentsLocal,
            method.Signature.ParameterSignatureTypes);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)request.FunctionIndices.Resolve(method))));
        if (underlying is not null)
        {
            EmitMemberResultAddress(request, code);
            _nullableResults.Emit(request, code, underlying);
            return;
        }
        if (boxedResult)
        {
            return;
        }
        if (resultType.StackKind == CliValueKind.Void)
        {
            _addresses.Emit(code, 0);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)resultLocal)));
            return;
        }
        _results.Emit(request, code, resultType);
    }

    private void EmitMemberResultAddress(RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code) =>
        _valueFrames.Emit(code, request.Instruction.Context,
            request.Instruction.Context.ValueLayout.MemberResultOffsets[
                request.Instruction.Instruction.Offset]);
}
