// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.Tests.EmitterTestSupport;

namespace NetWasm.Compiler.Wasm.Tests;

// A number array that enters managed code arrives staged in linear memory as an address and a length (-1 for
// null): the emitter builds the array from the block with memory.copy and frees the block, also before an
// allocation failure throws.
public sealed class StagedValueArrayEmitterTests
{
    private const int Address = 5;
    private const int Length = 6;
    private const int Destination = 7;
    private const int Temporary = 8;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BuildsTheArrayFromTheStagedBlockAndFreesIt(WasmTarget target)
    {
        var (instructions, imports) = Emit(target, CliTypeIdentity.SzArray(CliTypeIdentity.Primitive("i4", CliValueKind.I4)));
        var free = (uint)imports.Resolve(RuntimeImportSymbol.NativeFree);
        var allocate = (uint)imports.Resolve(RuntimeImportSymbol.AllocateValueArray);

        // A negative length is null: the destination is set without allocating.
        Assert.Equal([WasmOpcodes.LocalGet, WasmOpcodes.I32Constant, WasmOpcodes.I32LessThanSigned, WasmOpcodes.If],
            instructions.Take(4).Select(instruction => instruction.Opcode));
        Assert.Equal((uint)Length, instructions[0].Operand.UnsignedValue);
        var calls = instructions.Select((instruction, index) => (instruction, index))
            .Where(pair => pair.instruction.Opcode == WasmOpcodes.Call)
            .Select(pair => (Target: pair.instruction.Operand.UnsignedValue, pair.index)).ToArray();
        var copy = instructions.ToList().FindIndex(instruction =>
            instruction.Opcode == WasmOpcodes.Prefixed && instruction.Operand.Alignment == WasmOpcodes.MemoryCopy);
        Assert.True(copy > 0);
        Assert.Single(calls, call => call.Target == allocate);
        // The block is freed on the allocation failure path, before the throw, and after the copy.
        var frees = calls.Where(call => call.Target == free).Select(call => call.index).ToArray();
        Assert.Equal(2, frees.Length);
        Assert.True(frees[0] < copy && frees[1] > copy);
        Assert.Equal((uint)Address, instructions[frees[0] - 1].Operand.UnsignedValue);
        // The byte count is the length times the element size, at the address width.
        Assert.Contains(instructions, instruction => instruction.Opcode ==
            (target == WasmTarget.Wasm64 ? WasmOpcodes.I64Multiply : WasmOpcodes.I32Multiply));
        Assert.Equal(target == WasmTarget.Wasm64,
            instructions.Any(instruction => instruction.Opcode == WasmOpcodes.I64ExtendI32Unsigned));
        Assert.Equal(WasmOpcodes.End, instructions[^1].Opcode);
        Assert.Equal((uint)Destination, instructions[^2].Operand.UnsignedValue);
    }

    private static (ImmutableArray<WasmInstruction> Instructions, IRuntimeImportResolver Imports) Emit(
        WasmTarget target, CliTypeIdentity arrayType)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = new StagedValueArrayEmitter(layouts, layouts, layouts, imports,
            new ImplicitExceptionEmitter(layouts), new AddressInstructionEmitter(layouts));
        var code = new RecordingInstructionWriter();
        emitter.Emit(code, arrayType, Address, Length, Destination, Temporary, OptionalFunctionIndex.At(9));
        return (code.ToInstructions(), imports);
    }
}
