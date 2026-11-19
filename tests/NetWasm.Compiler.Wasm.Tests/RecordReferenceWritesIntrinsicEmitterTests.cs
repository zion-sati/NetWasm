// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

// Buffer.RecordReferenceWrites, which follows a block move of references: the write barrier over the
// destination's range for a program whose collector has generations, and nothing for any other.
public sealed class RecordReferenceWritesIntrinsicEmitterTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmitsTheRangeBarrierOnlyForAProgramThatRecords(bool records)
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = new RecordReferenceWritesIntrinsicEmitter(new ReferenceWriteBarrierEmitter(imports, new ReferenceWriteRecordPolicy(new RecordingLayoutProvider())));
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.RecordReferenceWrites,
            [CliValueKind.ManagedAddress, CliValueKind.NativeInt]);
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
                            IncludeReferenceWriteBarrier = records,
                        },
                    },
                },
            },
        };
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Emit(request, code);

        var instructions = code.ToInstructions();
        if (!records)
        {
            Assert.Empty(instructions);
            return;
        }
        Assert.Equal([WasmOpcodes.LocalGet, WasmOpcodes.LocalGet, WasmOpcodes.Call],
            instructions.Select(instruction => instruction.Opcode));
        Assert.Equal((uint)request.Local(0, CliValueKind.ManagedAddress), instructions[0].Operand.UnsignedValue);
        Assert.Equal((uint)request.Local(1, CliValueKind.NativeInt), instructions[1].Operand.UnsignedValue);
        Assert.Equal(
            (uint)imports.Resolve(RuntimeImportSymbol.RecordReferenceWrites, request.Instruction.Target.RuntimeImportSelection),
            instructions[2].Operand.UnsignedValue);
    }

    [Fact]
    public void RejectsMissingCollaboratorAndArguments()
    {
        var emitter = new RecordReferenceWritesIntrinsicEmitter(
            new ReferenceWriteBarrierEmitter(WasmRuntimeImports.CreateCatalog(), new ReferenceWriteRecordPolicy(new RecordingLayoutProvider())));
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.RecordReferenceWrites, [CliValueKind.ManagedAddress, CliValueKind.NativeInt]);

        Assert.Throws<ArgumentNullException>(() => new RecordReferenceWritesIntrinsicEmitter(null!));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, new EmitterTestSupport.RecordingInstructionWriter()));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, null!));
    }
}
