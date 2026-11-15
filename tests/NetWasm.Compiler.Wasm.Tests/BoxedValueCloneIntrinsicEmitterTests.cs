// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class BoxedValueCloneIntrinsicEmitterTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void EmitClonesTheBoxAndPublishesTheReference(
        bool memory64,
        int argumentBase)
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(
            RuntimeIntrinsic.BoxedValueClone,
            imports);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.BoxedValueClone,
            [
                .. Enumerable.Repeat(CliValueKind.I4, argumentBase),
                CliValueKind.ManagedReference,
            ]);
        request = request with
        {
            Target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32,
            Call = request.Call with { ArgumentBase = argumentBase, Consumed = 1 },
        };
        var writer = new RecordingWriter();

        emitter.Emit(request, writer);

        Assert.Equal(
            [WasmOpcodes.LocalGet, WasmOpcodes.Call, WasmOpcodes.LocalSet],
            writer.Instructions.Select(instruction => instruction.Opcode));
        var expectedLocal = (uint)request.Local(0, CliValueKind.ManagedReference);
        Assert.Equal(expectedLocal, writer.Instructions[0].Operand.UnsignedValue);
        Assert.Equal(
            (uint)imports.Resolve(
                RuntimeImportSymbol.BoxedValueClone,
                request.Instruction.Target.RuntimeImportSelection),
            writer.Instructions[1].Operand.UnsignedValue);
        Assert.Equal(expectedLocal, writer.Instructions[2].Operand.UnsignedValue);
    }

    private sealed class RecordingWriter : IWasmInstructionWriter
    {
        public List<WasmInstruction> Instructions { get; } = [];

        public void Write(WasmInstruction instruction) => Instructions.Add(instruction);
    }
}
