using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class RuntimeInitializerFunctionEmitterTests
{
    [Fact]
    public void InitializationUsesTargetWidthAndNamedRuntimeImports()
    {
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var wasm32 = Emit(WasmTargetLayout.Wasm32, runtimeImports);
        var wasm64 = Emit(WasmTargetLayout.Wasm64, runtimeImports);

        Assert.Equal([0, WasmOpcodes.I32Constant], wasm32[..2]);
        Assert.Equal([0, WasmOpcodes.I64Constant], wasm64[..2]);
        Assert.Equal(WasmOpcodes.End, wasm32[^1]);
        Assert.Contains(
            Call(runtimeImports.Resolve(RuntimeImportSymbol.Initialize)),
            Calls(wasm32));
        Assert.Contains(
            Call(runtimeImports.Resolve(RuntimeImportSymbol.RegisterTypes)),
            Calls(wasm32));
        Assert.Contains(
            Call(runtimeImports.Resolve(RuntimeImportSymbol.RegisterStaticRoot)),
            Calls(wasm32));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitializesFinalizationOnlyWhenSelected(bool includeFinalization)
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var writer = new EmitterTestSupport.RecordingInstructionWriter();
        var code = new GeneratedFunctionWriterFactory().Create();
        var lease = new GeneratedFunctionWriterLease(
            code.Bytes,
            code.Snapshots,
            writer);
        var selection = new RuntimeImportSelection(
            WasmModuleProfile.CoreApplication,
            IncludeTerminalExceptionReporter: true,
            IncludeFinalization: includeFinalization);

        new InitializerHarness(
            new FixedStaticDataLayout([]),
            imports,
            new RecordingAddressEmitter(),
            new RecordingRuntimeCoreInitializer()).Initialize(
                lease,
                TestRuntimeInitialization.Create(512, selection));

        Assert.Equal(
            includeFinalization,
            writer.ToInstructions().Any(instruction =>
                instruction.Opcode == WasmOpcodes.Call &&
                instruction.Operand.UnsignedValue == (uint)imports.Resolve(
                    RuntimeImportSymbol.InitializeFinalization,
                    selection with { IncludeFinalization = true })));
    }

    // The static Type objects become their types' objects once those types are registered.
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void RegistersTheStaticTypeObjectsWhenThereAreAny(int count)
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var writer = new EmitterTestSupport.RecordingInstructionWriter();
        var code = new GeneratedFunctionWriterFactory().Create();
        var lease = new GeneratedFunctionWriterLease(code.Bytes, code.Snapshots, writer);
        var selection = new RuntimeImportSelection(
            WasmModuleProfile.CoreApplication,
            IncludeTerminalExceptionReporter: true);

        new InitializerHarness(
            new FixedStaticDataLayout([]) { TypeObjectTableAddress = 640, TypeObjectCount = count, TypeObjectSize = 8 },
            imports,
            new RecordingAddressEmitter(),
            new RecordingRuntimeCoreInitializer()).Initialize(
                lease,
                TestRuntimeInitialization.Create(512, selection));

        var instructions = writer.ToInstructions().ToArray();
        var register = WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned(
            (uint)imports.Resolve(RuntimeImportSymbol.RegisterTypeObjects, selection)));
        var call = Array.IndexOf(instructions, register);
        if (count == 0)
        {
            Assert.Equal(-1, call);
            return;
        }
        Assert.Equal(
            [
                WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(count)),
                WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(8)),
            ],
            instructions[(call - 2)..call]);
    }

    [Fact]
    public void RegistersEveryTypeThroughOneTableCallAndEachStaticRoot()
    {
        var staticData = new FixedStaticDataLayout([200, 204]) { TypeTableAddress = 300 };
        var imports = new RecordingRuntimeImportResolver();
        var addresses = new RecordingAddressEmitter();
        var core = new RecordingRuntimeCoreInitializer();
        var initializer = new InitializerHarness(staticData, imports, addresses, core);
        var code = new GeneratedFunctionWriterFactory().Create();

        initializer.Initialize(code, TestRuntimeInitialization.Create(512));

        Assert.Equal(512, core.StaticDataEnd);
        Assert.Single(imports.Symbols, symbol => symbol == RuntimeImportSymbol.RegisterTypes);
        Assert.Equal(2, imports.Symbols.Count(symbol =>
            symbol == RuntimeImportSymbol.RegisterStaticRoot));
        Assert.Contains(300, addresses.Constants);
        Assert.Contains(200, addresses.Constants);
        Assert.Contains(204, addresses.Constants);
    }

    [Fact]
    public void EnabledStackTracePlanInitializesExceptionTraceLayout()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var plan = new StackTraceMethodPlan(
            ImmutableDictionary<EntityKey, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            [new(1, "Example.Program.Main()")],
            ImmutableDictionary<int, ImmutableArray<StackTraceLocationSymbol>>.Empty,
            29,
            31);

        var code = new GeneratedFunctionWriterFactory().Create();
        var instructionWriter = new EmitterTestSupport.RecordingInstructionWriter();
        var addresses = new RecordingAddressEmitter();
        var recordingCode = new GeneratedFunctionWriterLease(
            code.Bytes,
            code.Snapshots,
            instructionWriter);
        new InitializerHarness(
            new FixedStaticDataLayout([]),
            imports,
            addresses,
            new RecordingRuntimeCoreInitializer()).Initialize(
                recordingCode,
                new RuntimeInitializationPlan(
                    512,
                    plan,
                    new RuntimeImportSelection(
                        WasmModuleProfile.CoreApplication,
                        IncludeTerminalExceptionReporter: true,
                        IncludeStackTrace: true))
                );
        var instructions = instructionWriter.ToInstructions();

        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue == (uint)imports.Resolve(
                RuntimeImportSymbol.StackTraceInitialize,
                new RuntimeImportSelection(
                    WasmModuleProfile.CoreApplication,
                    IncludeTerminalExceptionReporter: true,
                    IncludeStackTrace: true)));
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I32Constant &&
            instruction.Operand.SignedValue == 29);
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I32Constant &&
            instruction.Operand.SignedValue == 31);
        // Frame text comes from the method-symbols sidecar, which CoreLib registers (issue 225).
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue == (uint)imports.Resolve(
                RuntimeImportSymbol.StackTraceRegisterSymbol,
                new RuntimeImportSelection(
                    WasmModuleProfile.CoreApplication,
                    IncludeTerminalExceptionReporter: true,
                    IncludeStackTrace: true)));
    }

    // CoreLib finds the sidecar descriptor through a static word the initializer fills, as wide as an address.
    [Theory]
    [InlineData(4, WasmOpcodes.I32Store)]
    [InlineData(8, WasmOpcodes.I64Store)]
    public void TheSidecarDescriptorAddressIsStoredInItsCoreLibWord(int addressSize, byte store)
    {
        var instructionWriter = new EmitterTestSupport.RecordingInstructionWriter();
        var addresses = new RecordingAddressEmitter();
        var code = new GeneratedFunctionWriterFactory().Create();
        new InitializerHarness(
            new FixedStaticDataLayout([]),
            WasmRuntimeImports.CreateCatalog(),
            addresses,
            new RecordingRuntimeCoreInitializer(),
            addressSize == 8 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32).Initialize(
                new GeneratedFunctionWriterLease(code.Bytes, code.Snapshots, instructionWriter),
                new RuntimeInitializationPlan(512, StackTraceMethodPlan.Disabled)
                {
                    SidecarSlotAddress = 600,
                    SidecarDescriptorAddress = 700,
                });

        Assert.Equal([600, 700], addresses.Constants.Where(value => value is 600 or 700));
        Assert.Contains(instructionWriter.ToInstructions(), instruction => instruction.Opcode == store);
    }

    [Fact]
    public void ModuleInitializersDelegateToTheirPlannedGuardFunction()
    {
        var code = new GeneratedFunctionWriterFactory().Create();
        var instructionWriter = new EmitterTestSupport.RecordingInstructionWriter();
        var recordingCode = new GeneratedFunctionWriterLease(
            code.Bytes,
            code.Snapshots,
            instructionWriter);
        var addresses = new RecordingAddressEmitter();
        var plan = TestRuntimeInitialization.Create(512) with
        {
            ModuleInitializers = [new ModuleInitializerCall(256, 37)],
        };

        new InitializerHarness(
            new FixedStaticDataLayout([]),
            WasmRuntimeImports.CreateCatalog(),
            addresses,
            new RecordingRuntimeCoreInitializer()).Initialize(recordingCode, plan);
        var instructions = instructionWriter.ToInstructions();

        Assert.DoesNotContain(256, addresses.Constants);
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue == 37);
        Assert.DoesNotContain(instructions, instruction => instruction.Opcode == WasmOpcodes.I32Store);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CallbackReadinessPublishesAfterStaticRootsAndBeforeModuleInitializers(
        WasmTarget target)
    {
        var code = new GeneratedFunctionWriterFactory().Create();
        var writer = new EmitterTestSupport.RecordingInstructionWriter();
        var recordingCode = new GeneratedFunctionWriterLease(
            code.Bytes,
            code.Snapshots,
            writer);
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var imports = WasmRuntimeImports.CreateCatalog();
        var plan = TestRuntimeInitialization.Create(512) with
        {
            NativeCallbackReadinessAddress = 300,
            ModuleInitializers = [new ModuleInitializerCall(256, 37)],
        };

        new InitializerHarness(
            new FixedStaticDataLayout([200]),
            imports,
            new AddressInstructionEmitter(layouts),
            new RecordingRuntimeCoreInitializer()).Initialize(recordingCode, plan);

        var instructions = writer.ToInstructions();
        var rootRegistration = Enumerable.Range(0, instructions.Length).Single(index =>
            instructions[index].Opcode == WasmOpcodes.Call &&
            instructions[index].Operand.UnsignedValue == (uint)imports.Resolve(
                RuntimeImportSymbol.RegisterStaticRoot));
        var readinessStore = Enumerable.Range(0, instructions.Length).Single(index =>
            instructions[index].Opcode == WasmOpcodes.I32Store);
        var moduleInitializer = Enumerable.Range(0, instructions.Length).Single(index =>
            instructions[index].Opcode == WasmOpcodes.Call &&
            instructions[index].Operand.UnsignedValue == 37);

        Assert.True(rootRegistration < readinessStore);
        Assert.True(readinessStore < moduleInitializer);
        Assert.DoesNotContain(instructions, instruction => instruction.Opcode == WasmOpcodes.If);
        Assert.Equal(WasmOpcodes.I32Constant, instructions[readinessStore - 1].Opcode);
        Assert.Equal(1, instructions[readinessStore - 1].Operand.SignedValue);
        var address = instructions[readinessStore - 2];
        Assert.Equal(
            target == WasmTarget.Wasm32
                ? WasmOpcodes.I32Constant
                : WasmOpcodes.I64Constant,
            address.Opcode);
        if (target == WasmTarget.Wasm32)
        {
            Assert.Equal(300, address.Operand.SignedValue);
        }
        else
        {
            Assert.Equal(300, address.Operand.Signed64Value);
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PlannedStateRegistrationGuardLimitsRegistrationsToTheFirstEntry(
        WasmTarget target)
    {
        var code = new GeneratedFunctionWriterFactory().Create();
        var writer = new EmitterTestSupport.RecordingInstructionWriter();
        var recordingCode = new GeneratedFunctionWriterLease(
            code.Bytes,
            code.Snapshots,
            writer);
        var imports = WasmRuntimeImports.CreateCatalog();
        var plan = TestRuntimeInitialization.Create(512) with
        {
            StateRegistrationGuardAddress = 320,
            NativeCallbackReadinessAddress = 300,
            ModuleInitializers = [new ModuleInitializerCall(256, 37)],
        };

        new InitializerHarness(
            new FixedStaticDataLayout([200, 204]) { TypeTableAddress = 280 },
            imports,
            new AddressInstructionEmitter(new RecordingLayoutProvider(WasmTargetLayout.For(target))),
            new RecordingRuntimeCoreInitializer()).Initialize(recordingCode, plan);

        var instructions = writer.ToInstructions();
        int IndexOf(Func<WasmInstruction, bool> predicate) =>
            Enumerable.Range(0, instructions.Length).Single(index => predicate(instructions[index]));
        int[] CallsTo(RuntimeImportSymbol symbol) =>
        [
            .. Enumerable.Range(0, instructions.Length).Where(index =>
                instructions[index].Opcode == WasmOpcodes.Call &&
                instructions[index].Operand.UnsignedValue == (uint)imports.Resolve(symbol)),
        ];
        bool IsAddress(WasmInstruction instruction, int value) => target == WasmTarget.Wasm32
            ? instruction.Opcode == WasmOpcodes.I32Constant && instruction.Operand.SignedValue == value
            : instruction.Opcode == WasmOpcodes.I64Constant && instruction.Operand.Signed64Value == value;

        var load = IndexOf(instruction => instruction.Opcode == WasmOpcodes.I32Load);
        var guardIf = IndexOf(instruction => instruction.Opcode == WasmOpcodes.If);
        var ends = Enumerable.Range(0, instructions.Length)
            .Where(index => instructions[index].Opcode == WasmOpcodes.End)
            .ToArray();
        Assert.Equal([instructions.Length - 1], ends[1..]);
        var guardEnd = ends[0];
        var stores = Enumerable.Range(0, instructions.Length)
            .Where(index => instructions[index].Opcode == WasmOpcodes.I32Store)
            .ToArray();
        var registrations = CallsTo(RuntimeImportSymbol.RegisterTypes)
            .Concat(CallsTo(RuntimeImportSymbol.RegisterStaticRoot))
            .ToArray();

        Assert.True(IsAddress(instructions[load - 1], 320));
        Assert.Equal(WasmOpcodes.I32EqualZero, instructions[load + 1].Opcode);
        Assert.Equal(load + 2, guardIf);
        Assert.Equal(3, registrations.Length);
        Assert.All(registrations, index => Assert.InRange(index, guardIf + 1, stores[0] - 1));
        Assert.Equal(2, stores.Length);
        Assert.True(IsAddress(instructions[stores[0] - 2], 320));
        Assert.Equal(1, instructions[stores[0] - 1].Operand.SignedValue);
        Assert.Equal(stores[0] + 1, guardEnd);
        Assert.True(IsAddress(instructions[stores[1] - 2], 300));
        Assert.True(guardEnd < stores[1]);
        Assert.True(stores[1] < IndexOf(instruction =>
            instruction.Opcode == WasmOpcodes.Call && instruction.Operand.UnsignedValue == 37));
    }

    private static byte[] Emit(
        WasmTargetLayout target,
        RuntimeImportCatalog runtimeImports,
        StackTraceMethodPlan? stackTraceMethods = null)
    {
        var layouts = new RecordingLayoutProvider(target);
        return EmitterTestSupport.CreateRuntimeInitializerFunctionEmitter(
            layouts,
            runtimeImports).Emit(new RuntimeInitializationPlan(
                512,
                stackTraceMethods ?? StackTraceMethodPlan.Disabled));
    }

    private static byte[] Calls(byte[] code) => [.. code
        .Select((value, index) => (value, index))
        .Where(item => item.value == WasmOpcodes.Call && item.index + 1 < code.Length)
        .Select(item => code[item.index + 1])];

    private static byte Call(int index) => checked((byte)index);

    // Runs the emitter against the test's writer so its instructions can be inspected.
    private sealed class InitializerHarness(
        IStaticDataLayout staticData,
        IRuntimeImportResolver imports,
        IAddressInstructionEmitter addresses,
        IRuntimeCoreInitializer core,
        WasmTargetLayout? target = null)
    {
        public void Initialize(GeneratedFunctionWriterLease code, RuntimeInitializationPlan plan) =>
            new RuntimeInitializerFunctionEmitter(
                new RecordingLayoutProvider(target ?? WasmTargetLayout.Wasm32),
                staticData,
                imports,
                addresses,
                core,
                new FixedWriterFactory(code)).Emit(plan);
    }

    private sealed class FixedWriterFactory(GeneratedFunctionWriterLease code) : IGeneratedFunctionWriterFactory
    {
        public GeneratedFunctionWriterLease Create() => code;
    }

    private sealed class FixedStaticDataLayout(ImmutableArray<int> roots) : IStaticDataLayout
    {
        public int StaticDataEnd => 0;
        public ImmutableArray<int> StaticRootAddresses => roots;
        public ImmutableArray<DataSegment> DataSegments => [];
        public int TypeTableAddress { get; init; }
        public int TypeObjectTableAddress { get; init; }
        public int TypeObjectCount { get; init; }
        public int TypeObjectSize { get; init; }

        public StringLayout GetStringLayout(string value) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingRuntimeImportResolver : IRuntimeImportResolver
    {
        public List<RuntimeImportSymbol> Symbols { get; } = [];

        public int Resolve(RuntimeImportSymbol symbol)
        {
            Symbols.Add(symbol);
            return (int)symbol;
        }

        public int Resolve(RuntimeImportSymbol symbol, WasmModuleProfile profile) =>
            Resolve(symbol);

        public ImmutableArray<WasmFunctionImport> Resolve(WasmModuleProfile profile) => [];
    }

    private sealed class RecordingAddressEmitter : IAddressInstructionEmitter
    {
        public List<int> Constants { get; } = [];

        public void Emit(IWasmInstructionWriter code, int constant) =>
            Constants.Add(constant);

        public void Emit(IWasmInstructionWriter code, AddressOperation operation) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingRuntimeCoreInitializer : IRuntimeCoreInitializer
    {
        public int? StaticDataEnd { get; private set; }

        public void Initialize(GeneratedFunctionWriterLease code, int staticDataEnd) =>
            StaticDataEnd = staticDataEnd;
    }
}
