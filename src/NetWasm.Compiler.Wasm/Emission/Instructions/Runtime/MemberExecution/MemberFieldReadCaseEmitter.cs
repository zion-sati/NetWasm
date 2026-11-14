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

// A field read: a static field's owner is initialized, an instance field's receiver checked, and the value
// becomes the call's result (a nullable's value's box, a struct copied into a new box).
internal sealed class MemberFieldReadCaseEmitter(
    IMemberDescriptorLayout descriptors,
    IMemberDescriptorCaseEmitter descriptorCases,
    IStaticInitializationEmitter initialization,
    IMemberReferenceCheckEmitter referenceChecks,
    INullableTypeResolver nullableTypes,
    IMemberFieldLocationEmitter fieldLocations,
    IMemberNullableResultEmitter nullableResults,
    IMemberObjectAllocator allocator,
    IBoxPayloadAddressEmitter payloads,
    IMemberResultBoxEmitter results,
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IAddressInstructionEmitter addresses) : IMemberFieldReadCaseEmitter
{
    private readonly IMemberDescriptorLayout _descriptors = descriptors ??
        throw new ArgumentNullException(nameof(descriptors));
    private readonly IMemberDescriptorCaseEmitter _descriptorCases = descriptorCases ??
        throw new ArgumentNullException(nameof(descriptorCases));
    private readonly IStaticInitializationEmitter _initialization = initialization ??
        throw new ArgumentNullException(nameof(initialization));
    private readonly IMemberReferenceCheckEmitter _referenceChecks = referenceChecks ??
        throw new ArgumentNullException(nameof(referenceChecks));
    private readonly INullableTypeResolver _nullableTypes = nullableTypes ??
        throw new ArgumentNullException(nameof(nullableTypes));
    private readonly IMemberFieldLocationEmitter _fieldLocations = fieldLocations ??
        throw new ArgumentNullException(nameof(fieldLocations));
    private readonly IMemberNullableResultEmitter _nullableResults = nullableResults ??
        throw new ArgumentNullException(nameof(nullableResults));
    private readonly IMemberObjectAllocator _allocator = allocator ??
        throw new ArgumentNullException(nameof(allocator));
    private readonly IBoxPayloadAddressEmitter _payloads = payloads ??
        throw new ArgumentNullException(nameof(payloads));
    private readonly IMemberResultBoxEmitter _results = results ??
        throw new ArgumentNullException(nameof(results));
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly IValueLayoutProvider _values = values ??
        throw new ArgumentNullException(nameof(values));
    private readonly IAddressInstructionEmitter _addresses = addresses ??
        throw new ArgumentNullException(nameof(addresses));

    public int EmitCases(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, MemberExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(plan);
        int descriptorLocal = request.Local(0, CliValueKind.ManagedReference);
        int receiverLocal = request.Local(1, CliValueKind.ManagedReference);
        var cases = plan.Fields.Values
            .OrderBy(field => field.CanonicalName, StringComparer.Ordinal)
            .ToArray();
        var throwFunction = request.Instruction.Context.ManagedThrowFunction;
        foreach (var field in cases)
        {
            _descriptorCases.Open(
                code,
                descriptorLocal,
                _descriptors.GetDescriptorAddress(field));
            if (field.Definition.IsStatic)
            {
                _initialization.Emit(new(field.Definition.DeclaringType, field.DeclaringType,
                    request.Instruction.Target.StaticInitializerGuards), code, request.FunctionIndices);
            }
            else
            {
                _referenceChecks.Check(code, receiverLocal,
                    field.DeclaringType.IsValueType ? field.DeclaringType : null, throwFunction);
            }
            var underlying = _nullableTypes.Resolve(field.FieldType);
            if (underlying is not null)
            {
                _fieldLocations.Emit(code, receiverLocal, field, address: true);
                _nullableResults.Emit(request, code, underlying);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
                continue;
            }
            if (field.FieldType.StackKind == CliValueKind.ValueType)
            {
                // The box comes first: the receiver is the call's argument, so it stays rooted while the
                // allocation collects, and nothing runs between the copy and the result.
                var valueLayout = _values.GetValueLayout(field.FieldType);
                int resultLocal = request.Local(0, CliValueKind.ManagedReference);
                _allocator.Allocate(request, code, field.FieldType, resultLocal, rooted: false);
                _payloads.Emit(code, resultLocal, field.FieldType);
                _fieldLocations.Emit(code, receiverLocal, field, address: true);
                SmallBlockEmitter.EmitKnownSizeCopy(code, request.Instruction.Context, valueLayout.Size,
                    SmallBlockEmitter.WidestChunk(valueLayout), writer => _addresses.Emit(writer, valueLayout.Size));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
                continue;
            }
            var fieldOffset = _fieldLocations.Emit(code, receiverLocal, field, address: false);
            ManagedMemoryEmitter.EmitLoadByType(
                code,
                _layouts.Target,
                fieldOffset,
                field.FieldType,
                _values.GetValueLayout(field.FieldType).Size, scratch: request.Instruction.Context.VectorScratch);
            _results.Emit(request, code, field.FieldType);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        }
        return cases.Length;
    }
}
