// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.MemberExecution;

// A method case: the arguments' count is checked, then a constructor allocates and runs, a method runs on the
// receiver (a virtual one on the receiver's type), and the result becomes the call's.
internal sealed class MemberMethodCaseEmitter(
    IMemberDescriptorLayout descriptors,
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
    IVirtualDispatchCasePlanner cases,
    IKeyedDispatchEmitter keyedDispatch,
    IImplicitExceptionEmitter exceptions,
    IAddressInstructionEmitter addresses,
    IReferenceWriteBarrierEmitter writeBarrier) : IMemberMethodCaseEmitter
{
    private readonly IReferenceWriteBarrierEmitter _writeBarrier = writeBarrier ??
        throw new ArgumentNullException(nameof(writeBarrier));
    private readonly IMemberDescriptorLayout _descriptors = descriptors ??
        throw new ArgumentNullException(nameof(descriptors));
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
    private readonly IVirtualDispatchCasePlanner _cases = cases ??
        throw new ArgumentNullException(nameof(cases));
    private readonly IKeyedDispatchEmitter _keyedDispatch = keyedDispatch ??
        throw new ArgumentNullException(nameof(keyedDispatch));
    private readonly IImplicitExceptionEmitter _exceptions = exceptions ??
        throw new ArgumentNullException(nameof(exceptions));
    private readonly IAddressInstructionEmitter _addresses = addresses ??
        throw new ArgumentNullException(nameof(addresses));

    public ImmutableArray<MemberCase> Cases(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, MemberExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(plan);
        int receiverLocal = request.Local(1, CliValueKind.ManagedReference);
        int argumentsLocal = request.Local(2, CliValueKind.ManagedReference);
        var cases = plan.Methods.Values
            .OrderBy(entry => entry.Descriptor.CanonicalName, StringComparer.Ordinal)
            .ToArray();
        return [.. cases.Select(execution => new MemberCase(_descriptors.GetDescriptorAddress(execution.Descriptor), () => EmitCase(execution)))];

        void EmitCase(MemberMethodExecutionPlan execution)
        {
            MethodInstanceModel method = execution.Descriptor;
            _argumentCounts.Check(
                code,
                argumentsLocal,
                method.Signature.ParameterSignatureTypes.Length, request.Instruction.Context.ManagedThrowFunction);
            if (IsConstructor(method))
            {
                EmitConstructorCall(request, code, argumentsLocal, method);
                return;
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
        }
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
        // The receiver's type id picks the target, through the runs of type ids that share one, as a virtual call does.
        var dispatchCases = _cases.Plan(execution.Targets, execution.Targets);
        var typeIdLocal = request.Instruction.Context.NumericTemporaryI4;
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)receiverLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)typeIdLocal)));
        _keyedDispatch.Emit(code, new(
            typeIdLocal,
            wide: false,
            [.. dispatchCases.Select(dispatchCase => new KeyRange(dispatchCase.LowTypeId, dispatchCase.HighTypeId))],
            index => EmitMethodCall(
                request,
                code,
                receiverLocal,
                argumentsLocal,
                dispatchCases[index].Target.Method,
                resultType,
                dispatchCases[index].Target.ReceiverType),
            () => _exceptions.Emit(code, ManagedExceptionKind.InvalidCast, request.Instruction.Context.ManagedThrowFunction)));
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
            // The callee wrote its result into the box, which a collection during the call may have made old.
            _writeBarrier.Emit(request.Instruction, code, resultLocal, resultType);
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
