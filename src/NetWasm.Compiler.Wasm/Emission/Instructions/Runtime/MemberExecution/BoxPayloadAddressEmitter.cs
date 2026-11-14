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

internal sealed class BoxPayloadAddressEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IAddressInstructionEmitter addresses) : IBoxPayloadAddressEmitter
{
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly IValueLayoutProvider _values = values ??
        throw new ArgumentNullException(nameof(values));
    private readonly IAddressInstructionEmitter _addresses = addresses ??
        throw new ArgumentNullException(nameof(addresses));

    public void Emit(IWasmInstructionWriter code, int objectLocal, CliTypeIdentity valueType)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(valueType);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)objectLocal)));
        _addresses.Emit(code, WasmTargetLayout.Align(
            _layouts.Target.ObjectHeaderSize,
            _values.GetValueLayout(valueType).Alignment));
        _addresses.Emit(code, AddressOperation.Add);
    }
}
