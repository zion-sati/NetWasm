// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class GcRunQueuedFinalizersIntrinsicEmitterTests
{
    // With finalizable types, the intrinsic calls the runtime's finalizer safepoint and keeps the exception it returns.
    [Fact]
    public void EmitCallsTheFinalizerSafepointAndKeepsItsResult()
    {
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(RuntimeIntrinsic.GcRunQueuedFinalizers, runtimeImports);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.GcRunQueuedFinalizers, []);
        var writer = new RecordingWriter();

        emitter.Emit(request, writer);

        Assert.Collection(
            writer.Instructions,
            call =>
            {
                Assert.Equal(WasmOpcodes.Call, call.Opcode);
                Assert.Equal(
                    (uint)runtimeImports.Resolve(
                        RuntimeImportSymbol.FinalizerSafepoint,
                        request.Instruction.Target.RuntimeImportSelection),
                    call.Operand!.UnsignedValue);
            },
            store => Assert.Equal(WasmOpcodes.LocalSet, store.Opcode));
    }

    // Without one, nothing can be queued: the result is null, a pointer-sized zero, and the runtime is not called.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmitYieldsNullWhenNoFinalizableTypeExists(bool memory64)
    {
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(RuntimeIntrinsic.GcRunQueuedFinalizers, runtimeImports);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.GcRunQueuedFinalizers, []);
        request = request with
        {
            Call = request.Call with
            {
                Instruction = request.Instruction with
                {
                    Target = request.Instruction.Target with
                    {
                        RuntimeImportSelection = request.Instruction.Target.RuntimeImportSelection with
                        {
                            IncludeFinalization = false,
                        },
                    },
                },
            },
            Target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32,
        };
        var writer = new RecordingWriter();

        emitter.Emit(request, writer);

        Assert.Equal(
            [memory64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant, WasmOpcodes.LocalSet],
            writer.Instructions.Select(static instruction => instruction.Opcode));
        Assert.Equal(0, memory64 ? writer.Instructions[0].Operand!.Signed64Value : writer.Instructions[0].Operand!.SignedValue);
        Assert.Equal((uint)request.Local(0, CliValueKind.ManagedReference), writer.Instructions[1].Operand!.UnsignedValue);
    }

    private sealed class RecordingWriter : IWasmInstructionWriter
    {
        public List<WasmInstruction> Instructions { get; } = [];

        public void Write(WasmInstruction instruction) => Instructions.Add(instruction);
    }
}
