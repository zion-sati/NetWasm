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

// Assignment: the value, a reference the field's type accepts or a box of exactly its type, goes into the field;
// the result is null. Constants and read-only fields cannot be written, and a nullable field's value would need
// rewrapping, so their descriptors take no case (the interpreter does not compile them).
internal sealed class MemberFieldWriteCaseEmitter(
    IMemberDescriptorLayout descriptors,
    IMemberDescriptorCaseEmitter descriptorCases,
    IStaticInitializationEmitter initialization,
    IMemberReferenceCheckEmitter referenceChecks,
    INullableTypeResolver nullableTypes,
    IMemberFieldLocationEmitter fieldLocations,
    IBoxPayloadAddressEmitter payloads,
    ITargetLayout layouts,
    IValueLayoutProvider values,
    ITypeLayoutProvider types,
    IAddressInstructionEmitter addresses,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions) : IMemberFieldWriteCaseEmitter
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
    private readonly IBoxPayloadAddressEmitter _payloads = payloads ??
        throw new ArgumentNullException(nameof(payloads));
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly IValueLayoutProvider _values = values ??
        throw new ArgumentNullException(nameof(values));
    private readonly ITypeLayoutProvider _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly IAddressInstructionEmitter _addresses = addresses ??
        throw new ArgumentNullException(nameof(addresses));
    private readonly IRuntimeImportResolver _runtimeImports = runtimeImports ??
        throw new ArgumentNullException(nameof(runtimeImports));
    private readonly IImplicitExceptionEmitter _exceptions = exceptions ??
        throw new ArgumentNullException(nameof(exceptions));

    public int EmitCases(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, MemberExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(plan);
        int descriptorLocal = request.Local(0, CliValueKind.ManagedReference);
        int receiverLocal = request.Local(1, CliValueKind.ManagedReference);
        int valueLocal = request.Local(2, CliValueKind.ManagedReference);
        var cases = plan.Fields.Values
            .Where(field => !field.Definition.IsLiteral && !field.Definition.IsInitOnly &&
                _nullableTypes.Resolve(field.FieldType) is null)
            .OrderBy(field => field.CanonicalName, StringComparer.Ordinal)
            .ToArray();
        var throwFunction = request.Instruction.Context.ManagedThrowFunction;
        foreach (var field in cases)
        {
            _descriptorCases.Open(code, descriptorLocal, _descriptors.GetDescriptorAddress(field));
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
            var fieldType = field.FieldType;
            var valueLayout = _values.GetValueLayout(fieldType);
            if (fieldType.StackKind == CliValueKind.ManagedReference)
            {
                EmitAssignabilityCheck(code, valueLocal, fieldType, throwFunction);
                var offset = _fieldLocations.Emit(code, receiverLocal, field, address: false);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)valueLocal)));
                ManagedMemoryEmitter.EmitStoreByType(code, _layouts.Target, offset, fieldType, valueLayout.Size,
                    scratch: request.Instruction.Context.VectorScratch);
            }
            else
            {
                _referenceChecks.Check(code, valueLocal, fieldType, throwFunction);
                if (fieldType.StackKind == CliValueKind.ValueType)
                {
                    _fieldLocations.Emit(code, receiverLocal, field, address: true);
                    _payloads.Emit(code, valueLocal, fieldType);
                    SmallBlockEmitter.EmitKnownSizeCopy(code, request.Instruction.Context, valueLayout.Size,
                        SmallBlockEmitter.WidestChunk(valueLayout), writer => _addresses.Emit(writer, valueLayout.Size));
                }
                else
                {
                    var offset = _fieldLocations.Emit(code, receiverLocal, field, address: false);
                    _payloads.Emit(code, valueLocal, fieldType);
                    ManagedMemoryEmitter.EmitLoadByType(code, _layouts.Target, 0, fieldType, valueLayout.Size,
                        scratch: request.Instruction.Context.VectorScratch);
                    ManagedMemoryEmitter.EmitStoreByType(code, _layouts.Target, offset, fieldType, valueLayout.Size,
                        scratch: request.Instruction.Context.VectorScratch);
                }
            }
            _addresses.Emit(code, 0);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)request.Local(0, CliValueKind.ManagedReference))));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        }
        return cases.Length;
    }

    // A non-null reference must be of a type the field accepts; object accepts any.
    private void EmitAssignabilityCheck(
        IWasmInstructionWriter code, int valueLocal, CliTypeIdentity fieldType, OptionalFunctionIndex throwFunction)
    {
        if (fieldType.CanonicalName == "primitive:object")
        {
            return;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)valueLocal)));
        _addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)valueLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(_types.GetObjectLayout(fieldType).TypeId)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)_runtimeImports.Resolve(RuntimeImportSymbol.IsAssignable))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        _exceptions.Emit(code, ManagedExceptionKind.InvalidCast, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
