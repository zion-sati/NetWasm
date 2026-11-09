using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;
using NetWasm.Compiler.Wasm.Emission.Planning;

public sealed class NativeMemoryIntrinsicEmitterTests
{
    // At the speed presets small copies run inline: past 128 bytes the copy is
    // memory.copy, and below it every case loads all of its chunks before it
    // stores any, so overlapping blocks copy as memmove does.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InlineCopiesLoadEveryChunkBeforeStoringAndFallBackPast128Bytes(bool memory64)
    {
        var instructions = EmitInline(RuntimeIntrinsic.NativeMemoryCopy, memory64,
            [CliValueKind.NativeInt, CliValueKind.NativeInt, CliValueKind.NativeInt]);

        Assert.Equal(WasmOpcodes.If, instructions[3].Opcode);
        Assert.Equal(129, memory64 ? instructions[1].Operand.Signed64Value : instructions[1].Operand.SignedValue);
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.Prefixed &&
            instruction.Operand.Alignment == WasmOpcodes.MemoryCopy);
        Assert.Contains(instructions, instruction => IsVector(instruction, WasmSimdOpcodes.V128Store));
        var segments = new List<List<WasmInstruction>> { new() };
        foreach (var instruction in instructions)
        {
            if (instruction.Opcode is WasmOpcodes.If or WasmOpcodes.Else or WasmOpcodes.End) segments.Add([]);
            else segments[^1].Add(instruction);
        }
        foreach (var segment in segments)
        {
            var lastLoad = segment.FindLastIndex(IsLoad);
            var firstStore = segment.FindIndex(IsStore);
            Assert.True(firstStore < 0 || lastLoad < firstStore);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InlineFillsStoreTheRepeatedValueAndFallBackPast128Bytes(bool memory64)
    {
        var instructions = EmitInline(RuntimeIntrinsic.NativeMemoryFill, memory64,
            [CliValueKind.NativeInt, CliValueKind.NativeInt, CliValueKind.I4]);

        Assert.Equal(WasmOpcodes.If, instructions[3].Opcode);
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.Prefixed &&
            instruction.Operand.Alignment == WasmOpcodes.MemoryFill);
        Assert.Contains(instructions, instruction => IsVector(instruction, WasmSimdOpcodes.I8x16Splat));
        Assert.Contains(instructions, instruction => IsVector(instruction, WasmSimdOpcodes.V128Store));
    }

    // Known-size value copies inline the same way, in exact chunks widest
    // first with every load before any store, up to 128 bytes; they stay
    // memory.copy past that, outside the speed presets, or where the method
    // has no scratch.
    [Theory]
    [InlineData(29, true, true, "28,24,16,0")]
    [InlineData(29, false, true, "")]
    [InlineData(29, true, false, "")]
    [InlineData(65, true, true, "64,48,32,16,0")]
    [InlineData(128, true, true, "112,96,80,64,48,32,16,0")]
    [InlineData(129, true, true, "")]
    public void KnownSizeCopiesLoadEveryChunkBeforeStoring(int size, bool inline, bool scratch, string storeOffsets)
    {
        var inlined = storeOffsets.Length > 0;
        var context = CreateMethodEmissionContext() with
        {
            InlineSmallBlocks = inline,
            CopyDestinationTemporary = scratch ? 30 : -1,
            CopySourceTemporary = scratch ? 31 : -1,
        };
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        SmallBlockEmitter.EmitKnownSizeCopy(writer, context, size, 16, code => code.Write(
            WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(size))));

        List<WasmInstruction> instructions = [.. writer.ToInstructions()];
        Assert.Equal(!inlined, instructions.Exists(instruction => instruction.Opcode == WasmOpcodes.Prefixed &&
            instruction.Operand.Alignment == WasmOpcodes.MemoryCopy));
        if (!inlined)
        {
            return;
        }
        Assert.True(instructions.FindLastIndex(IsLoad) < instructions.FindIndex(IsStore));
        Assert.Equal(storeOffsets.Split(',').Select(uint.Parse), instructions.Where(IsStore).Select(store =>
            store.Opcode == WasmOpcodes.SimdPrefixed ? store.Operand.Third : store.Operand.Offset));
    }

    // Where at most four chunks of the widest chunk cover the value, its copy reads
    // no chunk wider than that: Span<int> on wasm32 (8 bytes, widest chunk 4)
    // copies as two i32 chunks, not one i64. A value needing more keeps the widest.
    [Theory]
    [InlineData(8, 4, "4:i32,0:i32")]
    [InlineData(8, 8, "0:i64")]
    [InlineData(12, 4, "8:i32,4:i32,0:i32")]
    [InlineData(16, 4, "12:i32,8:i32,4:i32,0:i32")]
    [InlineData(16, 8, "8:i64,0:i64")]
    [InlineData(16, 16, "0:v128")]
    [InlineData(6, 2, "4:i16,2:i16,0:i16")]
    [InlineData(3, 1, "2:i8,1:i8,0:i8")]
    [InlineData(4, 1, "3:i8,2:i8,1:i8,0:i8")]
    [InlineData(32, 8, "24:i64,16:i64,8:i64,0:i64")]
    [InlineData(5, 1, "4:i8,0:i32")]
    [InlineData(8, 1, "0:i64")]
    [InlineData(16, 1, "0:v128")]
    [InlineData(10, 2, "8:i16,0:i64")]
    [InlineData(40, 8, "32:i64,16:v128,0:v128")]
    [InlineData(24, 32, "16:i64,0:v128")]
    [InlineData(17, 1, "16:i8,0:v128")]
    [InlineData(24, 4, "16:i64,0:v128")]
    public void KnownSizeCopiesReadNoChunkWiderThanTheWidestChunkWhereFourCoverIt(int size, int widestChunk, string stores)
    {
        var context = CreateMethodEmissionContext() with
        {
            InlineSmallBlocks = true,
            CopyDestinationTemporary = 30,
            CopySourceTemporary = 31,
        };
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        SmallBlockEmitter.EmitKnownSizeCopy(writer, context, size, widestChunk, code => code.Write(
            WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(size))));

        List<WasmInstruction> instructions = [.. writer.ToInstructions()];
        Assert.True(instructions.FindLastIndex(IsLoad) < instructions.FindIndex(IsStore));
        Assert.Equal(stores, string.Join(",", instructions.Where(IsStore).Select(store => store.Opcode switch
        {
            WasmOpcodes.SimdPrefixed => $"{store.Operand.Third}:v128",
            WasmOpcodes.I64Store => $"{store.Operand.Offset}:i64",
            WasmOpcodes.I32Store => $"{store.Operand.Offset}:i32",
            WasmOpcodes.I32Store16 => $"{store.Operand.Offset}:i16",
            _ => $"{store.Operand.Offset}:i8",
        })));
        Assert.Equal(instructions.Count(IsStore), instructions.Count(IsLoad));
    }

    // A value holding a byref (a span) is built field by field in frames, so its
    // copies read no chunk wider than its alignment; any other value's read 16.
    [Theory]
    [InlineData(4, true, 4)]
    [InlineData(8, true, 8)]
    [InlineData(4, false, 16)]
    [InlineData(1, false, 16)]
    public void TheWidestCopyChunkIsTheAlignmentOnlyForValuesHoldingAByref(int alignment, bool holdsByReference, int widest)
    {
        var layout = new ValueLayout(CliTypeIdentity.Primitive("i4", CliValueKind.I4), 8, alignment, [])
        {
            ByReferenceOffsets = holdsByReference ? [0] : [],
        };

        Assert.Equal(widest, SmallBlockEmitter.WidestChunk(layout));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-4)]
    public void AChunkWidthThatIsNotAPositivePowerOfTwoIsRejectedBeforeAnyCodeIsWritten(int alignment)
    {
        var context = CreateMethodEmissionContext() with
        {
            InlineSmallBlocks = true,
            CopyDestinationTemporary = 30,
            CopySourceTemporary = 31,
        };
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        Assert.Throws<ArgumentOutOfRangeException>(() => SmallBlockEmitter.EmitInlineKnownSizeCopy(writer, context, 8, alignment));
        Assert.Empty(writer.ToInstructions());
    }

    private static List<WasmInstruction> EmitInline(RuntimeIntrinsic intrinsic, bool memory64, CliValueKind[] stack)
    {
        var request = CreateBulkRequest(intrinsic, memory64, stack);
        var call = request.Call with
        {
            Instruction = request.Call.Instruction with
            {
                Target = request.Call.Instruction.Target with { InlineSmallBlockOperations = true },
            },
        };
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(intrinsic, WasmRuntimeImports.CreateCatalog());
        var writer = new EmitterTestSupport.RecordingInstructionWriter();
        emitter.Emit(request with { Call = call }, writer);
        return [.. writer.ToInstructions()];
    }

    // Prefixed operands hold a sub-opcode alone, or first in a triple.
    private static bool IsVector(WasmInstruction instruction, uint opcode) =>
        instruction.Opcode == WasmOpcodes.SimdPrefixed &&
        (instruction.Operand.Shape == WasmInstructionOperandShape.PrefixedUnsignedLeb128
            ? instruction.Operand.UnsignedValue
            : instruction.Operand.Alignment) == opcode;

    private static bool IsLoad(WasmInstruction instruction) =>
        IsVector(instruction, WasmSimdOpcodes.V128Load) || instruction.Opcode is WasmOpcodes.I64Load or
            WasmOpcodes.I32Load or WasmOpcodes.I32Load16Unsigned or WasmOpcodes.I32Load8Unsigned;

    private static bool IsStore(WasmInstruction instruction) =>
        IsVector(instruction, WasmSimdOpcodes.V128Store) || instruction.Opcode is WasmOpcodes.I64Store or
            WasmOpcodes.I32Store or WasmOpcodes.I32Store16 or WasmOpcodes.I32Store8;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearEmitsZeroValuedBulkMemoryFill(bool memory64)
    {
        var request = CreateBulkRequest(
            RuntimeIntrinsic.NativeMemoryClear,
            memory64,
            [CliValueKind.NativeInt, CliValueKind.NativeInt]);
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(
            RuntimeIntrinsic.NativeMemoryClear,
            WasmRuntimeImports.CreateCatalog());
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Emit(request, writer);

        var instructions = writer.ToInstructions();
        Assert.Equal(
            [WasmOpcodes.LocalGet, WasmOpcodes.I32Constant,
                WasmOpcodes.LocalGet, WasmOpcodes.Prefixed],
            instructions.Select(instruction => instruction.Opcode));
        Assert.Equal(
            (uint)request.Local(0, CliValueKind.NativeInt),
            instructions[0].Operand.UnsignedValue);
        Assert.Equal(WasmInstructionOperand.Signed(0), instructions[1].Operand);
        Assert.Equal(
            (uint)request.Local(1, CliValueKind.NativeInt),
            instructions[2].Operand.UnsignedValue);
        Assert.Equal(
            WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0),
            instructions[3].Operand);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyEmitsDestinationSourceAndCountBulkMemoryCopy(bool memory64)
    {
        var request = CreateBulkRequest(
            RuntimeIntrinsic.NativeMemoryCopy,
            memory64,
            [CliValueKind.NativeInt, CliValueKind.NativeInt,
                CliValueKind.NativeInt]);
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(
            RuntimeIntrinsic.NativeMemoryCopy,
            WasmRuntimeImports.CreateCatalog());
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Emit(request, writer);

        var instructions = writer.ToInstructions();
        Assert.Equal(
            [WasmOpcodes.LocalGet, WasmOpcodes.LocalGet,
                WasmOpcodes.LocalGet, WasmOpcodes.Prefixed],
            instructions.Select(instruction => instruction.Opcode));
        Assert.Equal(
            (uint)request.Local(1, CliValueKind.NativeInt),
            instructions[0].Operand.UnsignedValue);
        Assert.Equal(
            (uint)request.Local(0, CliValueKind.NativeInt),
            instructions[1].Operand.UnsignedValue);
        Assert.Equal(
            (uint)request.Local(2, CliValueKind.NativeInt),
            instructions[2].Operand.UnsignedValue);
        Assert.Equal(
            WasmInstructionOperand.PrefixedTriple(
                WasmOpcodes.MemoryCopy,
                0,
                0),
            instructions[3].Operand);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FillEmitsPointerValueAndCountBulkMemoryFill(bool memory64)
    {
        var request = CreateBulkRequest(
            RuntimeIntrinsic.NativeMemoryFill,
            memory64,
            [CliValueKind.NativeInt, CliValueKind.NativeInt, CliValueKind.I4]);
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(
            RuntimeIntrinsic.NativeMemoryFill,
            WasmRuntimeImports.CreateCatalog());
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Emit(request, writer);

        var instructions = writer.ToInstructions();
        Assert.Equal(
            [WasmOpcodes.LocalGet, WasmOpcodes.LocalGet,
                WasmOpcodes.LocalGet, WasmOpcodes.Prefixed],
            instructions.Select(instruction => instruction.Opcode));
        Assert.Equal(
            (uint)request.Local(0, CliValueKind.NativeInt),
            instructions[0].Operand.UnsignedValue);
        Assert.Equal(
            (uint)request.Local(2, CliValueKind.I4),
            instructions[1].Operand.UnsignedValue);
        Assert.Equal(
            (uint)request.Local(1, CliValueKind.NativeInt),
            instructions[2].Operand.UnsignedValue);
        Assert.Equal(
            WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0),
            instructions[3].Operand);
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.NativeMemoryAlloc, (int)RuntimeImportSymbol.NativeAlloc, 1)]
    [InlineData(RuntimeIntrinsic.NativeMemoryRealloc, (int)RuntimeImportSymbol.NativeRealloc, 2)]
    [InlineData(RuntimeIntrinsic.NativeMemoryAlignedAlloc, (int)RuntimeImportSymbol.NativeAlignedAlloc, 2)]
    [InlineData(RuntimeIntrinsic.NativeMemoryAlignedRealloc, (int)RuntimeImportSymbol.NativeAlignedRealloc, 3)]
    public void AllocationStrategiesDelegateTheirExactRuntimeContract(
        RuntimeIntrinsic intrinsic,
        int symbolValue,
        int argumentCount)
    {
        var allocations = new RecordingAllocationEmitter();
        var request = CreateRuntimeIntrinsicRequest(
            intrinsic,
            Enumerable.Repeat(CliValueKind.NativeInt, argumentCount));
        var emitter = CreateAllocationStrategy(intrinsic, allocations);

        emitter.Emit(request, GetCodeWriter(request.Instruction));

        Assert.Equal((RuntimeImportSymbol)symbolValue, allocations.Symbol);
        Assert.Equal(argumentCount, allocations.ArgumentCount);
        Assert.Same(request, allocations.Request);
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.NativeMemoryFree, (int)RuntimeImportSymbol.NativeFree)]
    [InlineData(RuntimeIntrinsic.NativeMemoryAlignedFree, (int)RuntimeImportSymbol.NativeAlignedFree)]
    public void ReleaseStrategiesDelegateTheirExactRuntimeContract(
        RuntimeIntrinsic intrinsic,
        int symbolValue)
    {
        var releases = new RecordingReleaseEmitter();
        var request = CreateRuntimeIntrinsicRequest(
            intrinsic,
            [CliValueKind.NativeInt]);
        IRuntimeIntrinsicEmitter emitter = intrinsic == RuntimeIntrinsic.NativeMemoryFree
            ? new NativeMemoryFreeIntrinsicEmitter(releases)
            : new NativeMemoryAlignedFreeIntrinsicEmitter(releases);

        emitter.Emit(request, GetCodeWriter(request.Instruction));

        Assert.Equal((RuntimeImportSymbol)symbolValue, releases.Symbol);
        Assert.Same(request, releases.Request);
    }

    [Theory]
    [InlineData(1, (int)RuntimeImportSymbol.NativeAlloc)]
    [InlineData(3, (int)RuntimeImportSymbol.NativeAlignedRealloc)]
    public void AllocationCapabilityCallsRuntimeAndChecksForOutOfMemory(
        int argumentCount,
        int symbolValue)
    {
        var addresses = new RecordingAddressEmitter();
        var exceptions = new RecordingExceptionEmitter();
        var emitter = ThroughAllocationContract(new NativeMemoryAllocationEmitter(
            WasmRuntimeImports.CreateCatalog(),
            addresses,
            exceptions));
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.NativeMemoryAlloc,
            Enumerable.Repeat(CliValueKind.NativeInt, argumentCount));

        emitter.Emit(
            request,
            GetCodeWriter(request.Instruction),
            (RuntimeImportSymbol)symbolValue,
            argumentCount);

        Assert.Equal([AddressOperation.EqualZero], addresses.Operations);
        Assert.Equal([ManagedExceptionKind.OutOfMemory], exceptions.Kinds);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request.Instruction));
    }

    [Fact]
    public void ReleaseCapabilityCallsRuntime()
    {
        var emitter = ThroughReleaseContract(new NativeMemoryReleaseEmitter(
            WasmRuntimeImports.CreateCatalog()));
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.NativeMemoryFree,
            [CliValueKind.NativeInt]);

        emitter.Emit(
            request,
            GetCodeWriter(request.Instruction),
            RuntimeImportSymbol.NativeFree);

        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request.Instruction));
    }

    private static INativeMemoryAllocationEmitter ThroughAllocationContract(
        INativeMemoryAllocationEmitter emitter) => emitter;

    private static RuntimeIntrinsicEmissionRequest CreateBulkRequest(
        RuntimeIntrinsic intrinsic,
        bool memory64,
        IReadOnlyList<CliValueKind> arguments)
    {
        var request = CreateRuntimeIntrinsicRequest(intrinsic, arguments);
        return request with
        {
            Target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32,
        };
    }

    private static INativeMemoryReleaseEmitter ThroughReleaseContract(
        INativeMemoryReleaseEmitter emitter) => emitter;

    private static IRuntimeIntrinsicEmitter CreateAllocationStrategy(
        RuntimeIntrinsic intrinsic,
        INativeMemoryAllocationEmitter allocations) => intrinsic switch
        {
            RuntimeIntrinsic.NativeMemoryAlloc =>
                new NativeMemoryAllocIntrinsicEmitter(allocations),
            RuntimeIntrinsic.NativeMemoryRealloc =>
                new NativeMemoryReallocIntrinsicEmitter(allocations),
            RuntimeIntrinsic.NativeMemoryAlignedAlloc =>
                new NativeMemoryAlignedAllocIntrinsicEmitter(allocations),
            RuntimeIntrinsic.NativeMemoryAlignedRealloc =>
                new NativeMemoryAlignedReallocIntrinsicEmitter(allocations),
            _ => throw new ArgumentOutOfRangeException(nameof(intrinsic)),
        };

    private sealed class RecordingAllocationEmitter : INativeMemoryAllocationEmitter
    {
        public RuntimeIntrinsicEmissionRequest? Request { get; private set; }
        public RuntimeImportSymbol? Symbol { get; private set; }
        public int? ArgumentCount { get; private set; }

        public void Emit(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code,
            RuntimeImportSymbol symbol,
            int argumentCount)
        {
            Request = request;
            Symbol = symbol;
            ArgumentCount = argumentCount;
        }
    }

    private sealed class RecordingReleaseEmitter : INativeMemoryReleaseEmitter
    {
        public RuntimeIntrinsicEmissionRequest? Request { get; private set; }
        public RuntimeImportSymbol? Symbol { get; private set; }

        public void Emit(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code,
            RuntimeImportSymbol symbol)
        {
            Request = request;
            Symbol = symbol;
        }
    }

    private sealed class RecordingAddressEmitter : IAddressInstructionEmitter
    {
        public List<AddressOperation> Operations { get; } = [];

        public void Emit(IWasmInstructionWriter code, int constant) =>
            throw new NotSupportedException();

        public void Emit(IWasmInstructionWriter code, AddressOperation operation) =>
            Operations.Add(operation);
    }

    private sealed class RecordingExceptionEmitter : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind, OptionalFunctionIndex throwFunctionIndex) =>
            Kinds.Add(kind);
    }
}
