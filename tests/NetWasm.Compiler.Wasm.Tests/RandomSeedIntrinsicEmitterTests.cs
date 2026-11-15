// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class RandomSeedIntrinsicEmitterTests
{
    [Fact]
    public void EmitReadsRuntimeSeedAndPublishesInt64Result()
    {
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(
            RuntimeIntrinsic.RandomSeed,
            runtimeImports);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.RandomSeed,
            []);
        var writer = new RecordingWriter();

        emitter.Emit(request, writer);

        Assert.Equal(
            [WasmOpcodes.Call, WasmOpcodes.LocalSet],
            writer.Instructions.Select(instruction => instruction.Opcode));
        Assert.Equal(
            (uint)runtimeImports.Resolve(
                RuntimeImportSymbol.RandomSeed,
                request.Instruction.Target.RuntimeImportSelection),
            writer.Instructions[0].Operand!.UnsignedValue);
        Assert.Equal(
            (uint)request.Local(0, CliValueKind.I8),
            writer.Instructions[1].Operand!.UnsignedValue);
    }

    private sealed class RecordingWriter : IWasmInstructionWriter
    {
        public List<WasmInstruction> Instructions { get; } = [];

        public void Write(WasmInstruction instruction) => Instructions.Add(instruction);
    }
}
