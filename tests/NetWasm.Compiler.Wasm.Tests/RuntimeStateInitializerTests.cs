using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class RuntimeStateInitializerTests
{
    [Fact]
    public void InitializationUsesTargetWidthAndNamedRuntimeImports()
    {
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var wasm32 = Emit(WasmTargetLayout.Wasm32, runtimeImports);
        var wasm64 = Emit(WasmTargetLayout.Wasm64, runtimeImports);

        Assert.Equal(WasmOpcodes.I32Constant, wasm32[0]);
        Assert.Equal(WasmOpcodes.I64Constant, wasm64[0]);
        Assert.Contains(
            Call(runtimeImports.Resolve(RuntimeImportSymbol.Initialize)),
            Calls(wasm32));
        Assert.Contains(
            Call(runtimeImports.Resolve(RuntimeImportSymbol.RegisterType)),
            Calls(wasm32));
        Assert.Contains(
            Call(runtimeImports.Resolve(RuntimeImportSymbol.RegisterStaticRoot)),
            Calls(wasm32));
    }

    [Fact]
    public void InitializesEveryDescriptorFamilyAndStaticRootThroughFocusedActions()
    {
        var method = new FakeProgram().GetMethod(EmitterTestSupport.EntryKey);
        var finalizer = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(
                EmitterTestSupport.Assembly,
                "Test",
                "Type",
                false),
            [],
            method.Signature);
        var descriptors = new FixedTypeDescriptorSource(
            [
                new(EmitterTestSupport.TypeKey, 1, 0, 16, 100, 3, null),
                new(EmitterTestSupport.TypeKey, 2, 1, 20, 104, 4, finalizer),
            ],
            [new(CliTypeIdentity.FromStackKind(CliValueKind.I4), 3, 0, 24, 108, 5, null)],
            [new(CliTypeIdentity.FromStackKind(CliValueKind.I8), 4, 8, 8, 112, 6)],
            [new(CliTypeIdentity.ScopedGenericParameter("owner", false, 0), 5)]);
        var staticData = new FixedStaticDataLayout([200, 204]);
        var imports = new RecordingRuntimeImportResolver();
        var addresses = new RecordingAddressEmitter();
        var core = new RecordingRuntimeCoreInitializer();
        IRuntimeStateInitializer initializer = new[]
        {
            new RuntimeStateInitializer(
                descriptors,
                staticData,
                imports,
                addresses,
                core),
        }.Cast<IRuntimeStateInitializer>().Single();
        var code = new GeneratedFunctionWriterFactory().Create();

        initializer.Initialize(code, TestRuntimeInitialization.Create(512));

        Assert.Equal(512, core.StaticDataEnd);
        Assert.Equal(4, imports.Symbols.Count(symbol =>
            symbol == RuntimeImportSymbol.RegisterType));
        Assert.Single(imports.Symbols, symbol =>
            symbol == RuntimeImportSymbol.RegisterValueType);
        Assert.Equal(2, imports.Symbols.Count(symbol =>
            symbol == RuntimeImportSymbol.RegisterStaticRoot));
        Assert.Contains(200, addresses.Constants);
        Assert.Contains(204, addresses.Constants);
    }

    [Fact]
    public void TypeRegistrationEmitsInterfaceTraitAfterFinalizerTrait()
    {
        var descriptors = new FixedTypeDescriptorSource(
            [new TypeDescriptorLayout(
                EmitterTestSupport.TypeKey,
                7,
                0,
                16,
                100,
                3,
                null)
            {
                IsInterface = true,
            }],
            [],
            []);
        var imports = WasmRuntimeImports.CreateCatalog();
        var code = new GeneratedFunctionWriterFactory().Create();
        var writer = new EmitterTestSupport.RecordingInstructionWriter();
        var recordingCode = new GeneratedFunctionWriterLease(
            code.Bytes,
            code.Snapshots,
            writer);

        new RuntimeStateInitializer(
            descriptors,
            new FixedStaticDataLayout([]),
            imports,
            new RecordingAddressEmitter(),
            new RecordingRuntimeCoreInitializer()).Initialize(
                recordingCode,
                TestRuntimeInitialization.Create(512));

        var instructions = writer.ToInstructions();
        var registerType = (uint)imports.Resolve(RuntimeImportSymbol.RegisterType);
        var callIndex = Enumerable.Range(0, instructions.Length).Single(index =>
            instructions[index].Opcode == WasmOpcodes.Call &&
            instructions[index].Operand.UnsignedValue == registerType);
        Assert.True(callIndex >= 2);
        Assert.Equal(0, instructions[callIndex - 2].Operand.SignedValue);
        Assert.Equal(1, instructions[callIndex - 1].Operand.SignedValue);
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
        new RuntimeStateInitializer(
            new FixedTypeDescriptorSource([], [], []),
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
                {
                    StackTraceSymbolRegistrationGuardAddress = 300,
                    StackTraceSymbols = [new(1, 304, 22)],
                });
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
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue == (uint)imports.Resolve(
                RuntimeImportSymbol.StackTraceRegisterSymbol,
                new RuntimeImportSelection(
                    WasmModuleProfile.CoreApplication,
                    IncludeTerminalExceptionReporter: true,
                    IncludeStackTrace: true)));
        Assert.Equal(2, addresses.Constants.Count(value => value == 300));
        Assert.Contains(304, addresses.Constants);
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.I32Load);
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.If);
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.I32Store);
    }

    [Fact]
    public void EnabledStackTracePlanRequiresRegistrationGuard()
    {
        var plan = new StackTraceMethodPlan(
            ImmutableDictionary<EntityKey, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            [new(1, "Example.Program.Main()")],
            ImmutableDictionary<int, ImmutableArray<StackTraceLocationSymbol>>.Empty,
            29,
            31);
        var initializer = new RuntimeStateInitializer(
            new FixedTypeDescriptorSource([], [], []),
            new FixedStaticDataLayout([]),
            WasmRuntimeImports.CreateCatalog(),
            new RecordingAddressEmitter(),
            new RecordingRuntimeCoreInitializer());

        var exception = Assert.Throws<InvalidOperationException>(() => initializer.Initialize(
            new GeneratedFunctionWriterFactory().Create(),
            new RuntimeInitializationPlan(
                512,
                plan,
                new RuntimeImportSelection(
                    WasmModuleProfile.CoreApplication,
                    IncludeTerminalExceptionReporter: true,
                    IncludeStackTrace: true))));

        Assert.Contains("guard address", exception.Message, StringComparison.Ordinal);
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

        new RuntimeStateInitializer(
            new FixedTypeDescriptorSource([], [], []),
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

        new RuntimeStateInitializer(
            new FixedTypeDescriptorSource([], [], []),
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

    private static byte[] Emit(
        WasmTargetLayout target,
        RuntimeImportCatalog runtimeImports,
        StackTraceMethodPlan? stackTraceMethods = null)
    {
        var code = new GeneratedFunctionWriterFactory().Create();
        var layouts = new RecordingLayoutProvider(target);
        EmitterTestSupport.CreateRuntimeStateInitializer(
            layouts,
            runtimeImports).Initialize(code, new RuntimeInitializationPlan(
                512,
                stackTraceMethods ?? StackTraceMethodPlan.Disabled));
        return code.Snapshots.Read();
    }

    private static byte[] Calls(byte[] code) => [.. code
        .Select((value, index) => (value, index))
        .Where(item => item.value == WasmOpcodes.Call && item.index + 1 < code.Length)
        .Select(item => code[item.index + 1])];

    private static byte Call(int index) => checked((byte)index);

    private sealed class FixedTypeDescriptorSource(
        ImmutableArray<TypeDescriptorLayout> types,
        ImmutableArray<ConstructedTypeDescriptorLayout> constructedTypes,
        ImmutableArray<ValueTypeDescriptorLayout> valueTypes,
        ImmutableArray<MetadataTypeDescriptorLayout> metadataTypes = default) :
        ITypeDescriptorSource
    {
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => types;
        public ImmutableArray<ConstructedTypeDescriptorLayout>
            ConstructedTypeDescriptors => constructedTypes;
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => valueTypes;
        public ImmutableArray<MetadataTypeDescriptorLayout> MetadataTypeDescriptors =>
            metadataTypes.IsDefault ? [] : metadataTypes;
    }

    private sealed class FixedStaticDataLayout(ImmutableArray<int> roots) : IStaticDataLayout
    {
        public int StaticDataEnd => 0;
        public ImmutableArray<int> StaticRootAddresses => roots;
        public ImmutableArray<DataSegment> DataSegments => [];

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
