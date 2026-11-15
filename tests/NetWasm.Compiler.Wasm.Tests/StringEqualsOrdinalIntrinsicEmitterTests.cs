// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class StringEqualsOrdinalIntrinsicEmitterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmitsLeafOrdinalComparisonForEveryAddressWidth(bool memory64)
    {
        var target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32;
        var layouts = new RecordingLayoutProvider(target);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.StringEqualsOrdinal,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference]) with
        {
            Target = target,
        };
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        new StringEqualsOrdinalIntrinsicEmitter(
            layouts,
            EmitterTestSupport.CreateAddressInstructions(layouts)).Emit(request, writer);

        var instructions = writer.ToInstructions();
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.Loop);
        Assert.Equal(2, instructions.Count(instruction =>
            instruction.Opcode == WasmOpcodes.I32Load16Unsigned));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Opcode is WasmOpcodes.Call or WasmOpcodes.Throw);
        Assert.Contains(instructions, instruction => instruction.Opcode ==
            (memory64 ? WasmOpcodes.I64Add : WasmOpcodes.I32Add));
    }
}
