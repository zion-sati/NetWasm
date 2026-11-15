// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ExceptionDispatchPreservedIntrinsicEmitterTests
{
    [Fact]
    public void RethrowsThroughTheSharedRethrowFunction()
    {
        var request = WithRethrowFunction(
            CreateRuntimeIntrinsicRequest(
                RuntimeIntrinsic.ExceptionDispatchPreserved,
                [CliValueKind.ManagedReference]),
            OptionalFunctionIndex.At(43));
        var writer = new RecordingInstructionWriter();

        new ExceptionDispatchPreservedIntrinsicEmitter().Emit(request, writer);

        var instructions = writer.ToInstructions();
        Assert.Equal(
            [WasmOpcodes.LocalGet, WasmOpcodes.Call, WasmOpcodes.Unreachable],
            instructions.Select(static instruction => instruction.Opcode));
        Assert.Equal(
            (uint)request.Local(0, CliValueKind.ManagedReference),
            instructions[0].Operand.UnsignedValue);
        Assert.Equal(43u, instructions[1].Operand.UnsignedValue);
    }

    [Fact]
    public void MissingRethrowFunctionFailsBeforeEmission()
    {
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.ExceptionDispatchPreserved,
            [CliValueKind.ManagedReference]);
        var writer = new RecordingInstructionWriter();

        Assert.Throws<InvalidOperationException>(() =>
            new ExceptionDispatchPreservedIntrinsicEmitter().Emit(request, writer));
        Assert.Empty(writer.ToInstructions());
    }

    private static RuntimeIntrinsicEmissionRequest WithRethrowFunction(
        RuntimeIntrinsicEmissionRequest request,
        OptionalFunctionIndex functionIndex) => request with
        {
            Call = request.Call with
            {
                Instruction = request.Instruction with
                {
                    Target = request.Instruction.Target with
                    {
                        FunctionIndices = request.Instruction.Target.FunctionIndices with
                        {
                            ManagedRethrowFunctionIndex = functionIndex,
                        },
                    },
                },
            },
        };
}
