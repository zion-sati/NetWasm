using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class StructuredControlFlowInstructionHandler : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        .. OwnedOperations.Select(operation => new InstructionCommand(
            operation,
            InstructionFamily.StructuredControlFlow,
            RejectScalarLowering)),
    ];

    private static ImmutableArray<CilOperation> OwnedOperations { get; } =
    [
        CilOperation.Branch,
        CilOperation.BranchIfTrue,
        CilOperation.BranchIfFalse,
        CilOperation.BranchIfEqual,
        CilOperation.BranchIfNotEqual,
        CilOperation.BranchIfGreaterThanSigned,
        CilOperation.BranchIfGreaterThanUnsigned,
        CilOperation.BranchIfGreaterThanOrEqualSigned,
        CilOperation.BranchIfGreaterThanOrEqualUnsigned,
        CilOperation.BranchIfLessThanSigned,
        CilOperation.BranchIfLessThanUnsigned,
        CilOperation.BranchIfLessThanOrEqualSigned,
        CilOperation.BranchIfLessThanOrEqualUnsigned,
        CilOperation.Switch,
        CilOperation.Leave,
    ];

    private static void RejectScalarLowering(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        throw new InvalidOperationException(
            "Control-flow instruction reached scalar lowering.");
}
