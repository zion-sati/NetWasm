// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Arrays;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ArrayElementAddressEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false, 0)]
    [InlineData(WasmTarget.Wasm64, false, 0)]
    [InlineData(WasmTarget.Wasm32, true, 0)]
    [InlineData(WasmTarget.Wasm64, true, 0)]
    [InlineData(WasmTarget.Wasm32, true, 1)]
    [InlineData(WasmTarget.Wasm64, true, 1)]
    [InlineData(WasmTarget.Wasm32, true, 2)]
    [InlineData(WasmTarget.Wasm64, true, 2)]
    public void UsesBaseAndConstantOffsetOnlyForCurrentRegisterBackedProof(WasmTarget target, bool cached, int rejection)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = new[] { new ArrayElementAddressEmitter(layouts, CreateAddressInstructions(layouts)) }
            .Cast<IArrayElementAddressEmitter>().Single();
        var request = CreateInstructionRequest(CilOperation.LoadArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4]);
        var header = request.Header with
        {
            Instructions = [request.Instruction],
            Locals = [CliValueKind.I4],
            LocalSignatureTypes = [CliTypeIdentity.FromStackKind(CliValueKind.I4)],
        };
        header = header with
        {
            ArrayBoundsProof = new(header.Instructions, header.Locals, header.LocalSignatureTypes,
            [request.Instruction.Offset])
            {
                IndexOffsets = ImmutableDictionary<int, ArrayIndexOffset>.Empty.Add(request.Instruction.Offset, new(0, 3)),
            }
        };
        if (rejection == 1) header = header with { Instructions = [request.Instruction with { OriginalOffset = 123 }] };
        var context = request.Context with { LocalBase = 40 };
        if (rejection == 2) context = context with { ValueLayout = context.ValueLayout with { SpilledScalarLocals = [0] } };
        if (cached)
        {
            var slot = new ArrayReferenceSlot(false, 1);
            context = context with
            {
                ArrayMetadata = new(new([slot],
                ImmutableDictionary<int, ArrayReferenceSlot>.Empty.Add(request.Instruction.Offset, slot)),
                ImmutableDictionary<ArrayReferenceSlot, ArrayMetadataSnapshotLocals>.Empty.Add(slot, new(50, 51)))
            };
        }
        request = request with { Header = header, Context = context };
        var code = new RecordingInstructionWriter();

        emitter.Emit(request, code, 3, 4, 8);

        var instructions = code.ToInstructions();
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand.UnsignedValue == (rejection == 0 ? 40u : 4u));
        Assert.DoesNotContain(instructions, instruction => instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand.UnsignedValue == (rejection == 0 ? 4u : 40u));
        if (rejection == 0)
        {
            Assert.Equal(target == WasmTarget.Wasm64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant,
                instructions[^2].Opcode);
            Assert.Equal(24L, target == WasmTarget.Wasm64
                ? instructions[^2].Operand.Signed64Value : instructions[^2].Operand.SignedValue);
            Assert.Equal(target == WasmTarget.Wasm64 ? WasmOpcodes.I64Add : WasmOpcodes.I32Add, instructions[^1].Opcode);
        }
    }
}
