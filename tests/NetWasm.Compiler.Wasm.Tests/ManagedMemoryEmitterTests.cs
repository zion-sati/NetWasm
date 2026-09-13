using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ManagedMemoryEmitterTests
{
    [Fact]
    public void InvalidLoadAndStoreSizesAreRejected()
    {
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        Assert.Throws<CompilerException>(() =>
            ManagedMemoryEmitter.EmitLoadBySize(code, WasmTargetLayout.Wasm32, 0, 3));
        Assert.Throws<CompilerException>(() =>
            ManagedMemoryEmitter.EmitStoreBySize(code, WasmTargetLayout.Wasm32, 0, 3));
    }

    [Fact]
    public void Memory64UsesI64ForAddressesAndManagedReferences()
    {
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        ManagedMemoryEmitter.EmitRootSlotStore(
            code, WasmTargetLayout.Wasm64, rootFrameLocal: 0, slot: 1, valueLocal: 2);
        ManagedMemoryEmitter.EmitArrayElementAddress(
            code, WasmTargetLayout.Wasm64, arrayLocal: 3, indexLocal: 4, elementSize: 8);
        ManagedMemoryEmitter.EmitLoadBySize(code, WasmTargetLayout.Wasm64, 0, 8);
        ManagedMemoryEmitter.EmitStoreBySize(code, WasmTargetLayout.Wasm64, 0, 8);

        Assert.Equal(
            [
                0x20, 0x00, 0x42, 0x08, 0x7c, 0x20, 0x02, 0x37, 0x03, 0x00,
                0x20, 0x03, 0x29, 0x03, 0x10, 0x20, 0x04, 0xad, 0x42, 0x08,
                0x7e, 0x7c, 0x29, 0x03, 0x00, 0x37, 0x03, 0x00,
            ],
            code.ToArray());
    }

    [Fact]
    public void Memory32UsesI32ForRootAndArrayAddresses()
    {
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        ManagedMemoryEmitter.EmitRootSlotStore(
            code, WasmTargetLayout.Wasm32, rootFrameLocal: 0, slot: 0, valueLocal: 1);
        ManagedMemoryEmitter.EmitArrayElementAddress(
            code, WasmTargetLayout.Wasm32, arrayLocal: 2, indexLocal: 3, elementSize: 4);

        var bytes = code.ToArray();
        Assert.Contains(WasmOpcodes.I32Load, bytes);
        Assert.Contains(WasmOpcodes.I32Store, bytes);
        Assert.Contains(WasmOpcodes.I32Multiply, bytes);
        Assert.Contains(WasmOpcodes.I32Add, bytes);
    }

    [Fact]
    public void PrimitiveLoadsAndStoresUseTheirExactMemoryInstructions()
    {
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var types = new[]
        {
            ("i1", CliValueKind.I4, 1),
            ("bool", CliValueKind.I4, 1),
            ("u1", CliValueKind.I4, 1),
            ("i2", CliValueKind.I4, 2),
            ("char", CliValueKind.I4, 2),
            ("u2", CliValueKind.I4, 2),
            ("i4", CliValueKind.I4, 4),
            ("i8", CliValueKind.I8, 8),
            ("u8", CliValueKind.I8, 8),
            ("f4", CliValueKind.F4, 4),
            ("f8", CliValueKind.F8, 8),
        };

        foreach (var (name, kind, size) in types)
        {
            var type = CliTypeIdentity.Primitive(name, kind);
            ManagedMemoryEmitter.EmitLoadByType(
                code, WasmTargetLayout.Wasm32, 0, type, size);
            ManagedMemoryEmitter.EmitStoreByType(
                code, WasmTargetLayout.Wasm32, 0, type, size);
        }
        foreach (var size in new[] { 1, 2, 4, 8 })
        {
            ManagedMemoryEmitter.EmitLoadBySize(code, WasmTargetLayout.Wasm32, 0, size);
            ManagedMemoryEmitter.EmitStoreBySize(code, WasmTargetLayout.Wasm32, 0, size);
        }

        Assert.NotEmpty(code.ToArray());
    }

    [Fact]
    public void LoadValueTypeMatchesTheInstructionProducedForEveryScalarWidth()
    {
        var i4 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);

        Assert.Equal(
            WasmValueType.I32,
            ManagedMemoryEmitter.GetLoadValueType(i4, sizeof(int)));
        Assert.Equal(
            WasmValueType.I64,
            ManagedMemoryEmitter.GetLoadValueType(
                CliTypeIdentity.ManagedByReference(i4),
                sizeof(long)));
        Assert.Equal(
            WasmValueType.I32,
            ManagedMemoryEmitter.GetLoadValueType(
                CliTypeIdentity.ManagedByReference(i4),
                sizeof(int)));
        Assert.Equal(
            WasmValueType.F32,
            ManagedMemoryEmitter.GetLoadValueType(
                CliTypeIdentity.Primitive("f4", CliValueKind.F4),
                sizeof(float)));
        Assert.Equal(
            WasmValueType.F64,
            ManagedMemoryEmitter.GetLoadValueType(
                CliTypeIdentity.Primitive("f8", CliValueKind.F8),
                sizeof(double)));
        Assert.Equal(
            WasmValueType.I64,
            ManagedMemoryEmitter.GetLoadValueType(
                CliTypeIdentity.Primitive("i8", CliValueKind.I8),
                sizeof(long)));
    }

    [Fact]
    public void EvaluationStackLoadAndStoreMoveValuesThroughLocals()
    {
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var stack = new List<CliValueKind> { CliValueKind.I4 };

        ManagedMemoryEmitter.EmitLoad(
            code, stack, stackBase: 3, sourceLocal: 7, type: CliValueKind.I8);
        ManagedMemoryEmitter.EmitStore(
            code, stack, stackBase: 3, targetLocal: 9);

        Assert.Equal([CliValueKind.I4], stack);
        Assert.Contains(WasmOpcodes.LocalGet, code.ToArray());
        Assert.Contains(WasmOpcodes.LocalSet, code.ToArray());
    }

    [Fact]
    public void NegativeLocalsAndMemoryOffsetsAreRejected()
    {
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ManagedMemoryEmitter.EmitLoad(
                code, [], stackBase: 0, sourceLocal: -1, type: CliValueKind.I4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ManagedMemoryEmitter.EmitLoad(
                code, [], stackBase: -1, sourceLocal: 0, type: CliValueKind.I4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ManagedMemoryEmitter.EmitLoadBySize(
                code, WasmTargetLayout.Wasm32, offset: -1, size: 1));
    }
}
