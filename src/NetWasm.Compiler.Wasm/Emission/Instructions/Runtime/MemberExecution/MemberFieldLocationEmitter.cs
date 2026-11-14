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

internal sealed class MemberFieldLocationEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IAddressInstructionEmitter addresses,
    IInstanceFieldLayoutProvider fields,
    IStaticFieldLayoutProvider staticFields) : IMemberFieldLocationEmitter
{
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly IValueLayoutProvider _values = values ??
        throw new ArgumentNullException(nameof(values));
    private readonly IAddressInstructionEmitter _addresses = addresses ??
        throw new ArgumentNullException(nameof(addresses));
    private readonly IInstanceFieldLayoutProvider _fields = fields ??
        throw new ArgumentNullException(nameof(fields));
    private readonly IStaticFieldLayoutProvider _staticFields = staticFields ??
        throw new ArgumentNullException(nameof(staticFields));

    public int Emit(IWasmInstructionWriter code, int receiverLocal, FieldInstanceModel field, bool address)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(field);
        if (field.Definition.IsStatic)
        {
            _addresses.Emit(code, _staticFields.GetStaticFieldLayout(field).Address);
            return 0;
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)receiverLocal)));
        var offset = _fields.GetFieldLayout(field).Offset;
        if (field.DeclaringType.IsValueType)
        {
            offset += WasmTargetLayout.Align(
                _layouts.Target.ObjectHeaderSize,
                _values.GetValueLayout(field.DeclaringType).Alignment);
        }
        if (!address)
        {
            return offset;
        }
        _addresses.Emit(code, offset);
        _addresses.Emit(code, AddressOperation.Add);
        return 0;
    }
}
