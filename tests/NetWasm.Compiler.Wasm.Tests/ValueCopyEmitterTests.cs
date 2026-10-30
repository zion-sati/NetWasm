using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ValueCopyEmitterTests
{
    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    public void FallbackReceivesUnmodifiedRequestOnlyForNonInlineCopies(int size)
    {
        var fallback = new RecordingCopyEmitter();
        var emitter = new SmallValueCopyEmitter(fallback);
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var sourceCalls = 0;
        Action<IWasmInstructionWriter> source = _ => sourceCalls++;

        emitter.Emit(code, 7, size, 4, source);

        if (size == 16)
        {
            Assert.Empty(fallback.Requests);
            Assert.Equal(1, sourceCalls);
            Assert.Equal(1, code.ToInstructions().Count(instruction => IsVector(instruction, WasmSimdOpcodes.V128Store)));
        }
        else
        {
            var request = Assert.Single(fallback.Requests);
            Assert.Same(code, request.Code);
            Assert.Equal(7, request.Destination);
            Assert.Equal(size, request.Size);
            Assert.Equal(4, request.Alignment);
            Assert.Same(source, request.Source);
            Assert.Equal(0, sourceCalls);
            Assert.Empty(code.ToInstructions());
        }
    }

    // Where the method inlines small blocks, other sizes of up to 64 bytes copy
    // in exact chunks rather than through memory.copy, every chunk loaded
    // before any is stored.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InlineCopiesOfOtherSizesUseExactChunks(bool inline)
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateSmall(layouts);
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var context = EmitterTestSupport.CreateMethodEmissionContext() with
        {
            InlineSmallBlocks = inline,
            CopyDestinationTemporary = 30,
            CopySourceTemporary = 31,
        };
        WriteLocalGet(code, 7);

        emitter.Emit(code, 7, 29, 1, writer => WriteLocalGet(writer, 8), context);

        var instructions = code.ToInstructions();
        if (!inline)
        {
            Assert.Contains(instructions, IsMemoryCopy);
            return;
        }
        Assert.DoesNotContain(instructions, IsMemoryCopy);
        var stores = instructions.Where(instruction => IsStore(instruction) ||
            instruction.Opcode == WasmOpcodes.SimdPrefixed && instruction.Operand.Alignment == WasmSimdOpcodes.V128Store).ToList();
        Assert.Equal([28U, 24U, 16U, 0U],
            stores.Select(store => store.Opcode == WasmOpcodes.SimdPrefixed ? store.Operand.Third : store.Operand.Offset));
        Assert.Equal([WasmOpcodes.I32Store8, WasmOpcodes.I32Store, WasmOpcodes.I64Store, WasmOpcodes.SimdPrefixed],
            stores.Select(store => store.Opcode));
    }

    // One 16-byte load and store, so a vector value that SIMD code stored
    // whole forwards whole to the copy, and a copy forwards whole to SIMD code.
    [Theory]
    [InlineData(1, 0U)]
    [InlineData(2, 1U)]
    [InlineData(4, 2U)]
    [InlineData(8, 3U)]
    [InlineData(16, 4U)]
    public void SixteenByteCopiesUseOneVectorLoadAndStore(
        int alignment,
        uint encodedAlignment)
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateSmall(layouts);
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var sourceCalls = 0;
        WriteLocalGet(code, 7);

        emitter.Emit(code, 7, 16, alignment, writer =>
        {
            sourceCalls++;
            WriteLocalGet(writer, 8);
        });

        Assert.Equal(1, sourceCalls);
        Assert.Equal(
            new[]
            {
                WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(7)),
                WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(8)),
                WasmInstruction.WithOperand(WasmOpcodes.SimdPrefixed,
                    WasmInstructionOperand.PrefixedTriple(WasmSimdOpcodes.V128Load, encodedAlignment, 0)),
                WasmInstruction.WithOperand(WasmOpcodes.SimdPrefixed,
                    WasmInstructionOperand.PrefixedTriple(WasmSimdOpcodes.V128Store, encodedAlignment, 0)),
            },
            code.ToInstructions());
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 15)]
    [InlineData(false, 17)]
    [InlineData(true, 1)]
    [InlineData(true, 15)]
    [InlineData(true, 17)]
    public void OtherCopiesDelegateToBulkMemoryCopy(
        bool memory64,
        int size)
    {
        var layouts = new RecordingLayoutProvider(
            memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32);
        var emitter = CreateSmall(layouts);
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var sourceCalls = 0;
        WriteLocalGet(code, 5);

        emitter.Emit(code, 5, size, 1, writer =>
        {
            sourceCalls++;
            WriteLocalGet(writer, 6);
        });

        Assert.Equal(1, sourceCalls);
        Assert.Single(code.ToInstructions(), IsMemoryCopy);
        Assert.DoesNotContain(code.ToInstructions(), IsLoad);
        Assert.DoesNotContain(code.ToInstructions(), IsStore);
        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == (memory64
                ? WasmOpcodes.I64Constant
                : WasmOpcodes.I32Constant));
    }

    [Fact]
    public void InvalidRequestsFailBeforeWritingOrCallingSource()
    {
        var layouts = new RecordingLayoutProvider();
        var small = CreateSmall(layouts);
        var bulk = new BulkValueCopyEmitter(layouts);
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var sourceCalls = 0;
        Action<IWasmInstructionWriter> source =
            _ => sourceCalls++;

        Assert.Throws<ArgumentNullException>(() =>
            small.Emit(null!, 0, 1, 1, source));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            small.Emit(code, -1, 1, 1, source));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            small.Emit(code, 0, 0, 1, source));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            small.Emit(code, 0, 1, 0, source));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            small.Emit(code, 0, 1, 32, source));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            small.Emit(code, 0, 1, 3, source));
        Assert.Throws<ArgumentNullException>(() =>
            small.Emit(code, 0, 1, 1, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            bulk.Emit(code, 0, 0, 1, source));

        Assert.Equal(0, sourceCalls);
        Assert.Empty(code.ToInstructions());
    }

    private static SmallValueCopyEmitter CreateSmall(
        RecordingLayoutProvider layouts) =>
        new SmallValueCopyEmitter(new BulkValueCopyEmitter(layouts));

    private static void WriteLocalGet(
        IWasmInstructionWriter code,
        int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static bool IsLoad(WasmInstruction instruction) =>
        instruction.Opcode is WasmOpcodes.I64Load or WasmOpcodes.I32Load or
            WasmOpcodes.I32Load16Unsigned or WasmOpcodes.I32Load8Unsigned;

    private static bool IsStore(WasmInstruction instruction) =>
        instruction.Opcode is WasmOpcodes.I64Store or WasmOpcodes.I32Store or
            WasmOpcodes.I32Store16 or WasmOpcodes.I32Store8;

    private static bool IsVector(WasmInstruction instruction, uint opcode) =>
        instruction.Opcode == WasmOpcodes.SimdPrefixed &&
        instruction.Operand.Alignment == opcode;

    private static bool IsMemoryCopy(WasmInstruction instruction) =>
        instruction.Opcode == WasmOpcodes.Prefixed &&
        instruction.Operand.Alignment == WasmOpcodes.MemoryCopy;

    private sealed class RecordingCopyEmitter : IValueCopyEmitter
    {
        public List<(IWasmInstructionWriter Code, int Destination, int Size,
            int Alignment, Action<IWasmInstructionWriter> Source)> Requests
        { get; } = [];

        public void Emit(
            IWasmInstructionWriter code,
            int destinationLocal,
            int size,
            int alignment,
            Action<IWasmInstructionWriter> emitSource,
            MethodEmissionContext? inlineContext = null) =>
            Requests.Add((code, destinationLocal, size, alignment, emitSource));
    }
}
