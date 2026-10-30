using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ArrayCopyClearIntrinsicEmitterTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void ClonePassesTheFullRangeSentinelAndPublishesTheResult(
        bool memory64,
        int argumentBase)
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = ThroughContract(new ArrayCloneIntrinsicEmitter(imports));
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.ArrayClone,
            [
                .. Enumerable.Repeat(CliValueKind.I4, argumentBase),
                CliValueKind.ManagedReference,
            ]);
        request = request with
        {
            Target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32,
            Call = request.Call with { ArgumentBase = argumentBase, Consumed = 1 },
        };
        var writer = Assert.IsType<RecordingInstructionWriter>(GetCodeWriter(request.Instruction));

        emitter.Emit(request, writer);

        var instructions = writer.ToInstructions();
        Assert.Equal(
            [
                WasmOpcodes.LocalGet,
                WasmOpcodes.I32Constant,
                WasmOpcodes.I32Constant,
                WasmOpcodes.Call,
                WasmOpcodes.LocalSet,
            ],
            instructions.Select(instruction => instruction.Opcode));
        var sourceLocal = (uint)((memory64
            ? request.Instruction.Context.StackLocals.I8Base
            : request.Instruction.Context.StackLocals.I4Base) + argumentBase);
        Assert.Equal(sourceLocal, instructions[0].Operand.UnsignedValue);
        Assert.Equal(0, instructions[1].Operand.SignedValue);
        Assert.Equal(-1, instructions[2].Operand.SignedValue);
        Assert.Equal(
            (uint)imports.Resolve(
                RuntimeImportSymbol.ArrayClone,
                request.Instruction.Target.RuntimeImportSelection),
            instructions[3].Operand.UnsignedValue);
        Assert.Equal(sourceLocal, instructions[4].Operand.UnsignedValue);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void CloneRangePreservesTheArrayAndBoundsAndPublishesTheResult(
        bool memory64,
        int argumentBase)
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = ThroughContract(new ArrayCloneRangeIntrinsicEmitter(imports));
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.ArrayCloneRange,
            [
                .. Enumerable.Repeat(CliValueKind.I4, argumentBase),
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.I4,
            ]);
        request = request with
        {
            Target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32,
            Call = request.Call with { ArgumentBase = argumentBase, Consumed = 3 },
        };
        var writer = Assert.IsType<RecordingInstructionWriter>(GetCodeWriter(request.Instruction));

        emitter.Emit(request, writer);

        var instructions = writer.ToInstructions();
        Assert.Equal(
            [
                WasmOpcodes.LocalGet,
                WasmOpcodes.LocalGet,
                WasmOpcodes.LocalGet,
                WasmOpcodes.Call,
                WasmOpcodes.LocalSet,
            ],
            instructions.Select(instruction => instruction.Opcode));
        var locals = request.Instruction.Context.StackLocals;
        var sourceLocal = (uint)((memory64 ? locals.I8Base : locals.I4Base) + argumentBase);
        Assert.Equal(sourceLocal, instructions[0].Operand.UnsignedValue);
        Assert.Equal((uint)(locals.I4Base + argumentBase + 1), instructions[1].Operand.UnsignedValue);
        Assert.Equal((uint)(locals.I4Base + argumentBase + 2), instructions[2].Operand.UnsignedValue);
        Assert.Equal(
            (uint)imports.Resolve(
                RuntimeImportSymbol.ArrayClone,
                request.Instruction.Target.RuntimeImportSelection),
            instructions[3].Operand.UnsignedValue);
        Assert.Equal(sourceLocal, instructions[4].Operand.UnsignedValue);
    }

    [Fact]
    public void CopyLoadsEveryArgumentCallsRuntimeAndPublishesTheResult()
    {
        var emitter = ThroughContract(new ArrayCopyIntrinsicEmitter(
            WasmRuntimeImports.CreateCatalog()));
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.ArrayCopy,
            [
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.I4,
            ]);

        emitter.Emit(request, GetCodeWriter(request.Instruction));

        var code = GetCodeBytes(request.Instruction);
        Assert.Equal(5, code.Count(value => value == WasmOpcodes.LocalGet));
        Assert.Contains(WasmOpcodes.Call, code);
        Assert.Contains(WasmOpcodes.LocalSet, code);
    }

    [Fact]
    public void ClearLoadsEveryArgumentAndCallsRuntime()
    {
        var emitter = ThroughContract(new ArrayClearIntrinsicEmitter(
            WasmRuntimeImports.CreateCatalog()));
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.ArrayClear,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4]);

        emitter.Emit(request, GetCodeWriter(request.Instruction));

        var code = GetCodeBytes(request.Instruction);
        Assert.Equal(3, code.Count(value => value == WasmOpcodes.LocalGet));
        Assert.Contains(WasmOpcodes.Call, code);
        Assert.DoesNotContain(WasmOpcodes.LocalSet, code);
    }

    private static IRuntimeIntrinsicEmitter ThroughContract(
        IRuntimeIntrinsicEmitter emitter) => emitter;
}
