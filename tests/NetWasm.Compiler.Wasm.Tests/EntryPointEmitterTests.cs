using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class EntryPointEmitterTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void EntryInitializesRuntimeCallsManagedMethodAndOptionallyDrainsFinalizers(
        bool hasFinalizers,
        WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var imports = WasmRuntimeImports.CreateCatalog();
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                EntryKey, new(30)),
            [],
            [],
            []);
        var emitter = CreateEmitter(layouts, imports);

        var body = emitter.Emit(
            new FakeProgram().GetMethod(EntryKey),
            initialization: TestRuntimeInitialization.Create(512),
            hasFinalizers,
            new FunctionIndexResolver(new FakeProgram(), new FakeProgram(), indices));

        Assert.True(body.AsSpan().IndexOf("\u0010\u001e"u8) >= 0);
        Assert.Equal(
            hasFinalizers,
            body.AsSpan().IndexOf([
                WasmOpcodes.Call,
                (byte)imports.Resolve(RuntimeImportSymbol.FinalizerSafepoint),
            ]) >= 0);
    }

    [Theory]
    [InlineData(CliValueKind.I4, WasmTarget.Wasm32, (byte)0x7f)]
    [InlineData(CliValueKind.I8, WasmTarget.Wasm32, (byte)0x7e)]
    [InlineData(CliValueKind.F4, WasmTarget.Wasm64, (byte)0x7d)]
    [InlineData(CliValueKind.F8, WasmTarget.Wasm64, (byte)0x7c)]
    [InlineData(CliValueKind.ManagedReference, WasmTarget.Wasm64, (byte)0x7e)]
    public void ResultLocalUsesTheManagedMethodResultType(
        CliValueKind result,
        WasmTarget target,
        byte expectedWasmType)
    {
        var layouts = new RecordingLayoutProvider(target == WasmTarget.Wasm64
            ? WasmTargetLayout.Wasm64
            : WasmTargetLayout.Wasm32);
        var imports = WasmRuntimeImports.CreateCatalog();
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                EntryKey, new(30)),
            [],
            [],
            []);
        var entry = new FakeProgram().GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(result, CliValueKind.I4),
        };
        var emitter = CreateEmitter(layouts, imports);

        var body = emitter.Emit(
            entry,
            initialization: TestRuntimeInitialization.Create(512),
            hasFinalizers: false,
            new FunctionIndexResolver(new FakeProgram(), new FakeProgram(), indices));

        Assert.Equal(expectedWasmType, body[2]);
        var declaredLocalCount = 0;
        var offset = 1;
        for (var index = 0; index < body[0]; index++)
        {
            declaredLocalCount += body[offset];
            offset += 2;
        }
        Assert.Equal(6, declaredLocalCount);
    }

    [Fact]
    public void ComponentEntryRestoresResultWithoutCallingTerminalReporter()
    {
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                EntryKey, new(30)),
            [],
            [],
            []);
        var emitter = CreateEmitter(layouts, imports);

        var body = emitter.Emit(
            new FakeProgram().GetMethod(EntryKey),
            initialization: TestRuntimeInitialization.Create(512),
            hasFinalizers: false,
            new FunctionIndexResolver(new FakeProgram(), new FakeProgram(), indices),
            reportTerminalExceptions: false);

        Assert.Equal(
            -1,
            body.AsSpan().IndexOf(
                new byte[]
                {
                    WasmOpcodes.Call,
                    (byte)imports.Resolve(RuntimeImportSymbol.ManagedTerminalExceptionReport),
                }));
        Assert.True(
            body.AsSpan().IndexOf(
                new byte[]
                {
                    WasmOpcodes.LocalGet,
                    1,
                    WasmOpcodes.Return,
                }) > 0);
    }

    [Fact]
    public void ComponentProcessEntryUsesItsSelectedTerminalReporterIndex()
    {
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var selection = new RuntimeImportSelection(
            WasmModuleProfile.ComponentCoreModule,
            IncludeTerminalExceptionReporter: true);
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                EntryKey, new(30)),
            [],
            [],
            []);
        var emitter = CreateEmitter(layouts, imports);

        var body = emitter.Emit(
            new FakeProgram().GetMethod(EntryKey),
            initialization: TestRuntimeInitialization.Create(512, selection),
            hasFinalizers: false,
            new FunctionIndexResolver(new FakeProgram(), new FakeProgram(), indices));

        var selectedIndex = imports.Resolve(
            RuntimeImportSymbol.ManagedTerminalExceptionReport,
            selection);
        Assert.NotEqual(
            imports.Resolve(
                RuntimeImportSymbol.ManagedTerminalExceptionReport,
                WasmModuleProfile.CoreApplication),
            selectedIndex);
        Assert.True(body.AsSpan().IndexOf([
            WasmOpcodes.Call,
            (byte)selectedIndex,
        ]) >= 0);
    }

    [Fact]
    public void ManagedProcessEntryMaterializesArgumentsInsideTheGuest()
    {
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty
                .Add(EntryKey, new(30))
                .Add(StringMethodKey, new(31)),
            [],
            [],
            []);
        var entry = new FakeProgram().GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliValueKind.I4,
                CliValueKind.ManagedReference),
        };
        var emitter = CreateEmitter(layouts, imports);

        var body = emitter.Emit(
            entry,
            initialization: TestRuntimeInitialization.Create(512),
            hasFinalizers: false,
            new FunctionIndexResolver(new FakeProgram(), new FakeProgram(), indices),
            argumentFactory: StringMethodKey);

        Assert.True(body.AsSpan().IndexOf([
            WasmOpcodes.Call,
            (byte)31,
            WasmOpcodes.Call,
            (byte)30,
        ]) >= 0);
    }

    [Fact]
    public void VoidEntryDeclaresTerminalExceptionStateLocals()
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var imports = WasmRuntimeImports.CreateCatalog();
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                EntryKey, new(30)),
            [],
            [],
            []);
        var entry = new FakeProgram().GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4),
        };
        var emitter = CreateEmitter(layouts, imports);

        var body = emitter.Emit(
            entry,
            initialization: TestRuntimeInitialization.Create(512),
            hasFinalizers: false,
            new FunctionIndexResolver(new FakeProgram(), new FakeProgram(), indices));

        var declaredLocalCount = 0;
        var offset = 1;
        for (var index = 0; index < body[0]; index++)
        {
            declaredLocalCount += body[offset];
            offset += 2;
        }
        Assert.Equal(5, declaredLocalCount);
    }

    private static EntryPointEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        RuntimeImportCatalog imports)
    {
        var program = new FakeProgram();
        var state = new ExceptionObjectStateReader(
            layouts,
            layouts, new ExceptionFieldLayoutResolver(
            layouts,
            program,
            program,
            layouts));
        var terminal = new ManagedTerminalExceptionBoundaryEmitter(
            layouts,
            imports,
            new ExceptionPayloadBlockEmitter(layouts),
            state);
        return new EntryPointEmitter(
            layouts,
            imports,
            CreateRuntimeStateInitializer(layouts, imports),
            terminal,
            new GeneratedFunctionWriterFactory());
    }
}
