// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ManagedByRefAggregateInitializationEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void LoadsPromotedLanesFromManagedByRefParameter(WasmTarget target)
    {
        var type = CliTypeIdentity.Named(
            new("byref-initialization-tests"),
            "Tests",
            "Buffer",
            isValueType: true);
        var parameter = new ManagedByRefAggregateParameterPlan(
            type,
            12,
            [
                new(0, 4, CliValueKind.I4),
                new(4, 8, CliValueKind.I8),
            ]);
        var plan = new ManagedByRefAggregatePromotionPlan(
            ImmutableDictionary<int, ManagedByRefAggregateParameterPlan>.Empty.Add(
                0,
                parameter),
            []);
        var context = CreateMethodEmissionContext() with
        {
            ParameterOffset = 1,
            ManagedByRefAggregatePromotions = new(
                plan,
                ImmutableDictionary<int, ImmutableArray<int>>.Empty.Add(
                    0,
                    [40, 41])),
        };
        var writer = new RecordingInstructionWriter();
        var emitter = new ManagedByRefAggregateInitializationEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)));

        emitter.Emit(writer, context);

        var instructions = writer.ToInstructions();
        Assert.Equal(2, instructions.Count(instruction =>
            instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand.UnsignedValue == 1U));
        Assert.Single(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I32Load &&
            instruction.Operand.Offset == 0U);
        Assert.Single(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I64Load &&
            instruction.Operand.Offset == 4U);
        Assert.Equal([40U, 41U], instructions
            .Where(instruction => instruction.Opcode == WasmOpcodes.LocalSet)
            .Select(instruction => instruction.Operand.UnsignedValue)
            .ToArray());
    }

    [Fact]
    public void ValidatesInputsAndDependency()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new ManagedByRefAggregateInitializationEmitter(layouts);
        var context = CreateMethodEmissionContext();
        var writer = new RecordingInstructionWriter();

        emitter.Emit(writer, context);

        Assert.Empty(writer.ToInstructions());
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, context));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(writer, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedByRefAggregateInitializationEmitter(null!));
    }
}
