using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ModuleImportCollectorTests
{
    [Fact]
    public void CollectsRuntimeInteropJavaScriptAndCanonicalImports()
    {
        var program = new FakeProgram();
        var entry = program.GetMethod(EmitterTestSupport.EntryKey);
        var jsKey = EmitterTestSupport.Key(0x06000031);
        var synchronousJsKey = EmitterTestSupport.Key(0x06000034);
        var witKey = EmitterTestSupport.Key(0x06000032);
        var jsImport = entry with
        {
            Key = jsKey,
            Name = "JavaScriptImport",
            Signature = MethodSignatureModel.Create(
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4),
            RelativeVirtualAddress = 0,
            JSImport = new("invoke", null),
        };
        var witImport = entry with
        {
            Key = witKey,
            Name = "CanonicalImport",
            Signature = MethodSignatureModel.Create(CliValueKind.I4),
            RelativeVirtualAddress = 0,
            WitImport = new("example:host@1.0.0/api", "read"),
        };
        var synchronousJsImport = jsImport with
        {
            Key = synchronousJsKey,
            Name = "SynchronousJavaScriptImport",
            JSImport = new("sync", "custom"),
        };
        var canonical = new CanonicalAbiFunction(
            "example:host@1.0.0/api",
            "read",
            witKey,
            [],
            new(CanonicalAbiTypeKind.S32, witImport.Signature.ReturnSignatureType));
        var request = WasmEmissionRequest.Create(
            entry,
            ImmutableDictionary<EntityKey,
                NetWasm.Compiler.ControlFlow.Structured.StructuredMethod>.Empty,
            ImmutableDictionary<EntityKey, MethodRootMap>.Empty,
            [],
            ImmutableDictionary<string, EntityKey>.Empty,
            jsImportMethods: [jsImport, synchronousJsImport],
            witImportMethods: [witImport],
            componentContract: new("example:host@1.0.0", "host", [canonical], []),
            javaScriptAsyncBindings:
                ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>.Empty.Add(
                    jsKey,
                    null!),
            moduleProfile: WasmModuleProfile.ComponentCoreModule);
        var callbackInstance = new MethodInstanceModel(
            entry,
            CliTypeIdentity.Named(
                EmitterTestSupport.Assembly,
                "Tests",
                "Callback",
                isValueType: false),
            [],
            entry.Signature);
        var callbacks = ImmutableDictionary<
            (EntityKey Method, int ParameterIndex), HostCallbackDeclaration>.Empty.Add(
                (jsKey, 0),
                new(jsKey, 0, callbackInstance, "callback"));
        var runtimeImport = Import("runtime", "first");
        var interopImport = Import("interop", "second");
        var parameters = new RecordingJavaScriptParameterTypeResolver();
        var collector = CreateCollector(program, parameters);

        var imports = collector.Collect(new(
            request,
            [runtimeImport],
            [interopImport],
            WasmTarget.Wasm32,
            callbacks));

        Assert.Equal(5, imports.Length);
        Assert.Equal(["first", "second", "invoke", "sync", "read"],
            imports.Select(import => import.Name));
        Assert.Equal(RuntimeAbi.HostModule, imports[2].Module);
        Assert.Equal("custom", imports[3].Module);
        Assert.Equal(
            [CliValueKind.I8, CliValueKind.I4, CliValueKind.I4,
                CliValueKind.ManagedAddress],
            imports[2].Type.Parameters.ToArray());
        Assert.Equal([true, false, false, false], parameters.CallbackFlags);
        Assert.Contains("example:host", imports[4].Module, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectsEquivalentManagedCanonicalImportBinding()
    {
        var program = new FakeProgram();
        var entry = program.GetMethod(EmitterTestSupport.EntryKey);
        var reachable = entry with
        {
            Key = EmitterTestSupport.Key(0x06000035),
            Signature = MethodSignatureModel.Create(CliValueKind.I4),
            RelativeVirtualAddress = 0,
            WitImport = new("example:host@1.0.0/api", "read"),
        };
        var representative = EmitterTestSupport.Key(0x06000036);
        var canonical = new CanonicalAbiFunction(
            "example:host@1.0.0/api",
            "read",
            representative,
            [],
            new(CanonicalAbiTypeKind.S32, reachable.Signature.ReturnSignatureType));
        var request = EmitterTestSupport.CreateEmissionRequest(program) with
        {
            WitImportMethods = [reachable],
            ComponentContract = new(
                "example:host@1.0.0",
                "host",
                [canonical],
                []),
        };

        var imports = CreateCollector(
            program,
            new RecordingJavaScriptParameterTypeResolver()).Collect(
                new(request, [], [], WasmTarget.Wasm32, []));

        var import = Assert.Single(imports);
        Assert.Equal("read", import.Name);
        Assert.Contains("example:host", import.Module, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectsOneCanonicalImportForEquivalentManagedBindings()
    {
        var program = new FakeProgram();
        var entry = program.GetMethod(EmitterTestSupport.EntryKey);
        var first = entry with
        {
            Key = EmitterTestSupport.Key(0x06000037),
            RelativeVirtualAddress = 0,
            WitImport = new("example:host@1.0.0/api", "read"),
        };
        var second = first with
        {
            Key = EmitterTestSupport.Key(0x06000038),
            Name = "EquivalentCanonicalImport",
        };
        var canonical = new CanonicalAbiFunction(
            "example:host@1.0.0/api",
            "read",
            first.Key,
            [],
            null);
        var request = EmitterTestSupport.CreateEmissionRequest(program) with
        {
            WitImportMethods = [first, second],
            ComponentContract = new(
                "example:host@1.0.0",
                "host",
                [canonical],
                []),
        };

        var imports = CreateCollector(
            program,
            new RecordingJavaScriptParameterTypeResolver()).Collect(
                new(request, [], [], WasmTarget.Wasm32, []));

        Assert.Single(imports);
        Assert.Equal("read", imports[0].Name);
    }

    [Fact]
    public void RejectsReachableCanonicalImportMissingFromContract()
    {
        var program = new FakeProgram();
        var entry = program.GetMethod(EmitterTestSupport.EntryKey);
        var witImport = entry with
        {
            Key = EmitterTestSupport.Key(0x06000033),
            WitImport = new("example:host@1.0.0/api", "missing"),
        };
        var request = EmitterTestSupport.CreateEmissionRequest(program) with
        {
            WitImportMethods = [witImport],
        };

        var exception = Assert.Throws<CompilerException>(() =>
            CreateCollector(program, new RecordingJavaScriptParameterTypeResolver()).Collect(
                new(request, [], [], WasmTarget.Wasm32, [])));

        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
    }

    [Fact]
    public void RejectsMissingRequest()
    {
        var program = new FakeProgram();
        Assert.Throws<ArgumentNullException>(() =>
            CreateCollector(program, new RecordingJavaScriptParameterTypeResolver())
                .Collect(null!));
    }

    private static IModuleImportCollector CreateCollector(
        FakeProgram program,
        IJavaScriptImportParameterTypeResolver parameters) => new[]
    {
        new ModuleImportCollector(
            program,
            new RecordingLayoutProvider(),
            parameters,
            new CanonicalAbiFunctionTypePlanner(
                new CanonicalAbiSignaturePlanner(new CanonicalAbiTypeFlattener()))),
    }.Cast<IModuleImportCollector>().Single();

    private static WasmFunctionImport Import(string module, string name) => new(
        module,
        name,
        WasmFunctionType.Create(CliValueKind.Void));

    private sealed class RecordingJavaScriptParameterTypeResolver :
        IJavaScriptImportParameterTypeResolver
    {
        public List<bool> CallbackFlags { get; } = [];

        public CliValueKind Resolve(CliTypeIdentity type, bool isCallback)
        {
            CallbackFlags.Add(isCallback);
            return isCallback ? CliValueKind.I8 : CliValueKind.I4;
        }
    }
}
