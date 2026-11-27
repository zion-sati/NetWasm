// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

// A RuntimeHelpers.IsKnownConstant call constant folding left is one whose argument it did not know: false, in place
// of the argument, as .NET's JIT answers for a value it does not see as a constant.
public sealed class IsKnownConstantIntrinsicEmitterTests
{
    [Theory]
    [InlineData(CliValueKind.I4, 0)]
    [InlineData(CliValueKind.ManagedReference, 2)]
    public void AnUnfoldedCallIsFalse(CliValueKind argument, int argumentBase)
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.IsKnownConstant,
            [.. Enumerable.Repeat(CliValueKind.I4, argumentBase), argument]);
        request = request with { Call = request.Call with { ArgumentBase = argumentBase, Consumed = 1 } };
        var writer = new RecordingWriter();

        new IsKnownConstantIntrinsicEmitter().Emit(request, writer);

        Assert.Equal([WasmOpcodes.I32Constant, WasmOpcodes.LocalSet], writer.Instructions.Select(instruction => instruction.Opcode));
        Assert.Equal(0, writer.Instructions[0].Operand.SignedValue);
        Assert.Equal((uint)request.Local(0, CliValueKind.I4), writer.Instructions[1].Operand.UnsignedValue);
    }

    private sealed class RecordingWriter : IWasmInstructionWriter
    {
        public List<WasmInstruction> Instructions { get; } = [];

        public void Write(WasmInstruction instruction) => Instructions.Add(instruction);
    }
}
