using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class MethodFrameEntryEmitterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExceptionRootsComposeWithFilterAndStackTraceFrames(bool includeFilterAndTrace)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var context = CreateMethodEmissionContext() with
        {
            ExceptionRootSlots = ImmutableDictionary<StructuredExceptionGroupId, int>.Empty.Add(new(0), 0),
            ValueLayout = new ValueFrameLayout(includeFilterAndTrace ? 16 : 0, [], [], [], []),
            FilterEnvironment = includeFilterAndTrace
                ? new FilterEnvironmentLayout(16, 1, 0, [])
                : FilterEnvironmentLayout.Empty,
            StackTraceMethodId = includeFilterAndTrace ? 1 : 0,
            RuntimeImportSelection = new(WasmModuleProfile.CoreApplication,
                IncludeTerminalExceptionReporter: true, IncludeStackTrace: true),
        };
        var code = new RecordingInstructionWriter();
        CreateMethodFrameEntry(program, layouts, imports).Emit(code,
            new StructuredMethodHeader(program.GetMethod(EntryKey), null, 1, [], [], []), context);
        CreateMethodFrameExit(imports).Emit(code, context);
        var instructions = code.ToInstructions();
        Assert.Equal(0, context.RootMap.SlotCount);
        Assert.Equal(1, context.RootSlotCount);
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue == (uint)imports.Resolve(RuntimeImportSymbol.RootFrameEnter));
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue == (uint)imports.Resolve(RuntimeImportSymbol.RootFrameLeave));
        Assert.Equal(includeFilterAndTrace ? 2 : 1, instructions.Count(instruction =>
            instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue == (uint)imports.Resolve(RuntimeImportSymbol.RootFrameEnter)));
        Assert.Equal(includeFilterAndTrace ? 2 : 1, instructions.Count(instruction =>
            instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue == (uint)imports.Resolve(RuntimeImportSymbol.RootFrameLeave)));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 0)]
    [InlineData(WasmTarget.Wasm32, 1)]
    [InlineData(WasmTarget.Wasm64, 1)]
    public void InitializesValueTypeFilterCaptureForEachTargetWidth(
        WasmTarget target,
        int rootSlots)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true,
            CliValueKind.ValueType);
        var capture = new FilterCapture(
            new CapturedSlot(true, 0),
            valueType,
            8,
            []);
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(24, [], [], [], []),
            FilterEnvironment = new FilterEnvironmentLayout(
                24,
                rootSlots,
                0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(
                    capture.Slot,
                    capture)),
        };
        var body = new CilMethodBody(
            program.GetMethod(EntryKey),
            1,
            [],
            []);
        var code = new RecordingInstructionWriter();

        CreateMethodFrameEntry(
            program,
            layouts,
            WasmRuntimeImports.CreateCatalog()).Emit(code, Header(body), context);

        var bytes = code.ToArray();
        Assert.Contains(WasmOpcodes.Prefixed, bytes);
        Assert.Contains(
            target == WasmTarget.Wasm64
                ? WasmOpcodes.I64Constant
                : WasmOpcodes.I32Constant,
            bytes);
    }

    [Fact]
    public void InitializesRootFrameValueFrameAndValueArgumentStorage()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var addresses = new RecordingValueFrameAddressEmitter();
        var filterRoots = new RecordingFilterEnvironmentRootEmitter();
        var argumentTypes = new RecordingArgumentSignatureTypeResolver(
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var context = CreateMethodEmissionContext() with
        {
            RootMap = new MethodRootMap(
                EntryKey,
                ImmutableDictionary<RootSource, int>.Empty.Add(
                    new RootSource(RootSourceKind.Argument, 0),
                    0),
                []),
            ValueLayout = new ValueFrameLayout(
                24,
                ImmutableDictionary<int, int>.Empty.Add(0, 4),
                ImmutableDictionary<int, int>.Empty.Add(0, 8),
                [],
                []),
        };
        var emitter = CreateEmitter(
            layouts,
            imports,
            argumentTypes,
            addresses,
            filterRoots);
        var code = new RecordingInstructionWriter();

        emitter.Emit(
            code,
            new StructuredMethodHeader(
                program.GetMethod(EntryKey), null, 1, [], [], []),
            context);

        Assert.Contains(
            (byte)imports.Resolve(RuntimeImportSymbol.RootFrameEnter),
            code.ToArray());
        Assert.Contains(
            (byte)imports.Resolve(RuntimeImportSymbol.ValueFrameEnter),
            code.ToArray());
        Assert.Equal([4, 8], addresses.Offsets);
        Assert.Equal(0, filterRoots.Calls);
        Assert.Contains(WasmOpcodes.I32Store, code.ToArray());
    }

    [Fact]
    public void StoresScalarFilterArgumentCaptureAndPublishesFilterRoot()
    {
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var addresses = new RecordingValueFrameAddressEmitter();
        var filterRoots = new RecordingFilterEnvironmentRootEmitter();
        var capture = new FilterCapture(
            new CapturedSlot(true, 1),
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            12,
            []);
        var context = CreateMethodEmissionContext() with
        {
            FilterEnvironment = new FilterEnvironmentLayout(
                24,
                0,
                0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(
                    capture.Slot,
                    capture)),
        };
        var emitter = CreateEmitter(
            layouts,
            imports,
            new RecordingArgumentSignatureTypeResolver(
                CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            addresses,
            filterRoots);

        var code = new RecordingInstructionWriter();
        emitter.Emit(
            code,
            new StructuredMethodHeader(
                new FakeProgram().GetMethod(EntryKey), null, 1, [], [], []),
            context);

        Assert.Equal([0, 12], addresses.Offsets);
        Assert.Equal(1, filterRoots.Calls);
        Assert.Contains(WasmOpcodes.I32Store, code.ToArray());
        Assert.DoesNotContain(WasmOpcodes.Prefixed, code.ToArray());
    }

    private static IMethodFrameEntryEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        IRuntimeImportResolver imports,
        IArgumentSignatureTypeResolver argumentTypes,
        IValueFrameAddressEmitter addresses,
        IFilterEnvironmentRootEmitter filterRoots) =>
        new[]
        {
            new MethodFrameEntryEmitter(
                layouts,
                layouts,
                argumentTypes,
                imports,
                new StackTraceFrameEntryEmitter(imports),
                addresses,
                filterRoots),
        }.Cast<IMethodFrameEntryEmitter>().Single();

    private sealed class RecordingValueFrameAddressEmitter : IValueFrameAddressEmitter
    {
        public List<int> Offsets { get; } = [];

        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, int offset) =>
            Offsets.Add(offset);

        public void Emit(
            IWasmInstructionWriter code,
            MethodEmissionContext context,
            FilterCapture capture) => Offsets.Add(capture.Offset);
    }

    private sealed class RecordingFilterEnvironmentRootEmitter :
        IFilterEnvironmentRootEmitter
    {
        public int Calls { get; private set; }

        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context) => Calls++;
    }

    private sealed class RecordingArgumentSignatureTypeResolver(CliTypeIdentity type) :
        IArgumentSignatureTypeResolver
    {
        public CliTypeIdentity Resolve(StructuredMethodHeader header, int index) => type;
    }
}
