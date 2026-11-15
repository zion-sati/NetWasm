// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StructuredConditionValueEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I4, 0)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I4, 0)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.NativeInt, 0)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.NativeInt, 64)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I8, 64)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I8, 64)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.ManagedAddress, 32)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.ManagedAddress, 64)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.ManagedReference, 32)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.ManagedReference, 64)]
    public void ReadsTheTypedSlotAndNormalizesWideOrReferenceConditions(WasmTarget target, CliValueKind kind, int normalization)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = new[] { new StructuredConditionValueEmitter(layouts) }
            .Cast<IStructuredConditionValueEmitter>().Single();
        var context = CreateMethodEmissionContext();
        var code = new RecordingInstructionWriter();

        emitter.Emit(code, context, new(0, kind));

        var instructions = code.ToInstructions();
        Assert.Equal(WasmOpcodes.LocalGet, instructions[0].Opcode);
        Assert.Equal((uint)WasmLocalLayoutPlanner.GetEvaluationStackLocal(context.StackLocals, 0, kind, layouts.Target),
            instructions[0].Operand.UnsignedValue);
        if (normalization == 0)
        {
            Assert.Single(instructions);
        }
        else
        {
            Assert.Equal(3, instructions.Length);
            Assert.Equal(normalization == 64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero, instructions[1].Opcode);
            Assert.Equal(WasmOpcodes.I32EqualZero, instructions[2].Opcode);
        }
    }

    [Fact]
    public void RejectsMissingInputsWithoutWriting()
    {
        var emitter = new[] { new StructuredConditionValueEmitter(new RecordingLayoutProvider()) }
            .Cast<IStructuredConditionValueEmitter>().Single();
        var code = new RecordingInstructionWriter();
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, CreateMethodEmissionContext(), new(0, CliValueKind.I4)));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(code, null!, new(0, CliValueKind.I4)));
        Assert.Empty(code.ToInstructions());
    }
}
