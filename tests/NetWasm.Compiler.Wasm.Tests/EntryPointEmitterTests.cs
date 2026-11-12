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
            initialization: TestRuntimeInitialization.Create(
                512,
                new RuntimeImportSelection(
                    WasmModuleProfile.CoreApplication,
                    IncludeTerminalExceptionReporter: true,
                    IncludeFinalization: hasFinalizers)),
            hasFinalizers,
            new FunctionIndexResolver(new FakeProgram(), new FakeProgram(), indices));

        Assert.True(body.AsSpan().IndexOf("\u0010\u001e"u8) >= 0);
        Assert.Equal(
            hasFinalizers,
            body.AsSpan().IndexOf([
                WasmOpcodes.Call,
                (byte)imports.Resolve(
                    RuntimeImportSymbol.FinalizerSafepoint,
                    new RuntimeImportSelection(
                        WasmModuleProfile.CoreApplication,
                        IncludeTerminalExceptionReporter: true,
                        IncludeFinalization: true)),
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
        Assert.Equal(8, declaredLocalCount);
    }

    [Fact]
    public void CompletionReceivesTheEntryResultAndSuppliesTheProcessResult()
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var imports = WasmRuntimeImports.CreateCatalog();
        var entry = new FakeProgram().GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.ManagedReference),
        };
        var completion = new MethodInstanceModel(
            entry with { Key = new(Assembly, 0x06000099), Name = "Run" },
            CliTypeIdentity.Named(Assembly, "System.Runtime.InteropServices", "AsyncEntryPoint", isValueType: false),
            [],
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.ManagedReference));

        var body = CreateEmitter(layouts, imports).Emit(
            entry,
            initialization: TestRuntimeInitialization.Create(512),
            hasFinalizers: false,
            new CompletionIndices(completion),
            reportTerminalExceptions: false,
            completion: completion);

        // The result local is the completion's i32, not the entry's wasm64 reference.
        Assert.Equal((byte)0x7f, body[2]);
        Assert.Contains(
            Enumerable.Range(0, body.Length - 3),
            index => body[index] == WasmOpcodes.Call && body[index + 1] == 30 &&
                body[index + 2] == WasmOpcodes.Call && body[index + 3] == 31);
    }

    // The startup preflights may wait on wasi:filesystem, so the process
    // entry calls them after runtime initialization and before Main.
    [Fact]
    public void ProcessEntryRunsTheStartupPreflightsBeforeMain()
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm32);
        var imports = WasmRuntimeImports.CreateCatalog();
        var entry = new FakeProgram().GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.Void),
        };
        var preflight = new EntityKey(Assembly, 0x06000098);

        var body = CreateEmitter(layouts, imports).Emit(
            entry,
            initialization: TestRuntimeInitialization.Create(512),
            hasFinalizers: false,
            new PreflightIndices(preflight),
            reportTerminalExceptions: false,
            startupPreflights: [preflight]);

        Assert.Contains(
            Enumerable.Range(0, body.Length - 3),
            index => body[index] == WasmOpcodes.Call && body[index + 1] == 32 &&
                body[index + 2] == WasmOpcodes.Call && body[index + 3] == 30);
    }

    // The process entry ends the process through CoreLib after Main returns.
    [Fact]
    public void ProcessEntryRunsTheProcessExitAfterMain()
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm32);
        var imports = WasmRuntimeImports.CreateCatalog();
        var entry = new FakeProgram().GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.Void),
        };
        var exit = new EntityKey(Assembly, 0x06000098);

        var body = CreateEmitter(layouts, imports).Emit(
            entry,
            initialization: TestRuntimeInitialization.Create(512),
            hasFinalizers: false,
            new PreflightIndices(exit),
            reportTerminalExceptions: false,
            processExit: exit);

        Assert.Contains(
            Enumerable.Range(0, body.Length - 3),
            index => body[index] == WasmOpcodes.Call && body[index + 1] == 30 &&
                body[index + 2] == WasmOpcodes.Call && body[index + 3] == 32);
    }

    // An int Main hands its result to the process exit, which exits with it when it is not zero.
    [Fact]
    public void ProcessEntryHandsAnIntMainsResultToTheProcessExit()
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm32);
        var imports = WasmRuntimeImports.CreateCatalog();
        var entry = new FakeProgram().GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.I4),
        };
        var exit = new EntityKey(Assembly, 0x06000098);

        var body = CreateEmitter(layouts, imports).Emit(
            entry,
            initialization: TestRuntimeInitialization.Create(512),
            hasFinalizers: false,
            new PreflightIndices(exit),
            reportTerminalExceptions: false,
            processExit: exit);

        Assert.Contains(
            Enumerable.Range(0, body.Length - 7),
            index => body[index] == WasmOpcodes.Call && body[index + 1] == 30 &&
                body[index + 2] == WasmOpcodes.LocalSet && body[index + 3] == 0 &&
                body[index + 4] == WasmOpcodes.LocalGet && body[index + 5] == 0 &&
                body[index + 6] == WasmOpcodes.Call && body[index + 7] == 32);
    }

    // A callback-lifted entry's callback runs after its start initialized the runtime, so its
    // body is the entry's without the initialization.
    [Fact]
    public void ACallbackBoundarySkipsRuntimeInitialization()
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm32);
        var imports = WasmRuntimeImports.CreateCatalog();
        var entry = new FakeProgram().GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.Void),
        };
        var exit = new EntityKey(Assembly, 0x06000098);
        var emitter = CreateEmitter(layouts, imports);

        var initialized = emitter.Emit(entry, TestRuntimeInitialization.Create(512), false,
            new PreflightIndices(exit), reportTerminalExceptions: false);
        var resumed = emitter.Emit(entry, TestRuntimeInitialization.Create(512), false,
            new PreflightIndices(exit), reportTerminalExceptions: false, initializesRuntime: false);

        Assert.True(resumed.Length < initialized.Length);
        Assert.Equal([WasmOpcodes.Call, 30, WasmOpcodes.Return, WasmOpcodes.End], resumed[^4..]);
        Assert.Equal(resumed[^4..], initialized[^4..]);
    }

    private sealed class PreflightIndices(EntityKey preflight) : IFunctionIndexResolver
    {
        public OptionalFunctionIndex ManagedThrowFunction => OptionalFunctionIndex.At(7);

        public int Resolve(EntityKey method) =>
            method == EntryKey ? 30 : method == preflight ? 32 : throw new InvalidOperationException();
        public int Resolve(string method) => throw new InvalidOperationException();
        public int Resolve(MethodInstanceModel method) => throw new InvalidOperationException();
        public int Resolve(InteropImportKey import) => throw new InvalidOperationException();
    }

    private sealed class CompletionIndices(MethodInstanceModel completion) : IFunctionIndexResolver
    {
        public OptionalFunctionIndex ManagedThrowFunction => OptionalFunctionIndex.At(7);

        public int Resolve(EntityKey method) => method == EntryKey ? 30 : throw new InvalidOperationException();
        public int Resolve(string method) => throw new InvalidOperationException();
        public int Resolve(MethodInstanceModel method) =>
            method == completion ? 31 : throw new InvalidOperationException();
        public int Resolve(InteropImportKey import) => throw new InvalidOperationException();
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComponentProcessEntryUsesItsSelectedTerminalReporterIndex(bool includeRaise)
    {
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var selection = new RuntimeImportSelection(
            WasmModuleProfile.ComponentCoreModule,
            IncludeTerminalExceptionReporter: true,
            IncludeTerminalExceptionRaise: includeRaise);
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
        var reportOffset = body.AsSpan().IndexOf([
            WasmOpcodes.Call,
            (byte)selectedIndex,
        ]);
        var raiseIndex = imports.Resolve(
            RuntimeImportSymbol.ManagedTerminalExceptionRaise,
            selection with { IncludeTerminalExceptionRaise = true });
        Assert.Equal(includeRaise, body.AsSpan(reportOffset + 2).IndexOf([
            WasmOpcodes.Call,
            (byte)raiseIndex,
        ]) >= 0);
        Assert.Contains(WasmOpcodes.Unreachable, body[reportOffset..]);
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
        Assert.Equal(7, declaredLocalCount);
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
            CreateRuntimeStateInitializer(),
            terminal,
            new ManagedTerminalTrapBoundaryEmitter(
                layouts,
                new ExceptionPayloadBlockEmitter(layouts)),
            new GeneratedFunctionWriterFactory());
    }
}
