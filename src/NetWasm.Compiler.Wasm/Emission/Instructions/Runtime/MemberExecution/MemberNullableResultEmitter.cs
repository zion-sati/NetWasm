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

internal sealed class MemberNullableResultEmitter(
    INullableBoxEmitter nullableBoxes) : IMemberNullableResultEmitter
{
    private readonly INullableBoxEmitter _nullableBoxes = nullableBoxes ??
        throw new ArgumentNullException(nameof(nullableBoxes));

    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, CliTypeIdentity underlying)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(underlying);
        var source = request.Instruction.Context.ObjectTemporary;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)source)));
        _nullableBoxes.Emit(code, underlying, source,
            request.Local(0, CliValueKind.ManagedReference), request.Instruction.Context.ManagedThrowFunction,
            request.Instruction.Target.ObjectAllocation, request.Instruction.Context.VectorScratch);
    }
}
