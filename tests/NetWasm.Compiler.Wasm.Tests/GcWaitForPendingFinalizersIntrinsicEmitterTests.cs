// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class GcWaitForPendingFinalizersIntrinsicEmitterTests
{
    [Fact]
    public void EmitCallsRuntimeWaitCapability()
    {
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(
            RuntimeIntrinsic.GcWaitForPendingFinalizers,
            runtimeImports);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.GcWaitForPendingFinalizers,
            []);
        var writer = new RecordingWriter();

        emitter.Emit(request, writer);

        var instruction = Assert.Single(writer.Instructions);
        Assert.Equal(WasmOpcodes.Call, instruction.Opcode);
        Assert.Equal(
            (uint)runtimeImports.Resolve(
                RuntimeImportSymbol.GcWaitForPendingFinalizers,
                request.Instruction.Target.RuntimeImportSelection),
            instruction.Operand!.UnsignedValue);
    }

    [Fact]
    public void EmitDoesNothingWhenNoFinalizableTypeExists()
    {
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(
            RuntimeIntrinsic.GcWaitForPendingFinalizers,
            runtimeImports);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.GcWaitForPendingFinalizers,
            []);
        request = request with
        {
            Call = request.Call with
            {
                Instruction = request.Instruction with
                {
                    Target = request.Instruction.Target with
                    {
                        RuntimeImportSelection = request.Instruction.Target
                            .RuntimeImportSelection with
                        {
                            IncludeFinalization = false,
                        },
                    },
                },
            },
        };
        var writer = new RecordingWriter();

        emitter.Emit(request, writer);

        Assert.Empty(writer.Instructions);
    }

    private sealed class RecordingWriter : IWasmInstructionWriter
    {
        private readonly List<WasmInstruction> _instructions = [];

        public List<WasmInstruction> Instructions => _instructions;

        public void Write(WasmInstruction instruction) => _instructions.Add(instruction);
    }
}
