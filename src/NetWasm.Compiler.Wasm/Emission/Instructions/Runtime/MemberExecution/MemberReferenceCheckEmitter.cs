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

internal sealed class MemberReferenceCheckEmitter(
    IAddressInstructionEmitter addresses,
    IImplicitExceptionEmitter exceptions,
    IBoxedValueTypeValidator typeValidator) : IMemberReferenceCheckEmitter
{
    private readonly IAddressInstructionEmitter _addresses = addresses ??
        throw new ArgumentNullException(nameof(addresses));
    private readonly IImplicitExceptionEmitter _exceptions = exceptions ??
        throw new ArgumentNullException(nameof(exceptions));
    private readonly IBoxedValueTypeValidator _typeValidator = typeValidator ??
        throw new ArgumentNullException(nameof(typeValidator));

    public void Check(IWasmInstructionWriter code, int local, CliTypeIdentity? boxedType, OptionalFunctionIndex throwFunction)
    {
        ArgumentNullException.ThrowIfNull(code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));
        _addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        _exceptions.Emit(code, ManagedExceptionKind.NullReference, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        if (boxedType is not null)
        {
            _typeValidator.Validate(code, local, boxedType, throwFunction);
        }
    }
}
