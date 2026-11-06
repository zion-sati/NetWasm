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

    // Vector values fill a v128 whatever their storage: Vector4 moves 16 bytes,
    // Vector2 reads its 8 into the low lanes and writes only those back.
    [Fact]
    public void VectorValuesMoveTheirStorageWidthThroughAV128()
    {
        var assembly = new AssemblyIdentity("NetWasm.CoreLib");
        var wide = CliTypeIdentity.Named(assembly, "System.Numerics", "Vector4", isValueType: true);
        var narrow = CliTypeIdentity.Named(assembly, "System.Numerics", "Vector2", isValueType: true);
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        ManagedMemoryEmitter.EmitLoadByType(code, WasmTargetLayout.Wasm32, 4, wide, 16);
        ManagedMemoryEmitter.EmitStoreByType(code, WasmTargetLayout.Wasm32, 4, wide, 16);
        ManagedMemoryEmitter.EmitLoadByType(code, WasmTargetLayout.Wasm32, 8, narrow, 8);
        ManagedMemoryEmitter.EmitStoreByType(code, WasmTargetLayout.Wasm32, 8, narrow, 8);

        Assert.Equal(
            [
                0xfd, 0x00, 0x03, 0x04, 0xfd, 0x0b, 0x03, 0x04,
                0xfd, 0x5d, 0x03, 0x08, 0xfd, 0x1d, 0x00, 0x37, 0x03, 0x08,
            ],
            code.ToArray());
        Assert.Equal(WasmValueType.V128, ManagedMemoryEmitter.GetLoadValueType(narrow, 8));
        Assert.Throws<CompilerException>(() => ManagedMemoryEmitter.EmitVectorLoad(code, 0, 4));
        Assert.Throws<CompilerException>(() => ManagedMemoryEmitter.EmitVectorStore(code, 0, 4));
    }

    // Vector3 reads its 12 bytes into the three low lanes and writes only those
    // back, as its low 8 bytes and its third lane, each at the address the
    // scratch local keeps; a store keeps the value in the other scratch local.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void Vector3MovesItsLowEightBytesAndThirdLaneThroughScratchLocals(WasmTarget target)
    {
        var vector = CliTypeIdentity.Named(new AssemblyIdentity("NetWasm.CoreLib"), "System.Numerics", "Vector3", isValueType: true);
        var layout = WasmTargetLayout.For(target);
        var scratch = new VectorScratch(5, 6);
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        ManagedMemoryEmitter.EmitLoadByType(code, layout, 4, vector, 12, scratch);
        ManagedMemoryEmitter.EmitStoreByType(code, layout, 200, vector, 12, scratch);

        Assert.Equal(CliValueKind.V128, vector.StackKind);
        Assert.Equal(
            [
                0x21, 0x05, 0x20, 0x05, 0x20, 0x05,
                0xfd, 0x5d, 0x03, 0x04,
                0xfd, 0x56, 0x02, 0x0c, 0x02,
                0x21, 0x06, 0x21, 0x05, 0x20, 0x05, 0x20, 0x06,
                0xfd, 0x5b, 0x03, 0xc8, 0x01, 0x00,
                0x20, 0x05, 0x20, 0x06,
                0xfd, 0x5a, 0x02, 0xd0, 0x01, 0x02,
            ],
            code.ToArray());
    }

    // Wasm has no dup, so a 12-byte access without scratch locals fails at once
    // and writes nothing.
    [Fact]
    public void ATwelveByteVectorAccessWithoutScratchLocalsIsRejected()
    {
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        var load = Assert.Throws<CompilerException>(() => ManagedMemoryEmitter.EmitVectorLoad(code, 0, 12));
        var store = Assert.Throws<CompilerException>(() => ManagedMemoryEmitter.EmitVectorStore(code, 0, 12));

        Assert.Equal(DiagnosticCode.RuntimeContract, load.Diagnostic.Code);
        Assert.Contains("load", load.Diagnostic.Message);
        Assert.Contains("store", store.Diagnostic.Message);
        Assert.Empty(code.ToArray());
    }

    [Fact]
    public void Memory64UsesI64ForAddressesAndManagedReferences()
    {
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        ManagedMemoryEmitter.EmitRootSlotStore(
            code, WasmTargetLayout.Wasm64, rootFrameLocal: 0, slot: 1, valueLocal: 2);
        ManagedMemoryEmitter.EmitArrayElementAddress(
            code, WasmTargetLayout.Wasm64, arrayLocal: 3, indexLocal: 4, inlineData: true, elementSize: 8);
        ManagedMemoryEmitter.EmitLoadBySize(code, WasmTargetLayout.Wasm64, 0, 8);
        ManagedMemoryEmitter.EmitStoreBySize(code, WasmTargetLayout.Wasm64, 0, 8);

        Assert.Equal(
            [
                0x20, 0x00, 0x42, 0x08, 0x7c, 0x20, 0x02, 0x37, 0x03, 0x00,
                // The elements sit inline, 32 bytes into the array.
                0x20, 0x03, 0x42, 0x20, 0x7c, 0x20, 0x04, 0xad, 0x42, 0x08,
                0x7e, 0x7c, 0x29, 0x03, 0x00, 0x37, 0x03, 0x00,
            ],
            code.ToArray());
    }

    // Without inline array data an element's address starts from the data
    // pointer every array keeps.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithoutInlineArrayDataTheDataPointerIsLoaded(bool memory64)
    {
        var target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32;
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        ManagedMemoryEmitter.EmitArrayElementAddress(code, target, arrayLocal: 2, indexLocal: 3, inlineData: false);

        var load = code.ToInstructions()[1];
        Assert.Equal(memory64 ? WasmOpcodes.I64Load : WasmOpcodes.I32Load, load.Opcode);
        Assert.Equal((uint)(memory64 ? 16 : 8), load.Operand!.Offset);
    }

    [Fact]
    public void Memory32UsesI32ForRootAndArrayAddresses()
    {
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        ManagedMemoryEmitter.EmitRootSlotStore(
            code, WasmTargetLayout.Wasm32, rootFrameLocal: 0, slot: 0, valueLocal: 1);
        ManagedMemoryEmitter.EmitArrayElementAddress(
            code, WasmTargetLayout.Wasm32, arrayLocal: 2, indexLocal: 3, inlineData: true, elementSize: 4);

        // The elements sit inline, 16 bytes into the array, so no load finds them.
        var instructions = code.ToInstructions();
        Assert.DoesNotContain(instructions, static instruction => instruction.Opcode == WasmOpcodes.I32Load);
        Assert.Contains(instructions, static instruction =>
            instruction.Opcode == WasmOpcodes.I32Constant && instruction.Operand?.SignedValue == 16);
        var bytes = code.ToArray();
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

    // A 16-byte value is a v128, moved with one SIMD load or store.
    [Fact]
    public void SixteenByteLoadsAndStoresAreV128Instructions()
    {
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        ManagedMemoryEmitter.EmitLoadBySize(code, WasmTargetLayout.Wasm32, 32, 16);
        ManagedMemoryEmitter.EmitStoreBySize(code, WasmTargetLayout.Wasm32, 48, 16);

        Assert.Equal([0xfd, 0x00, 0x03, 0x20, 0xfd, 0x0b, 0x03, 0x30], code.ToArray());
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
