using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ValueFrameInitializationEmitterTests
{
    [Theory]
    [InlineData(1, WasmOpcodes.I32Store8)]
    [InlineData(2, WasmOpcodes.I32Store16)]
    [InlineData(4, WasmOpcodes.I32Store)]
    [InlineData(8, WasmOpcodes.I64Store)]
    public void InitializesSingleLocalWithOneStore(
        int size,
        byte expectedStore)
    {
        var layouts = new RecordingLayoutProvider();
        var addresses = new RecordingValueFrameAddressEmitter();
        var emitter = CreateEmitter(
            layouts,
            addresses,
            new FixedValueLayoutProvider(size));
        var type = CliTypeIdentity.Named(
            Assembly,
            "Fixture",
            "Value",
            isValueType: true,
            CliValueKind.ValueType);
        var header = CreateHeader([type]);
        var context = CreateContext(
            new ValueFrameLayout(
                16,
                ImmutableDictionary<int, int>.Empty.Add(0, 3),
                [],
                [],
                []));
        var code = new RecordingInstructionWriter();

        emitter.Initialize(code, header, context);

        Assert.Equal([3], addresses.Offsets);
        Assert.Contains(expectedStore, code.ToArray());
        Assert.DoesNotContain(
            code.ToInstructions(),
            instruction => instruction.Opcode == WasmOpcodes.Prefixed);
        Assert.Equal(
            size == 8 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant,
            code.ToInstructions()[0].Opcode);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Constant)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Constant)]
    public void CoalescesLocalStorageAndLeavesScratchOutsideTheSpanUntouched(
        WasmTarget target,
        byte expectedLengthConstant)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var addresses = new RecordingValueFrameAddressEmitter();
        var emitter = CreateEmitter(layouts, addresses);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var context = CreateContext(new ValueFrameLayout(
            320,
            ImmutableDictionary<int, int>.Empty
                .Add(0, 4)
                .Add(1, 40)
                .Add(2, 300),
            [],
            ImmutableDictionary<int, int>.Empty.Add(17, 308),
            []));
        var code = new RecordingInstructionWriter();

        emitter.Initialize(
            code,
            CreateHeader([scalar, scalar, scalar]),
            context);

        Assert.Equal([4], addresses.Offsets);
        var instructions = code.ToInstructions();
        Assert.Contains(
            instructions,
            instruction => instruction.Opcode == WasmOpcodes.Prefixed &&
                instruction.Operand == WasmInstructionOperand.PrefixedPair(
                    WasmOpcodes.MemoryFill,
                    0));
        Assert.Contains(
            instructions,
            instruction => instruction.Opcode == expectedLengthConstant &&
                (target == WasmTarget.Wasm64
                    ? instruction.Operand.Signed64Value == 300
                    : instruction.Operand.SignedValue == 300));
    }

    [Theory]
    [InlineData(3, new[] { WasmOpcodes.I32Store16, WasmOpcodes.I32Store8 }, new uint[] { 0, 2 })]
    [InlineData(12, new[] { WasmOpcodes.I64Store, WasmOpcodes.I32Store }, new uint[] { 0, 8 })]
    [InlineData(15, new[] { WasmOpcodes.I64Store, WasmOpcodes.I32Store, WasmOpcodes.I32Store16, WasmOpcodes.I32Store8 },
        new uint[] { 0, 8, 12, 14 })]
    [InlineData(40, new[] { WasmOpcodes.SimdPrefixed, WasmOpcodes.SimdPrefixed, WasmOpcodes.I64Store },
        new uint[] { 0, 16, 32 })]
    // Matrix3x2.Add's inlined locals.
    [InlineData(168, new[] { WasmOpcodes.SimdPrefixed, WasmOpcodes.SimdPrefixed, WasmOpcodes.SimdPrefixed,
        WasmOpcodes.SimdPrefixed, WasmOpcodes.SimdPrefixed, WasmOpcodes.SimdPrefixed, WasmOpcodes.SimdPrefixed,
        WasmOpcodes.SimdPrefixed, WasmOpcodes.SimdPrefixed, WasmOpcodes.SimdPrefixed, WasmOpcodes.I64Store },
        new uint[] { 0, 16, 32, 48, 64, 80, 96, 112, 128, 144, 160 })]
    public void ZeroesRangesUpToTwoHundredFiftySixBytesWithStores(
        int size,
        byte[] expectedStores,
        uint[] expectedOffsets)
    {
        var layouts = new RecordingLayoutProvider();
        var addresses = new RecordingValueFrameAddressEmitter();
        var emitter = CreateEmitter(
            layouts,
            addresses,
            new FixedValueLayoutProvider(size));
        var type = CliTypeIdentity.Named(
            Assembly,
            "Fixture",
            "Value",
            isValueType: true,
            CliValueKind.ValueType);
        var context = CreateContext(
            new ValueFrameLayout(
                300,
                ImmutableDictionary<int, int>.Empty.Add(0, 16),
                [],
                [],
                []));
        var code = new RecordingInstructionWriter();

        emitter.Initialize(code, CreateHeader([type]), context);

        var instructions = code.ToInstructions();
        Assert.DoesNotContain(
            instructions,
            instruction => instruction.Opcode == WasmOpcodes.Prefixed);
        // A v128.store is SIMD-prefixed; its operand holds the opcode, then the
        // alignment, then the offset.
        var stores = instructions
            .Where(instruction => instruction.Opcode is WasmOpcodes.I64Store or WasmOpcodes.I32Store or
                WasmOpcodes.I32Store16 or WasmOpcodes.I32Store8 ||
                instruction.Opcode == WasmOpcodes.SimdPrefixed &&
                instruction.Operand.Shape == WasmInstructionOperandShape.PrefixedUnsignedLeb128Triple &&
                instruction.Operand.Alignment == WasmSimdOpcodes.V128Store)
            .ToArray();
        Assert.Equal(expectedStores, stores.Select(instruction => instruction.Opcode));
        Assert.Equal(expectedOffsets, stores.Select(instruction =>
            instruction.Opcode == WasmOpcodes.SimdPrefixed ? instruction.Operand.Third : instruction.Operand.Offset));
        Assert.All(addresses.Offsets, offset => Assert.Equal(16, offset));
        Assert.Equal(expectedStores.Length, addresses.Offsets.Count);
    }

    [Fact]
    public void InitializesAppendedFilterLocalsWithoutClearingArgumentsOrReusedValueLocals()
    {
        var layouts = new RecordingLayoutProvider();
        var addresses = new RecordingValueFrameAddressEmitter();
        var emitter = CreateEmitter(layouts, addresses);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var value = CliTypeIdentity.Named(
            Assembly,
            "Fixture",
            "Value",
            isValueType: true,
            CliValueKind.ValueType);
        var argument = new FilterCapture(new(true, 0), scalar, 20, []);
        var local = new FilterCapture(new(false, 0), scalar, 28, []);
        var reusedValueLocal = new FilterCapture(new(false, 1), value, 8, []);
        var context = CreateContext(
            new ValueFrameLayout(
                40,
                ImmutableDictionary<int, int>.Empty.Add(1, 8),
                [],
                [],
                []),
            new FilterEnvironmentLayout(
                40,
                0,
                16,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty
                    .Add(argument.Slot, argument)
                    .Add(local.Slot, local)
                    .Add(reusedValueLocal.Slot, reusedValueLocal)));
        var code = new RecordingInstructionWriter();

        emitter.Initialize(code, CreateHeader([scalar, value]), context);

        Assert.Equal([8, 28], addresses.Offsets);
        Assert.Equal(
            2,
            code.ToInstructions().Count(instruction =>
                instruction.Opcode == WasmOpcodes.I32Store));
    }

    [Fact]
    public void CoalescesAndZeroesEverySharedAnchorCell()
    {
        var layouts = new RecordingLayoutProvider();
        var addresses = new RecordingValueFrameAddressEmitter();
        var emitter = CreateEmitter(layouts, addresses);
        var filters = FilterEnvironmentLayout.Empty with
        {
            AnchorOffsets = ImmutableDictionary<int, int>.Empty
                .Add(3, 20)
                .Add(7, 24),
        };
        var context = CreateContext(
            new ValueFrameLayout(28, [], [], [], []),
            filters);
        var code = new RecordingInstructionWriter();

        emitter.Initialize(code, CreateHeader([]), context);

        Assert.Equal([20], addresses.Offsets);
        var instructions = code.ToInstructions();
        Assert.Equal(WasmOpcodes.I64Constant, instructions[0].Opcode);
        Assert.Equal(WasmOpcodes.I64Store, instructions[1].Opcode);
    }

    [Fact]
    public void EmptyLayoutWritesNothingAndRejectsMissingInputs()
    {
        var emitter = CreateEmitter(
            new RecordingLayoutProvider(),
            new RecordingValueFrameAddressEmitter());
        var code = new RecordingInstructionWriter();
        var header = CreateHeader([]);
        var context = CreateContext(new ValueFrameLayout(32, [], [], [], []));

        emitter.Initialize(code, header, context);

        Assert.Empty(code.ToArray());
        Assert.Throws<ArgumentNullException>(() =>
            emitter.Initialize(null!, header, context));
        Assert.Throws<ArgumentNullException>(() =>
            emitter.Initialize(code, null!, context));
        Assert.Throws<ArgumentNullException>(() =>
            emitter.Initialize(code, header, null!));
    }

    private static IValueFrameInitializationEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        IValueFrameAddressEmitter addresses,
        IValueLayoutProvider? values = null) => new[]
    {
        new ValueFrameInitializationEmitter(
            layouts,
            values ?? layouts,
            addresses),
    }.Cast<IValueFrameInitializationEmitter>().Single();

    private static StructuredMethodHeader CreateHeader(
        ImmutableArray<CliTypeIdentity> locals) => new(
        new FakeProgram().GetMethod(EntryKey),
        null,
        1,
        [.. locals.Select(local => local.StackKind)],
        locals,
        []);

    private static MethodEmissionContext CreateContext(
        ValueFrameLayout values,
        FilterEnvironmentLayout? filters = null) =>
        CreateMethodEmissionContext() with
        {
            ValueLayout = values,
            FilterEnvironment = filters ?? FilterEnvironmentLayout.Empty,
        };

    private sealed class RecordingValueFrameAddressEmitter :
        IValueFrameAddressEmitter
    {
        public List<int> Offsets { get; } = [];

        public void Emit(
            IWasmInstructionWriter code,
            MethodEmissionContext context,
            int offset) => Offsets.Add(offset);

        public void Emit(
            IWasmInstructionWriter code,
            MethodEmissionContext context,
            FilterCapture capture) => Offsets.Add(capture.Offset);
    }

    private sealed class FixedValueLayoutProvider(int size) :
        IValueLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type) => new(
            type,
            size,
            1,
            []);
    }
}
