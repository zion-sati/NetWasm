using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class InstructionPrefixEmitter : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new(CilOperation.Constrained, InstructionFamily.CallsAndCallableLoading, EmitPrefix),
        new(CilOperation.Volatile, InstructionFamily.ArraysFieldsStatics, EmitPrefix),
        new(CilOperation.Readonly, InstructionFamily.ArraysFieldsStatics, EmitPrefix),
        new(CilOperation.Unaligned, InstructionFamily.ValueObjectBlockMemory, EmitPrefix),
    ];

    private static void EmitPrefix(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        ArgumentNullException.ThrowIfNull(request);
}
