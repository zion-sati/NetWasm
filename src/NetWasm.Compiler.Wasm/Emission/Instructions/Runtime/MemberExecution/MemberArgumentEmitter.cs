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

internal sealed class MemberArgumentEmitter(
    ITargetLayout layouts,
    IRuntimeObjectLayout objects,
    IAddressInstructionEmitter addresses,
    IValueLayoutProvider values,
    IMemberReferenceCheckEmitter referenceChecks,
    IBoxPayloadAddressEmitter payloads) : IMemberArgumentEmitter
{
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly IRuntimeObjectLayout _objects = objects ??
        throw new ArgumentNullException(nameof(objects));
    private readonly IAddressInstructionEmitter _addresses = addresses ??
        throw new ArgumentNullException(nameof(addresses));
    private readonly IValueLayoutProvider _values = values ??
        throw new ArgumentNullException(nameof(values));
    private readonly IMemberReferenceCheckEmitter _referenceChecks = referenceChecks ??
        throw new ArgumentNullException(nameof(referenceChecks));
    private readonly IBoxPayloadAddressEmitter _payloads = payloads ??
        throw new ArgumentNullException(nameof(payloads));

    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, int argumentsLocal,
        IReadOnlyList<CliTypeIdentity> parameters)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(parameters);
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
            _referenceChecks.Check(code, argumentLocal, parameter, request.Instruction.Context.ManagedThrowFunction);
            if (parameter.StackKind == CliValueKind.ValueType)
            {
                // A struct goes by its box's contents; a callee that writes its parameter copies it first.
                _payloads.Emit(code, argumentLocal, parameter);
                continue;
            }
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
                valueLayout.Size, scratch: request.Instruction.Context.VectorScratch);
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
}
