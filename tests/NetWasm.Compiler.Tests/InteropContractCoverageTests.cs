using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class InteropContractCoverageTests
{
    private static readonly AssemblyIdentity Assembly = new("InteropCoverage");
    private static readonly EntityKey TypeKey = new(Assembly, 0x02000001);

    [Fact]
    public void InteropRegistrationRejectsNullAndExposesItsCapabilities()
    {
        IServiceCollection? missing = null;
        Assert.Throws<ArgumentNullException>(() => missing!.AddCompilerInterop());

        var services = new ServiceCollection();
        services.AddCompilerComponentModel();
        services.AddCompilerInterop();
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IHostInteropManifestBuilder>());
        Assert.NotNull(provider.GetRequiredService<IComponentContractResolver>());
    }

    [Fact]
    public void ComponentContractResolverHandlesEmptyConfigurationAndWorldFunctions()
    {
        var document = Document(
            new WitWorld(0, "world", "pkg", [], []));
        var resolver = new ComponentContractResolver(
            new FixedDocuments(document),
            new FixedSignatures(),
            new FixedCanonicalTypes(),
            new FixedBindings());
        var metadata = Snapshot([]);
        var symbols = new Symbols();

        Assert.True(resolver.Resolve(
            metadata,
            symbols,
            Options()).IsEmpty);
        Assert.Throws<CompilerException>(() => resolver.Resolve(
            metadata,
            symbols,
            Options() with { WitWorld = "world" }));

        var method = Method(
            "run",
            1,
            new MethodSignatureModel(
                CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                [CliTypeIdentity.Primitive("i4", CliValueKind.I4)])) with
        {
            WitImport = new WitImportDeclaration("", "run"),
            WitExport = new WitExportDeclaration("", "run-export"),
        };
        var secondExport = method with
        {
            Key = new EntityKey(Assembly, 0x06000002),
            Name = "another-export",
            WitImport = null,
            WitExport = new WitExportDeclaration("", "another-export"),
        };
        var function = new WitFunction(
            "run",
            [new WitParameter("value", new WitTypeReference.Primitive("u32"))],
            new WitTypeReference.Primitive("u32"),
            new WitFunctionKind("sync"));
        var functionDocument = Document(
            new WitWorld(
                0,
                "world",
                "pkg",
                [new WitWorldItem("run", null, function)],
                [
                    new WitWorldItem(
                        "run-export",
                        null,
                        function with { Name = "run-export" }),
                    new WitWorldItem(
                        "another-export",
                        null,
                        function with { Name = "another-export" }),
                ]));
        var functionResolver = new ComponentContractResolver(
            new FixedDocuments(functionDocument),
            new FixedSignatures(),
            new FixedCanonicalTypes(),
            new FixedBindings());
        var contract = functionResolver.Resolve(
            Snapshot([method, secondExport]),
            symbols,
            Options() with { WitPath = "fixture.wit" });

        Assert.Equal("pkg", contract.Package);
        Assert.Equal("world", contract.World);
        Assert.Equal("run", Assert.Single(contract.Imports).FunctionName);
        Assert.Equal(
            ["another-export", "run-export"],
            contract.Exports.Select(function => function.FunctionName));
    }

    [Fact]
    public void ComponentContractResolverValidatesEveryEquivalentImportBinding()
    {
        var function = new WitFunction(
            "run",
            [new WitParameter("value", new WitTypeReference.Primitive("u32"))],
            new WitTypeReference.Primitive("u32"),
            new WitFunctionKind("sync"));
        var document = new WitDocument(
            [new WitPackage(
                0,
                "pkg",
                ImmutableDictionary<string, int>.Empty.Add("iface", 0),
                ImmutableDictionary<string, int>.Empty.Add("world", 0))],
            [new WitInterface(0, "iface", "pkg", [], [function])],
            [new WitWorld(
                0,
                "world",
                "pkg",
                [new WitWorldItem("iface", 0, null)],
                [])],
            [],
            "{}");
        var owned = Method(
            "owned",
            2,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4)) with
        {
            WitImport = new WitImportDeclaration("pkg/iface", "run"),
        };
        var referencedAssembly = new AssemblyIdentity("ReferencedBindings");
        var referenced = owned with
        {
            Key = new EntityKey(referencedAssembly, 0x06000001),
            DeclaringType = new EntityKey(referencedAssembly, 0x02000001),
            Name = "referenced",
        };
        var noise = Method("noise", 3, MethodSignatureModel.Create(CliValueKind.Void));
        var resolver = new ComponentContractResolver(
            new FixedDocuments(document),
            new FixedSignatures(),
            new FixedCanonicalTypes(),
            new WitManagedBindingSelector());
        var options = Options() with { WitPath = "fixture.wit" };

        var contract = resolver.Resolve(
            Snapshot([noise, referenced, owned]),
            new Symbols(),
            options);

        Assert.Equal(owned.Key, Assert.Single(contract.Imports).ManagedMethod);

        var invalidReferenced = referenced with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.I8, CliValueKind.I4),
        };
        var exception = Assert.Throws<CompilerException>(() => resolver.Resolve(
            Snapshot([noise, invalidReferenced, owned]),
            new Symbols(),
            options));

        Assert.Equal("referenced", exception.Diagnostic.Method);
    }

    [Fact]
    public void ComponentContractResolverAcceptsEquivalentReferencedImportBindings()
    {
        var function = new WitFunction(
            "run",
            [new WitParameter("value", new WitTypeReference.Primitive("u32"))],
            new WitTypeReference.Primitive("u32"),
            new WitFunctionKind("sync"));
        var document = new WitDocument(
            [new WitPackage(
                0,
                "pkg",
                ImmutableDictionary<string, int>.Empty.Add("iface", 0),
                ImmutableDictionary<string, int>.Empty.Add("world", 0))],
            [new WitInterface(0, "iface", "pkg", [], [function])],
            [new WitWorld(
                0,
                "world",
                "pkg",
                [new WitWorldItem("iface", 0, null)],
                [])],
            [],
            "{}");
        var firstAssembly = new AssemblyIdentity("A.Bindings");
        var secondAssembly = new AssemblyIdentity("B.Bindings");
        var first = Method(
            "first",
            2,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4)) with
        {
            Key = new EntityKey(firstAssembly, 0x06000001),
            DeclaringType = new EntityKey(firstAssembly, 0x02000001),
            WitImport = new("pkg/iface", "run"),
        };
        var second = first with
        {
            Key = new EntityKey(secondAssembly, 0x06000001),
            DeclaringType = new EntityKey(secondAssembly, 0x02000001),
            Name = "second",
        };
        var resolver = new ComponentContractResolver(
            new FixedDocuments(document),
            new FixedSignatures(),
            new FixedCanonicalTypes(),
            new WitManagedBindingSelector());

        var contract = resolver.Resolve(
            Snapshot([second, first]),
            new Symbols(),
            Options() with { WitPath = "fixture.wit" });

        Assert.Equal(first.Key, Assert.Single(contract.Imports).ManagedMethod);
    }

    [Fact]
    public void ComponentContractResolverRejectsMissingAndAsyncBindings()
    {
        var symbols = new Symbols();
        var function = new WitFunction(
            "run",
            [],
            null,
            new WitFunctionKind("sync"));
        var document = Document(new WitWorld(
            0,
            "world",
            "pkg",
            [new WitWorldItem("run", null, function)],
            []));
        var metadata = Snapshot([]);
        var resolver = new ComponentContractResolver(
            new FixedDocuments(document),
            new FixedSignatures(),
            new FixedCanonicalTypes(),
            new FixedBindings());

        var unbound = resolver.Resolve(
            metadata,
            symbols,
            Options() with { WitPath = "fixture.wit" });
        Assert.False(Assert.Single(unbound.Imports).HasManagedBinding);

        var asyncResolver = new ComponentContractResolver(
            new FixedDocuments(Document(new WitWorld(
                0,
                "world",
                "pkg",
                [new WitWorldItem(
                    "run",
                    null,
                    function with { Kind = new WitFunctionKind("async") })],
                []))),
            new FixedSignatures(),
            new FixedCanonicalTypes(),
            new FixedBindings());
        var unsupported = Assert.Throws<CompilerException>(() => asyncResolver.Resolve(
            metadata,
            symbols,
            Options() with { WitPath = "fixture.wit" }));
        Assert.Contains("asynchronous", unsupported.Message);

        var exportDocument = Document(new WitWorld(
            0,
            "world",
            "pkg",
            [],
            [new WitWorldItem("run", null, function)]));
        var exportResolver = new ComponentContractResolver(
            new FixedDocuments(exportDocument),
            new FixedSignatures(),
            new FixedCanonicalTypes(),
            new FixedBindings());
        var missingExport = Assert.Throws<CompilerException>(() => exportResolver.Resolve(
            metadata,
            symbols,
            Options() with
            {
                WitPath = "fixture.wit",
                WitWorld = "world",
            }));
        Assert.Contains("exactly one matching", missingExport.Message);
    }

    [Fact]
    public void ComponentContractResolverCoversResourceIntrinsicsAndPostReturns()
    {
        using var resourceKind = JsonDocument.Parse("\"resource\"");
        using var scalarKind = JsonDocument.Parse("\"u32\"");
        using var recordKind = JsonDocument.Parse("""{"record":{"fields":[]}}""");
        var resourceDocument = new WitDocument(
            [new WitPackage(
                0,
                "pkg",
                ImmutableDictionary<string, int>.Empty.Add("iface", 0),
                ImmutableDictionary<string, int>.Empty.Add("world", 0))],
            [new WitInterface(
                0,
                "iface",
                "pkg",
                ImmutableDictionary<string, int>.Empty
                    .Add("resource", 0)
                    .Add("scalar", 1)
                    .Add("record", 2),
                [])],
            [new WitWorld(
                0,
                "world",
                "pkg",
                [new WitWorldItem("iface", 0, null)],
                [new WitWorldItem("iface", 0, null)])],
            [
                new WitTypeDefinition(0, "resource", resourceKind.RootElement.Clone(), 0),
                new WitTypeDefinition(1, "scalar", scalarKind.RootElement.Clone(), 0),
                new WitTypeDefinition(2, "record", recordKind.RootElement.Clone(), 0),
            ],
            "{}");
        var interfaceName = "pkg/iface";
        var resourceMethods = new[]
        {
            Method("drop-import", 10, MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4))
                with { WitImport = new WitImportDeclaration(interfaceName, "[resource-drop]resource") },
            Method("new-export", 11, MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4))
                with { WitImport = new WitImportDeclaration(interfaceName, "[export-resource-new]resource") },
            Method("rep-export", 12, MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4))
                with { WitImport = new WitImportDeclaration(interfaceName, "[export-resource-rep]resource") },
            Method("drop-export", 13, MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4))
                with { WitImport = new WitImportDeclaration(interfaceName, "[export-resource-drop]resource") },
            Method("dtor-export", 14, MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4))
                with { WitExport = new WitExportDeclaration(interfaceName, "[resource-dtor]resource") },
        };
        var resourceResolver = new ComponentContractResolver(
            new FixedDocuments(resourceDocument),
            new FixedSignatures(),
            new FixedCanonicalTypes(),
            new FixedBindings());

        var resourceContract = resourceResolver.Resolve(
            Snapshot([.. resourceMethods]),
            new Symbols(),
            Options() with { WitPath = "resources.wit" });

        Assert.Equal(4, resourceContract.Imports.Length);
        Assert.Contains(resourceContract.Imports,
            function => function.Kind == CanonicalAbiFunctionKind.ImportedResourceDrop);
        Assert.Contains(resourceContract.Imports,
            function => function.Kind == CanonicalAbiFunctionKind.ExportedResourceNew &&
                function.Result is not null);
        Assert.Contains(resourceContract.Imports,
            function => function.Kind == CanonicalAbiFunctionKind.ExportedResourceRep &&
                function.Result is not null);
        Assert.Contains(resourceContract.Imports,
            function => function.Kind == CanonicalAbiFunctionKind.ExportedResourceDrop &&
                function.Result is null);
        var destructor = Assert.Single(resourceContract.Exports);
        Assert.Equal(CanonicalAbiFunctionKind.ExportedResourceDestructor, destructor.Kind);
        Assert.Null(destructor.Result);
        Assert.Contains(resourceContract.Exports,
            function => function.Kind == CanonicalAbiFunctionKind.ExportedResourceDestructor &&
                function.Result is null);

        var referencedAssembly = new AssemblyIdentity("GeneratedResourceBindings");
        var equivalentDrop = resourceMethods[0] with
        {
            Key = new EntityKey(referencedAssembly, 0x06000001),
            DeclaringType = new EntityKey(referencedAssembly, 0x02000001),
            Name = "drop-import-alias",
        };
        var aliasResolver = new ComponentContractResolver(
            new FixedDocuments(resourceDocument),
            new FixedSignatures(),
            new FixedCanonicalTypes(),
            new WitManagedBindingSelector());
        var aliasContract = aliasResolver.Resolve(
            Snapshot([.. resourceMethods, equivalentDrop]),
            new Symbols(),
            Options() with { WitPath = "resources.wit" });
        Assert.Contains(aliasContract.Imports,
            function => function.Kind == CanonicalAbiFunctionKind.ImportedResourceDrop &&
                function.ManagedMethod == resourceMethods[0].Key);

        var invalidDrop = equivalentDrop with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.I8, CliValueKind.I4),
        };
        var invalidAlias = Assert.Throws<CompilerException>(() => aliasResolver.Resolve(
            Snapshot([.. resourceMethods, invalidDrop]),
            new Symbols(),
            Options() with { WitPath = "resources.wit" }));
        Assert.Equal("drop-import-alias", invalidAlias.Diagnostic.Method);

        var missingResource = Assert.Throws<CompilerException>(() => resourceResolver.Resolve(
            Snapshot([]),
            new Symbols(),
            Options() with { WitPath = "resources.wit" }));
        Assert.Contains("resource intrinsic", missingResource.Message);

        var noResultFunction = new WitFunction(
            "no-result",
            [],
            null,
            new WitFunctionKind("sync"));
        var noResultDocument = FunctionDocument(noResultFunction, "pkg", "iface", "world");
        var noResultMethod = Method(
            "no-result",
            25,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4)) with
        {
            WitExport = new WitExportDeclaration("pkg/iface", "no-result"),
        };
        var noisyMethod = Method("noise", 26, MethodSignatureModel.Create(CliValueKind.I4));
        var wrongExport = Method(
            "wrong-export",
            29,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4)) with
        {
            WitExport = new WitExportDeclaration("other/iface", "other"),
        };
        var noResultResolver = new ComponentContractResolver(
            new FixedDocuments(noResultDocument),
            new FixedSignatures(),
            new FixedCanonicalTypes(),
            new FixedBindings());
        var noResultContract = noResultResolver.Resolve(
            Snapshot([noisyMethod, wrongExport, noResultMethod]),
            new Symbols(),
            Options() with { WitPath = "no-result.wit" });
        Assert.Null(Assert.Single(noResultContract.Exports).Result);

        var managedAddressResolver = new ComponentContractResolver(
            new FixedDocuments(noResultDocument),
            new ManagedAddressSignatures(),
            new FixedCanonicalTypes(),
            new FixedBindings());
        var managedAddressMethod = noResultMethod with
        {
            Key = new EntityKey(Assembly, 30),
            Signature = MethodSignatureModel.Create(
                CliValueKind.NativeInt,
                CliValueKind.NativeInt),
        };
        Assert.NotNull(managedAddressResolver.Resolve(
            Snapshot([managedAddressMethod]),
            new Symbols(),
            Options() with { WitPath = "no-result.wit" }));

        var wrongParameters = noResultMethod with
        {
            Key = new EntityKey(Assembly, 27),
            Signature = MethodSignatureModel.Create(CliValueKind.I4),
        };
        Assert.Throws<CompilerException>(() => noResultResolver.Resolve(
            Snapshot([wrongParameters]),
            new Symbols(),
            Options() with { WitPath = "no-result.wit" }));
        var wrongResult = noResultMethod with
        {
            Key = new EntityKey(Assembly, 28),
            Signature = MethodSignatureModel.Create(CliValueKind.I8, CliValueKind.I4),
        };
        Assert.Throws<CompilerException>(() => noResultResolver.Resolve(
            Snapshot([wrongResult]),
            new Symbols(),
            Options() with { WitPath = "no-result.wit" }));

        var function = new WitFunction(
            "run",
            [new WitParameter("value", new WitTypeReference.Primitive("u32"))],
            new WitTypeReference.Primitive("u32"),
            new WitFunctionKind("sync"));
        var functionDocument = FunctionDocument(function, "pkg", "iface", "world");
        var validExport = Method(
            "run",
            20,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4)) with
        {
            WitExport = new WitExportDeclaration("pkg/iface", "run"),
        };
        var validPostReturn = Method(
            "run-post",
            21,
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4)) with
        {
            WitPostReturn = new WitPostReturnDeclaration("pkg/iface", "run"),
        };
        var postReturnResolver = new ComponentContractResolver(
            new FixedDocuments(functionDocument),
            new FixedSignatures(),
            new FixedCanonicalTypes(),
            new FixedBindings());
        var validContract = postReturnResolver.Resolve(
            Snapshot([validExport, validPostReturn]),
            new Symbols(),
            Options() with { WitPath = "functions.wit" });
        Assert.Equal(validPostReturn.Key, Assert.Single(validContract.Exports).PostReturnMethod);

        var duplicatePostReturn = Assert.Throws<CompilerException>(() => postReturnResolver.Resolve(
            Snapshot([
                validExport,
                validPostReturn,
                validPostReturn with { Key = new EntityKey(Assembly, 22), Name = "run-post-2" },
            ]),
            new Symbols(),
            Options() with { WitPath = "functions.wit" }));
        Assert.Contains("multiple post-return", duplicatePostReturn.Message);

        var invalidParameters = validPostReturn with
        {
            Key = new EntityKey(Assembly, 23),
            Signature = MethodSignatureModel.Create(CliValueKind.Void),
        };
        var invalidParameterPostReturn = Assert.Throws<CompilerException>(() =>
            postReturnResolver.Resolve(
                Snapshot([validExport, invalidParameters]),
                new Symbols(),
                Options() with { WitPath = "functions.wit" }));
        Assert.Contains("post-return", invalidParameterPostReturn.Message);

        var invalidResult = validPostReturn with
        {
            Key = new EntityKey(Assembly, 24),
            Signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4),
        };
        var invalidResultPostReturn = Assert.Throws<CompilerException>(() =>
            postReturnResolver.Resolve(
                Snapshot([validExport, invalidResult]),
                new Symbols(),
                Options() with { WitPath = "functions.wit" }));
        Assert.Contains("post-return", invalidResultPostReturn.Message);
    }

    [Fact]
    public void ComponentContractResolverKeepsSemanticIdentityAndWorldAliasSeparate()
    {
        var function = new WitFunction(
            "add-value",
            [new WitParameter("value", new WitTypeReference.Primitive("u32"))],
            new WitTypeReference.Primitive("u32"),
            new WitFunctionKind("sync"));
        var package = "example:worker@1.0.0";
        var semanticInterface = package + "/math-api";
        var item = new WitWorldItem("math-alias", 0, null)
        {
            CoreBindingName = "math-alias",
        };
        var document = new WitDocument(
            [new WitPackage(
                0,
                package,
                ImmutableDictionary<string, int>.Empty.Add("math-api", 0),
                ImmutableDictionary<string, int>.Empty.Add("application", 0))],
            [new WitInterface(0, "math-api", package, [], [function])],
            [new WitWorld(0, "application", package, [], [item])],
            [],
            "{}");
        var method = Method(
            "add-value",
            31,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4)) with
        {
            WitExport = new WitExportDeclaration(semanticInterface, "add-value"),
        };
        var resolver = new ComponentContractResolver(
            new FixedDocuments(document),
            new FixedSignatures(),
            new FixedCanonicalTypes(),
            new FixedBindings());

        var contract = resolver.Resolve(
            Snapshot([method]),
            new Symbols(),
            Options() with { WitPath = "worker.wit", WitWorld = "application" });

        var export = Assert.Single(contract.Exports);
        Assert.Equal(semanticInterface, export.InterfaceName);
        Assert.Equal("math-alias", export.CoreInterfaceName);
        Assert.Equal("cm32p2|math-alias|add-value",
            CanonicalAbiNames.Export(export, WasmTarget.Wasm32));
    }

    [Fact]
    public void ComponentContractResolverValidatesItsInjectedCapabilities()
    {
        var document = Document(new WitWorld(0, "world", "pkg", [], []));
        var metadata = Snapshot([]);
        var symbols = new Symbols();
        var options = Options();

        Assert.Throws<ArgumentNullException>(() => new ComponentContractResolver(
            null!, new FixedSignatures(), new FixedCanonicalTypes(), new FixedBindings()));
        Assert.Throws<ArgumentNullException>(() => new ComponentContractResolver(
            new FixedDocuments(document), null!, new FixedCanonicalTypes(), new FixedBindings()));
        Assert.Throws<ArgumentNullException>(() => new ComponentContractResolver(
            new FixedDocuments(document), new FixedSignatures(), null!, new FixedBindings()));
        Assert.Throws<ArgumentNullException>(() => new ComponentContractResolver(
            new FixedDocuments(document), new FixedSignatures(), new FixedCanonicalTypes(), null!));
        Assert.Throws<ArgumentNullException>(() => new ComponentContractResolver(
            new FixedDocuments(document), new FixedSignatures(), new FixedCanonicalTypes(),
            new FixedBindings()).Resolve(null!, symbols, options));
        Assert.Throws<ArgumentNullException>(() => new ComponentContractResolver(
            new FixedDocuments(document), new FixedSignatures(), new FixedCanonicalTypes(),
            new FixedBindings()).Resolve(metadata, null!, options));
        Assert.Throws<ArgumentNullException>(() => new ComponentContractResolver(
            new FixedDocuments(document), new FixedSignatures(), new FixedCanonicalTypes(),
            new FixedBindings()).Resolve(metadata, symbols, null!));
    }

    [Fact]
    public void HostInteropManifestBuilderBuildsEmptyTargetManifestsAndRejectsNulls()
    {
        var builder = new HostInteropManifestBuilder();
        var program = ValidationTestData.Program();
        var exports = ImmutableArray<ProgramExport>.Empty;

        Assert.Throws<ArgumentNullException>(() => builder.Build(null!, exports, SnapshotLayout(), []));
        Assert.Throws<ArgumentNullException>(() => builder.Build(program, exports, null!, []));
        foreach (var module in new[] { "", "netwasm.host.v1", "netwasm:runtime/reactor-host", "wasi:io/streams", "wasi_unstable" })
        {
            Assert.Throws<CompilerException>(() => builder.Build(program, exports, SnapshotLayout(), [module]));
        }
        Assert.Throws<CompilerException>(() => builder.Build(program, exports, SnapshotLayout(), default));
        var wasm32 = builder.Build(program, exports, SnapshotLayout(WasmTarget.Wasm32), []);
        var wasm64 = builder.Build(program, exports, SnapshotLayout(WasmTarget.Wasm64), []);
        Assert.Equal("wasm32", wasm32.Target);
        Assert.Equal("wasm64", wasm64.Target);
        Assert.Empty(wasm32.Imports);
        Assert.Empty(wasm32.Exports);
        Assert.Empty(wasm32.Callbacks);
        Assert.Empty(wasm32.WitImports);
    }

    [Fact]
    public void HostInteropManifestBuilderCoversCallbacksAsyncResultsAndUnsupportedTypes()
    {
        var builder = new HostInteropManifestBuilder();
        var taskImport = Method(
            "task",
            30,
            MethodSignatureModel.Create(TaskType("Task`1", CliTypeIdentity.Primitive("i4", CliValueKind.I4)))) with
        {
            RelativeVirtualAddress = 0,
            JSImport = new InteropImportDeclaration("task", null),
        };
        var valueTaskImport = Method(
            "value-task",
            31,
            MethodSignatureModel.Create(TaskType("ValueTask`1", CliTypeIdentity.Primitive("i4", CliValueKind.I4)))) with
        {
            RelativeVirtualAddress = 0,
            JSImport = new InteropImportDeclaration("value-task", null),
        };
        var voidTaskImport = Method(
            "void-task",
            35,
            MethodSignatureModel.Create(TaskType("Task"))) with
        {
            RelativeVirtualAddress = 0,
            JSImport = new InteropImportDeclaration("void-task", null),
        };
        var callbackImport = Method(
            "callback",
            32,
            MethodSignatureModel.Create(
                JavaScriptType("JSSubscription"),
                CliTypeIdentity.Primitive("i4", CliValueKind.I4))) with
        {
            RelativeVirtualAddress = 0,
            JSImport = new InteropImportDeclaration("callback", null),
        };
        var callbackInvoke = Method(
            "Invoke",
            33,
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4));
        var callback = new HostCallbackDeclaration(
            callbackImport.Key,
            0,
            new MethodInstanceModel(
                callbackInvoke,
                CliTypeIdentity.Named(Assembly, "Callbacks", "Callback", false),
                [],
                callbackInvoke.Signature),
            "callback-export");
        var witImport = Method(
            "wit-import",
            36,
            MethodSignatureModel.Create(CliValueKind.Void)) with
        {
            RelativeVirtualAddress = 0,
            WitImport = new("wasi:http@0.2.11/outgoing-handler", "handle"),
        };
        var equivalentAssembly = new AssemblyIdentity("EquivalentBindings");
        var equivalentWitImport = witImport with
        {
            Key = new EntityKey(equivalentAssembly, 0x06000001),
            DeclaringType = new EntityKey(equivalentAssembly, 0x02000001),
        };
        var program = ValidationTestData.Program() with
        {
            JSImportMethods = [taskImport, valueTaskImport, voidTaskImport, callbackImport],
            HostCallbacks = [callback],
            WitImportMethods = [equivalentWitImport, witImport],
        };

        var manifest = builder.Build(program, [], SnapshotLayout(), []);

        Assert.Equal(4, manifest.Imports.Length);
        Assert.Contains(manifest.Imports, import => import.AsyncReturn == "task");
        Assert.Contains(manifest.Imports, import => import.AsyncReturn == "value-task");
        var callbackManifest = Assert.Single(manifest.Callbacks);
        Assert.Equal("callback-export", callbackManifest.ExportName);
        Assert.Equal("callback", callbackManifest.ImportName);
        var witManifest = Assert.Single(manifest.WitImports);
        Assert.Equal("wasi:http@0.2.11/outgoing-handler", witManifest.Interface);
        Assert.Equal("handle", witManifest.Function);

        var unsupported = Method(
            "unsupported",
            34,
            MethodSignatureModel.Create(
                CliTypeIdentity.Primitive("void", CliValueKind.Void),
                CliTypeIdentity.SzArray(CliTypeIdentity.Primitive("bool", CliValueKind.I4)))) with
        {
            RelativeVirtualAddress = 0,
            JSImport = new InteropImportDeclaration("unsupported", "module"),
        };
        var unsupportedProgram = ValidationTestData.Program() with
        {
            JSImportMethods = [unsupported],
        };
        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build(
            unsupportedProgram,
            [],
            SnapshotLayout(),
            []));
        Assert.Contains("unsupported host ABI type", exception.Message);
    }

    [Fact]
    public void InteropDeclarationValidatorHandlesValidScalarWitDeclarations()
    {
        var validator = new InteropDeclarationValidator(new DelegateTypeRecognizerFactory());
        var typeDefinitions = new FixedTypeDefinitions(
            new TypeDefinitionModel(TypeKey, "", "Program", false, [], []),
            new TypeDefinitionModel(TypeKey, "", "Program", false, [], []));
        var baseTypes = new FixedBaseTypes();
        var methods = new FixedMethods(ImmutableDictionary<EntityKey, MethodDefinitionModel>.Empty);
        var imported = Method("imported", 4,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4)) with
        {
            RelativeVirtualAddress = 0,
            WitImport = new WitImportDeclaration("", "imported"),
        };
        var exported = Method("exported", 5,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4)) with
        {
            WitExport = new WitExportDeclaration("", "exported"),
        };
        var valid = Snapshot([imported, exported]);
        validator.Validate(valid, typeDefinitions, baseTypes, methods, new Symbols());

        var invalid = imported with { IsStatic = false };
        var exception = Assert.Throws<CompilerException>(() => validator.Validate(
            Snapshot([invalid]), typeDefinitions, baseTypes, methods, new Symbols()));
        Assert.Contains("bodyless static", exception.Message);

        Assert.Throws<ArgumentNullException>(() => new InteropDeclarationValidator(null!));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            null!, typeDefinitions, baseTypes, methods, new Symbols()));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            valid, null!, baseTypes, methods, new Symbols()));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            valid, typeDefinitions, null!, methods, new Symbols()));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            valid, typeDefinitions, baseTypes, null!, new Symbols()));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            valid, typeDefinitions, baseTypes, methods, null!));
    }

    [Fact]
    public void InteropDeclarationValidatorCoversMalformedBoundaryDeclarations()
    {
        var validSignature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4);
        var unsupportedObject = CliTypeIdentity.Named(
            Assembly,
            "System",
            "Object",
            false);
        AssertInvalidDeclaration(
            [Method("js-export-object-parameter", 36, VoidSignature(unsupportedObject)) with
            {
                JSExport = new InteropExportDeclaration("js-export-object-parameter"),
            }],
            "JSExport currently supports");
        AssertInvalidDeclaration(
            [Method(
                "js-export-object-result",
                37,
                MethodSignatureModel.Create(unsupportedObject)) with
            {
                JSExport = new InteropExportDeclaration("js-export-object-result"),
            }],
            "JSExport currently supports");
        AssertValidDeclaration(
            [Method(
                "js-export-host-object",
                70,
                MethodSignatureModel.Create(JavaScriptType("JSObject"), JavaScriptType("JSObject"))) with
            {
                JSExport = new InteropExportDeclaration("js-export-host-object"),
            }],
            new DelegateTypeRecognizerFactory(),
            new FixedTypeDefinitions(
                new TypeDefinitionModel(TypeKey, "", "Program", false, [], []),
                new TypeDefinitionModel(TypeKey, "", "Program", false, [], [])),
            new FixedMethods(ImmutableDictionary<EntityKey, MethodDefinitionModel>.Empty));
        AssertInvalidDeclaration(
            [Method("wit-export-object-parameter", 38, VoidSignature(unsupportedObject)) with
            {
                WitExport = new WitExportDeclaration("", "wit-export-object-parameter"),
            }],
            "WitExport currently supports only primitive scalar");
        AssertInvalidDeclaration(
            [Method(
                "wit-export-object-result",
                39,
                MethodSignatureModel.Create(unsupportedObject)) with
            {
                WitExport = new WitExportDeclaration("", "wit-export-object-result"),
            }],
            "WitExport currently supports only primitive scalar");
        var duplicateExport = Method("first", 40, validSignature) with
        {
            JSExport = new InteropExportDeclaration("same"),
        };
        var duplicate = Method("second", 41, validSignature) with
        {
            JSExport = new InteropExportDeclaration("same"),
        };
        AssertInvalidDeclaration([duplicateExport, duplicate], "duplicate JSExport");
        AssertValidDeclaration(
            [Method("default-export-name", 65, validSignature) with
            {
                JSExport = new InteropExportDeclaration(null),
            }],
            new DelegateTypeRecognizerFactory(),
            new FixedTypeDefinitions(
                new TypeDefinitionModel(TypeKey, "", "Program", false, [], []),
                new TypeDefinitionModel(TypeKey, "", "Program", false, [], [])),
            new FixedMethods(ImmutableDictionary<EntityKey, MethodDefinitionModel>.Empty));

        AssertInvalidDeclaration(
            [Method("wit-export-instance", 42, validSignature) with
            {
                IsStatic = false,
                WitExport = new WitExportDeclaration("", "run"),
            }],
            "WitExport");
        AssertInvalidDeclaration(
            [Method("wit-export-declaration", 43, validSignature) with
            {
                RelativeVirtualAddress = 0,
                WitExport = new WitExportDeclaration("", "run"),
            }],
            "WitExport");
        AssertInvalidDeclaration(
            [Method("wit-post-instance", 44, validSignature) with
            {
                IsStatic = false,
                WitPostReturn = new WitPostReturnDeclaration("", "run"),
            }],
            "WitPostReturn");
        AssertInvalidDeclaration(
            [Method("wit-post-declaration", 45, validSignature) with
            {
                RelativeVirtualAddress = 0,
                WitPostReturn = new WitPostReturnDeclaration("", "run"),
            }],
            "WitPostReturn");
        AssertInvalidDeclaration(
            [Method("js-import-body", 46, validSignature) with
            {
                JSImport = new InteropImportDeclaration("run", "module"),
            }],
            "bodyless static");

        var instanceImport = Method("js-import-instance", 66, validSignature) with
        {
            IsStatic = false,
            RelativeVirtualAddress = 0,
            JSImport = new InteropImportDeclaration("run-instance", "module"),
        };
        AssertInvalidDeclaration([instanceImport], "bodyless static");

        var asyncExport = Method(
            "async-export",
            47,
            MethodSignatureModel.Create(
                TaskType("Task`1", CliTypeIdentity.Primitive("i4", CliValueKind.I4)),
                JavaScriptType("JSSubscription"))) with
        {
            JSExport = new InteropExportDeclaration("async-export"),
        };
        AssertInvalidDeclaration([asyncExport], "asynchronous JSExport");
        var validAsyncExport = asyncExport with
        {
            Key = new EntityKey(Assembly, 62),
            Signature = MethodSignatureModel.Create(
                TaskType("Task`1", CliTypeIdentity.Primitive("i4", CliValueKind.I4)),
                CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false),
                CliTypeIdentity.SzArray(CliTypeIdentity.Primitive("f8", CliValueKind.F8)),
                JavaScriptType("JSObject")),
        };
        AssertValidDeclaration(
            [validAsyncExport],
            new DelegateTypeRecognizerFactory(),
            new FixedTypeDefinitions(
                new TypeDefinitionModel(TypeKey, "", "Program", false, [], []),
                new TypeDefinitionModel(TypeKey, "", "Program", false, [], [])),
            new FixedMethods(ImmutableDictionary<EntityKey, MethodDefinitionModel>.Empty));

        var promiseImport = Method(
            "promise-import",
            48,
            MethodSignatureModel.Create(
                TaskType("Task`1", CliTypeIdentity.Primitive("i4", CliValueKind.I4)))) with
        {
            RelativeVirtualAddress = 0,
            JSImport = new InteropImportDeclaration("promise-import", "module") { IsPromise = true },
        };
        AssertInvalidDeclaration([promiseImport], "callback-compatible");

        var malformedPromise = Method(
            "malformed-promise",
            49,
            MethodSignatureModel.Create(JavaScriptType("JSSubscription"))) with
        {
            RelativeVirtualAddress = 0,
            JSImport = new InteropImportDeclaration("malformed-promise", "module")
            {
                IsPromise = true,
            },
        };
        AssertInvalidDeclaration([malformedPromise], "requires success and failure");

        var unsupportedAsyncImport = Method(
            "unsupported-async",
            50,
            MethodSignatureModel.Create(TaskType(
                "Task`1",
                JavaScriptType("JSSubscription")))) with
        {
            RelativeVirtualAddress = 0,
            JSImport = new InteropImportDeclaration("unsupported-async", "module"),
        };
        AssertInvalidDeclaration([unsupportedAsyncImport], "JSImport Task and ValueTask results");
        var unsupportedAsyncExportResult = asyncExport with
        {
            Key = new EntityKey(Assembly, 65),
            Signature = MethodSignatureModel.Create(TaskType(
                "Task`1",
                CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false))),
        };
        AssertInvalidDeclaration([unsupportedAsyncExportResult], "JSExport Task and ValueTask results");
        foreach (var (result, token) in new[]
        {
            (CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false), 67),
            (CliTypeIdentity.SzArray(CliTypeIdentity.Primitive("i4", CliValueKind.I4)), 68),
            (JavaScriptType("JSObject"), 69),
        })
        {
            AssertValidDeclaration(
                [unsupportedAsyncImport with
                {
                    Key = new EntityKey(Assembly, token),
                    Signature = MethodSignatureModel.Create(TaskType("Task`1", result)),
                }],
                new DelegateTypeRecognizerFactory(),
                new FixedTypeDefinitions(
                    new TypeDefinitionModel(TypeKey, "", "Program", false, [], []),
                    new TypeDefinitionModel(TypeKey, "", "Program", false, [], [])),
                new FixedMethods(ImmutableDictionary<EntityKey, MethodDefinitionModel>.Empty));
        }

        var validImport = Method(
            "valid-import",
            63,
            VoidSignature(CliTypeIdentity.Primitive("i4", CliValueKind.I4))) with
        {
            RelativeVirtualAddress = 0,
            JSImport = new InteropImportDeclaration("valid-import", "module"),
        };
        AssertValidDeclaration(
            [validImport],
            new DelegateTypeRecognizerFactory(),
            new FixedTypeDefinitions(
                new TypeDefinitionModel(TypeKey, "", "Program", false, [], []),
                new TypeDefinitionModel(TypeKey, "", "Program", false, [], [])),
            new FixedMethods(ImmutableDictionary<EntityKey, MethodDefinitionModel>.Empty));

        var nonGenericAsyncImport = Method(
            "non-generic-async",
            64,
            MethodSignatureModel.Create(TaskType("Task"))) with
        {
            RelativeVirtualAddress = 0,
            JSImport = new InteropImportDeclaration("non-generic-async", "module"),
        };
        AssertValidDeclaration(
            [nonGenericAsyncImport],
            new DelegateTypeRecognizerFactory(),
            new FixedTypeDefinitions(
                new TypeDefinitionModel(TypeKey, "", "Program", false, [], []),
                new TypeDefinitionModel(TypeKey, "", "Program", false, [], [])),
            new FixedMethods(ImmutableDictionary<EntityKey, MethodDefinitionModel>.Empty));

        var callbacks = CallbackFixture.Create(
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4),
            MethodSignatureModel.Create(CliValueKind.Void));
        var validPromise = CallbackImport(
            "valid-promise",
            51,
            callbacks,
            true,
            callbacks.Success,
            callbacks.Failure);
        AssertValidDeclaration(
            [validPromise],
            callbacks.Factory,
            callbacks.TypeDefinitions,
            callbacks.Methods);

        var invalidSuccessReturn = CallbackFixture.Create(
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4),
            MethodSignatureModel.Create(CliValueKind.Void));
        AssertInvalidDeclaration(
            [CallbackImport(
                "success-return",
                52,
                invalidSuccessReturn,
                true,
                invalidSuccessReturn.Success,
                invalidSuccessReturn.Failure)],
            "Action<T> success",
            invalidSuccessReturn);

        var invalidSuccessParameters = CallbackFixture.Create(
            MethodSignatureModel.Create(CliValueKind.Void),
            MethodSignatureModel.Create(CliValueKind.Void));
        AssertInvalidDeclaration(
            [CallbackImport(
                "success-parameters",
                53,
                invalidSuccessParameters,
                true,
                invalidSuccessParameters.Success,
                invalidSuccessParameters.Failure)],
            "Action<T> success",
            invalidSuccessParameters);

        var invalidFailureReturn = CallbackFixture.Create(
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4),
            MethodSignatureModel.Create(CliValueKind.I4));
        AssertInvalidDeclaration(
            [CallbackImport(
                "failure-return",
                54,
                invalidFailureReturn,
                true,
                invalidFailureReturn.Success,
                invalidFailureReturn.Failure)],
            "Action failure",
            invalidFailureReturn);

        var invalidFailureParameters = CallbackFixture.Create(
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4),
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4));
        AssertInvalidDeclaration(
            [CallbackImport(
                "failure-parameters",
                55,
                invalidFailureParameters,
                true,
                invalidFailureParameters.Success,
                invalidFailureParameters.Failure)],
            "Action failure",
            invalidFailureParameters);

        var ordinaryCallbacks = CallbackFixture.Create(
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4),
            MethodSignatureModel.Create(CliValueKind.Void));
        AssertInvalidDeclaration(
            [CallbackImport(
                "ordinary-count",
                56,
                ordinaryCallbacks,
                false,
                ordinaryCallbacks.Success,
                ordinaryCallbacks.Failure)],
            "exactly one delegate",
            ordinaryCallbacks);
        AssertValidDeclaration(
            [CallbackImport(
                "ordinary-valid",
                57,
                ordinaryCallbacks,
                false,
                ordinaryCallbacks.Success)],
            ordinaryCallbacks.Factory,
            ordinaryCallbacks.TypeDefinitions,
            ordinaryCallbacks.Methods);

        var invalidCallbackReturn = CallbackFixture.Create(
            MethodSignatureModel.Create(JavaScriptType("JSObject")),
            MethodSignatureModel.Create(CliValueKind.Void));
        AssertInvalidDeclaration(
            [CallbackImport(
                "callback-return",
                58,
                invalidCallbackReturn,
                false,
                invalidCallbackReturn.Success)],
            "callbacks support",
            invalidCallbackReturn);

        var invalidCallbackParameter = CallbackFixture.Create(
            VoidSignature(JavaScriptType("JSObject")),
            MethodSignatureModel.Create(CliValueKind.Void));
        AssertInvalidDeclaration(
            [CallbackImport(
                "callback-parameter",
                59,
                invalidCallbackParameter,
                false,
                invalidCallbackParameter.Success)],
            "callbacks support",
            invalidCallbackParameter);

        var invalidCallbackText = CallbackFixture.Create(
            VoidSignature(
                CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference),
                CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference)),
            MethodSignatureModel.Create(CliValueKind.Void));
        AssertInvalidDeclaration(
            [CallbackImport(
                "callback-text",
                60,
                invalidCallbackText,
                false,
                invalidCallbackText.Success)],
            "callbacks support",
            invalidCallbackText);

        AssertInvalidDeclaration(
            [Method(
                "bad-array",
                61,
                VoidSignature(CliTypeIdentity.SzArray(
                    CliTypeIdentity.Primitive("bool", CliValueKind.I4)))) with
            {
                RelativeVirtualAddress = 0,
                JSImport = new InteropImportDeclaration("bad-array", "module"),
            }],
            "JSImport currently supports");
    }

    [Fact]
    public void JavaScriptAsyncBindingResolverValidatesDependenciesAndSynchronousMethods()
    {
        var fixture = AsyncFixture.Create();
        Assert.Throws<ArgumentNullException>(() => new JavaScriptAsyncBindingResolver(
            null!, fixture.TypeDefinitions, fixture.Identities, fixture.Fields,
            fixture.Methods, fixture.MethodInstances, fixture.Symbols, ReactorWorld));
        Assert.Throws<ArgumentNullException>(() => new JavaScriptAsyncBindingResolver(
            fixture.Types, null!, fixture.Identities, fixture.Fields,
            fixture.Methods, fixture.MethodInstances, fixture.Symbols, ReactorWorld));
        Assert.Throws<ArgumentNullException>(() => new JavaScriptAsyncBindingResolver(
            fixture.Types, fixture.TypeDefinitions, null!, fixture.Fields,
            fixture.Methods, fixture.MethodInstances, fixture.Symbols, ReactorWorld));
        Assert.Throws<ArgumentNullException>(() => new JavaScriptAsyncBindingResolver(
            fixture.Types, fixture.TypeDefinitions, fixture.Identities, null!,
            fixture.Methods, fixture.MethodInstances, fixture.Symbols, ReactorWorld));
        Assert.Throws<ArgumentNullException>(() => new JavaScriptAsyncBindingResolver(
            fixture.Types, fixture.TypeDefinitions, fixture.Identities, fixture.Fields,
            null!, fixture.MethodInstances, fixture.Symbols, ReactorWorld));
        Assert.Throws<ArgumentNullException>(() => new JavaScriptAsyncBindingResolver(
            fixture.Types, fixture.TypeDefinitions, fixture.Identities, fixture.Fields,
            fixture.Methods, null!, fixture.Symbols, ReactorWorld));
        Assert.Throws<ArgumentNullException>(() => new JavaScriptAsyncBindingResolver(
            fixture.Types, fixture.TypeDefinitions, fixture.Identities, fixture.Fields,
            fixture.Methods, fixture.MethodInstances, null!, ReactorWorld));
        Assert.Throws<ArgumentNullException>(() => new JavaScriptAsyncBindingResolver(
            fixture.Types, fixture.TypeDefinitions, fixture.Identities, fixture.Fields,
            fixture.Methods, fixture.MethodInstances, fixture.Symbols, null!));

        var resolver = fixture.Resolver;
        Assert.Null(resolver.Resolve(fixture.SynchronousMethod));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!));

        var task = AsyncFixture.TaskMethod("Task");
        var taskBinding = resolver.Resolve(task);
        Assert.NotNull(taskBinding);
        Assert.Equal(JavaScriptAsyncReturnKind.Task, taskBinding.Return.Kind);
        Assert.NotNull(taskBinding.StatusField);
        Assert.Null(taskBinding.ResultField);
        Assert.Equal(new EntityKey(Assembly, 0x0600001a), taskBinding.GetVoidResult!.Definition.Key);
        Assert.Null(taskBinding.CommandEntryPoint);
        Assert.Empty(taskBinding.GetVoidResult.Signature.ParameterSignatureTypes);

        var genericTaskBinding = resolver.Resolve(fixture.GenericTaskMethod);
        Assert.NotNull(genericTaskBinding);
        Assert.Equal(JavaScriptAsyncReturnKind.Task, genericTaskBinding.Return.Kind);
        Assert.NotNull(genericTaskBinding.StatusField);
        Assert.NotNull(genericTaskBinding.ResultField);
        Assert.NotEqual(
            genericTaskBinding.StatusField.Definition.DeclaringType,
            genericTaskBinding.ResultField.Definition.DeclaringType);

        var valueTask = AsyncFixture.TaskMethod("ValueTask");
        var valueTaskBinding = resolver.Resolve(valueTask);
        Assert.NotNull(valueTaskBinding);
        Assert.Equal(JavaScriptAsyncReturnKind.ValueTask, valueTaskBinding.Return.Kind);
        Assert.NotNull(valueTaskBinding.ValueTaskTaskField);
        Assert.NotNull(valueTaskBinding.ValueTaskAsTask);
    }

    [Fact]
    public void JavaScriptAsyncBindingFactoryValidatesAndCreatesItsResolver()
    {
        var fixture = AsyncFixture.Create();
        var factory = new JavaScriptAsyncBindingResolverFactory();
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            null!, fixture.TypeDefinitions, fixture.Identities, fixture.Fields,
            fixture.Methods, fixture.MethodInstances, fixture.Symbols, ReactorWorld));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            fixture.Types, null!, fixture.Identities, fixture.Fields,
            fixture.Methods, fixture.MethodInstances, fixture.Symbols, ReactorWorld));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            fixture.Types, fixture.TypeDefinitions, null!, fixture.Fields,
            fixture.Methods, fixture.MethodInstances, fixture.Symbols, ReactorWorld));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            fixture.Types, fixture.TypeDefinitions, fixture.Identities, null!,
            fixture.Methods, fixture.MethodInstances, fixture.Symbols, ReactorWorld));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            fixture.Types, fixture.TypeDefinitions, fixture.Identities, fixture.Fields,
            null!, fixture.MethodInstances, fixture.Symbols, ReactorWorld));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            fixture.Types, fixture.TypeDefinitions, fixture.Identities, fixture.Fields,
            fixture.Methods, null!, fixture.Symbols, ReactorWorld));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            fixture.Types, fixture.TypeDefinitions, fixture.Identities, fixture.Fields,
            fixture.Methods, fixture.MethodInstances, null!, ReactorWorld));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            fixture.Types, fixture.TypeDefinitions, fixture.Identities, fixture.Fields,
            fixture.Methods, fixture.MethodInstances, fixture.Symbols, null!));
        Assert.NotNull(factory.Create(
            fixture.Types,
            fixture.TypeDefinitions,
            fixture.Identities,
            fixture.Fields,
            fixture.Methods,
            fixture.MethodInstances,
            fixture.Symbols, ReactorWorld));
    }

    private static CompilerOptions Options() => new(
        "entry.dll", [], "Program", "Main", []);

    private static MetadataCompilationSnapshot Snapshot(
        ImmutableArray<MethodDefinitionModel> methods) => new(
        [],
        Assembly,
        [new TypeDefinitionModel(TypeKey, "", "Program", false, [], [])],
        methods,
        methods);

    private static WitDocument Document(WitWorld world) => new(
        ImmutableArray.Create(new WitPackage(
            0,
            world.Package,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty.Add(world.Name, world.Id))),
        [],
        [world],
        [],
        "{}");

    private static WitDocument FunctionDocument(
        WitFunction function,
        string package,
        string interfaceName,
        string worldName) => new(
        [new WitPackage(
            0,
            package,
            ImmutableDictionary<string, int>.Empty.Add(interfaceName, 0),
            ImmutableDictionary<string, int>.Empty.Add(worldName, 0))],
        [new WitInterface(0, interfaceName, package, [], [function])],
        [new WitWorld(
            0,
            worldName,
            package,
            [],
            [new WitWorldItem(interfaceName, 0, null)])],
        [],
        "{}");

    private static CliTypeIdentity TaskType(
        string name,
        CliTypeIdentity result) => CliTypeIdentity.GenericInstantiation(
        CliTypeIdentity.Named(
            new AssemblyIdentity("System.Private.CoreLib"),
            "System.Threading.Tasks",
            name,
            false),
        [result]);

    private static CliTypeIdentity TaskType(string name) => CliTypeIdentity.Named(
        new AssemblyIdentity("System.Private.CoreLib"),
        "System.Threading.Tasks",
        name,
        false);

    private static CliTypeIdentity JavaScriptType(string name) => CliTypeIdentity.Named(
        new AssemblyIdentity("System.Runtime.InteropServices.JavaScript"),
        "System.Runtime.InteropServices.JavaScript",
        name,
        false);

    private static MethodDefinitionModel Method(
        string name,
        int token,
        MethodSignatureModel signature) => new(
        new EntityKey(Assembly, 0x06000000 + token),
        TypeKey,
        name,
        true,
        signature,
        1);

    private static MethodSignatureModel VoidSignature(
        params CliTypeIdentity[] parameters) => new(
        CliTypeIdentity.Primitive("void", CliValueKind.Void),
        [.. parameters]);

    private static void AssertInvalidDeclaration(
        IEnumerable<MethodDefinitionModel> methods,
        string message,
        CallbackFixture? callbacks = null)
    {
        var exception = Assert.Throws<CompilerException>(() => ValidateDeclaration(
            methods,
            callbacks?.Factory,
            callbacks?.TypeDefinitions,
            callbacks?.Methods));
        Assert.Contains(message, exception.Message);
    }

    private static void AssertValidDeclaration(
        IEnumerable<MethodDefinitionModel> methods,
        IDelegateTypeRecognizerFactory factory,
        ITypeDefinitionResolver typeDefinitions,
        IMethodRepository methodRepository) => ValidateDeclaration(
            methods,
            factory,
            typeDefinitions,
            methodRepository);

    private static void ValidateDeclaration(
        IEnumerable<MethodDefinitionModel> methods,
        IDelegateTypeRecognizerFactory? factory = null,
        ITypeDefinitionResolver? typeDefinitions = null,
        IMethodRepository? methodRepository = null) =>
        new InteropDeclarationValidator(factory ?? new DelegateTypeRecognizerFactory()).Validate(
            Snapshot([.. methods]),
            typeDefinitions ?? new FixedTypeDefinitions(
                new TypeDefinitionModel(TypeKey, "", "Program", false, [], []),
                new TypeDefinitionModel(TypeKey, "", "Program", false, [], [])),
            new FixedBaseTypes(),
            methodRepository ?? new FixedMethods(
                ImmutableDictionary<EntityKey, MethodDefinitionModel>.Empty),
            new Symbols());

    private static MethodDefinitionModel CallbackImport(
        string name,
        int token,
        CallbackFixture _,
        bool isPromise,
        params CliTypeIdentity[] callbacks) => Method(
            name,
            token,
            MethodSignatureModel.Create(JavaScriptType("JSSubscription"), callbacks)) with
        {
            RelativeVirtualAddress = 0,
            JSImport = new InteropImportDeclaration(name, "module")
            {
                IsPromise = isPromise,
            },
        };

    private static ManagedLayoutSnapshot SnapshotLayout(
        WasmTarget target = WasmTarget.Wasm32)
    {
        var typeLayouts = new ManagedTypeLayouts(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            ImmutableDictionary<string, ObjectLayout>.Empty
                .Add("System.Array", new ObjectLayout(1, 8, []))
                .Add("System.String", new ObjectLayout(2, 8, []))
                .Add("System.Type", new ObjectLayout(3, 8, [])),
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            new ManagedValueLayoutState(),
            WasmTargetLayout.For(target),
            1,
            2,
            3,
            4);
        return new ManagedLayoutSnapshot(
            typeLayouts,
            new ManagedStaticData(16, [], [], [], [], [], [], [], [], []));
    }

    private sealed class FixedDocuments(WitDocument document) : IWitDocumentReader
    {
        public WitDocument Read(string witPath) => document;
    }

    private sealed class FixedCanonicalTypes : IWitCanonicalTypeResolver
    {
        public CanonicalAbiType Resolve(WitDocument document, WitTypeReference reference) =>
            new(CanonicalAbiTypeKind.U32,
                CliTypeIdentity.Primitive("u4", CliValueKind.I4));
    }

    private sealed class FixedSignatures : ICanonicalAbiSignaturePlanner
    {
        public CanonicalAbiCoreSignature Plan(
            CanonicalAbiFunction abiFunction,
            CanonicalAbiDirection direction) => new(
            [CliValueKind.I4],
            CliValueKind.I4,
            [CliValueKind.I4],
            [CliValueKind.I4],
            false,
            false);
    }

    private sealed class ManagedAddressSignatures : ICanonicalAbiSignaturePlanner
    {
        public CanonicalAbiCoreSignature Plan(
            CanonicalAbiFunction abiFunction,
            CanonicalAbiDirection direction) => new(
            [CliValueKind.ManagedAddress],
            CliValueKind.ManagedAddress,
            [CliValueKind.ManagedAddress],
            [CliValueKind.ManagedAddress],
            false,
            false);
    }

    private sealed class FixedBindings : IWitManagedBindingSelector
    {
        public ImmutableArray<MethodDefinitionModel> Select(
            MetadataCompilationSnapshot metadata,
            IEnumerable<MethodDefinitionModel> candidates) =>
            candidates.ToImmutableArray();
    }

    private sealed class Symbols : ISymbolFormatter
    {
        public string Format(EntityKey key) => key.ToString();
        public string Format(MethodDefinitionModel method) => method.Name;
    }

    private sealed class FixedBaseTypes : IBaseTypeResolver
    {
        public CliTypeIdentity? Resolve(CliTypeIdentity type) => null;
    }

    // A host-driven reactor keeps the asynchronous process boundary, so no command entry is resolved.
    private static readonly PlatformWorldCapabilities ReactorWorld = new(ReactorHost: true, EventLoop: true);

    private sealed class AsyncFixture
    {
        public required ITypeFinder Types { get; init; }
        public required ITypeDefinitionResolver TypeDefinitions { get; init; }
        public required ITypeIdentityResolver Identities { get; init; }
        public required IFieldRepository Fields { get; init; }
        public required IMethodRepository Methods { get; init; }
        public required IMethodInstanceResolver MethodInstances { get; init; }
        public required ISymbolFormatter Symbols { get; init; }
        public required IJavaScriptAsyncBindingResolver Resolver { get; init; }
        public required MethodDefinitionModel SynchronousMethod { get; init; }
        public required MethodDefinitionModel GenericTaskMethod { get; init; }

        public static MethodDefinitionModel TaskMethod(string kind) =>
            Method(
                kind,
                kind == "Task" ? 2 : 3,
                new MethodSignatureModel(
                    CliTypeIdentity.Named(
                        Assembly,
                        "System.Threading.Tasks",
                        kind,
                        false),
                    []));

        public static AsyncFixture Create()
        {
            var taskType = CliTypeIdentity.Named(
                Assembly,
                "System.Threading.Tasks",
                "Task",
                false);
            var taskDefinition = new TypeDefinitionModel(
                TypeKey,
                "System.Threading.Tasks",
                "Task",
                false,
                [
                    new EntityKey(Assembly, 10),
                    new EntityKey(Assembly, 11),
                    new EntityKey(Assembly, 12),
                ],
                [
                    new EntityKey(Assembly, 20),
                    new EntityKey(Assembly, 21),
                    new EntityKey(Assembly, 22),
                    new EntityKey(Assembly, 23),
                    new EntityKey(Assembly, 24),
                    new EntityKey(Assembly, 26),
                    new EntityKey(Assembly, 27),
                ]);
            var valueTaskDefinition = new TypeDefinitionModel(
                new EntityKey(Assembly, 0x02000002),
                "System.Threading.Tasks",
                "ValueTask",
                false,
                [new EntityKey(Assembly, 13)],
                [new EntityKey(Assembly, 25)]);
            var genericTaskDefinition = new TypeDefinitionModel(
                new EntityKey(Assembly, 0x02000003),
                "System.Threading.Tasks",
                "Task`1",
                false,
                [new EntityKey(Assembly, 11)],
                [new EntityKey(Assembly, 21)])
            {
                GenericArity = 1,
            };
            var definitions = new FixedTypeDefinitions(taskDefinition, valueTaskDefinition);
            var sync = Method("Sync", 1, MethodSignatureModel.Create(CliValueKind.I4));
            var types = new FixedTypeFinder(taskDefinition, genericTaskDefinition);
            var identities = new FixedIdentities();
            var fieldDefinitions = ImmutableDictionary<EntityKey, FieldDefinitionModel>.Empty
                .Add(new EntityKey(Assembly, 10), new FieldDefinitionModel(
                    new EntityKey(Assembly, 10), TypeKey, "_status",
                    CliTypeIdentity.Primitive("i4", CliValueKind.I4), false))
                .Add(new EntityKey(Assembly, 11), new FieldDefinitionModel(
                    new EntityKey(Assembly, 11), genericTaskDefinition.Key, "_result",
                    CliTypeIdentity.Primitive("i4", CliValueKind.I4), false))
                .Add(new EntityKey(Assembly, 12), new FieldDefinitionModel(
                    new EntityKey(Assembly, 12), TypeKey, "_task",
                    taskType, false))
                .Add(new EntityKey(Assembly, 13), new FieldDefinitionModel(
                    new EntityKey(Assembly, 13), valueTaskDefinition.Key, "_task",
                    taskType, false));
            var fields = new FixedFields(fieldDefinitions);
            var methodDefinitions = ImmutableDictionary<EntityKey, MethodDefinitionModel>.Empty
                .Add(new EntityKey(Assembly, 26), Method("GetVoidResult", 26,
                    MethodSignatureModel.Create(CliValueKind.Void)))
                .Add(new EntityKey(Assembly, 27), Method("GetVoidResult", 27,
                    MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4)))
                .Add(new EntityKey(Assembly, 20), Method(
                    "SetResult", 20, MethodSignatureModel.Create(CliValueKind.Void)))
                .Add(new EntityKey(Assembly, 21), Method("SetResult", 21,
                    MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4)) with
                {
                    DeclaringType = genericTaskDefinition.Key,
                })
                .Add(new EntityKey(Assembly, 22), Method("SetException", 22,
                    MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4)))
                .Add(new EntityKey(Assembly, 23), Method("SetCanceledForJavaScriptInterop", 23,
                    MethodSignatureModel.Create(CliValueKind.Void)))
                .Add(new EntityKey(Assembly, 24), Method(
                    "AsTask", 24, MethodSignatureModel.Create(CliValueKind.Void)))
                .Add(new EntityKey(Assembly, 25), Method(
                    "AsTask", 25, MethodSignatureModel.Create(CliValueKind.Void)));
            var methods = new FixedMethods(methodDefinitions);
            var instances = new FixedMethodInstances();
            var symbols = new Symbols();
            return new AsyncFixture
            {
                Types = types,
                TypeDefinitions = definitions,
                Identities = identities,
                Fields = fields,
                Methods = methods,
                MethodInstances = instances,
                Symbols = symbols,
                Resolver = new JavaScriptAsyncBindingResolver(
                    types, definitions, identities, fields, methods, instances, symbols, ReactorWorld),
                SynchronousMethod = sync,
                GenericTaskMethod = Method(
                    "GenericTask",
                    4,
                    MethodSignatureModel.Create(TaskType(
                        "Task`1",
                        CliTypeIdentity.Primitive("i4", CliValueKind.I4)))),
            };
        }
    }

    private sealed class FixedTypeFinder(
        TypeDefinitionModel task,
        TypeDefinitionModel genericTask) : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) =>
            fullName.EndsWith("`1", StringComparison.Ordinal)
                ? genericTask
                : task;
    }

    private sealed class FixedTypeDefinitions(
        TypeDefinitionModel task,
        TypeDefinitionModel valueTask) :
        ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            identity.FullName?.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal)
                == true
                ? valueTask
                : task;
    }

    private sealed class FixedIdentities : ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey type) =>
            CliTypeIdentity.Named(Assembly, "System", "Task", false);
    }

    private sealed class FixedFields(
        ImmutableDictionary<EntityKey, FieldDefinitionModel> fields) : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => fields[key];
    }

    private sealed class FixedMethods(
        ImmutableDictionary<EntityKey, MethodDefinitionModel> methods) : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => methods[key];
    }

    private sealed class CallbackFixture
    {
        public required CliTypeIdentity Success { get; init; }
        public required CliTypeIdentity Failure { get; init; }
        public required IDelegateTypeRecognizerFactory Factory { get; init; }
        public required ITypeDefinitionResolver TypeDefinitions { get; init; }
        public required IMethodRepository Methods { get; init; }

        public static CallbackFixture Create(
            MethodSignatureModel success,
            MethodSignatureModel failure)
        {
            var successType = CliTypeIdentity.Named(
                Assembly,
                "Callbacks",
                "Success",
                false);
            var failureType = CliTypeIdentity.Named(
                Assembly,
                "Callbacks",
                "Failure",
                false);
            var successInvoke = Method("Invoke", 100, success);
            var failureInvoke = Method("Invoke", 101, failure);
            var successDefinition = new TypeDefinitionModel(
                new EntityKey(Assembly, 0x02000010),
                "Callbacks",
                "Success",
                false,
                [],
                [successInvoke.Key]);
            var failureDefinition = new TypeDefinitionModel(
                new EntityKey(Assembly, 0x02000011),
                "Callbacks",
                "Failure",
                false,
                [],
                [failureInvoke.Key]);
            return new CallbackFixture
            {
                Success = successType,
                Failure = failureType,
                Factory = new FixedDelegateFactory(
                    successType.CanonicalName,
                    failureType.CanonicalName),
                TypeDefinitions = new FixedDelegateTypeDefinitions(
                    ImmutableDictionary<string, TypeDefinitionModel>.Empty
                        .Add(successType.CanonicalName, successDefinition)
                        .Add(failureType.CanonicalName, failureDefinition)),
                Methods = new FixedMethods(
                    ImmutableDictionary<EntityKey, MethodDefinitionModel>.Empty
                        .Add(successInvoke.Key, successInvoke)
                        .Add(failureInvoke.Key, failureInvoke)),
            };
        }
    }

    private sealed class FixedDelegateFactory(params string[] delegateNames) :
        IDelegateTypeRecognizerFactory
    {
        public IDelegateTypeRecognizer Create(
            ITypeDefinitionResolver typeDefinitions,
            IBaseTypeResolver baseTypes) => new FixedDelegateRecognizer(delegateNames);
    }

    private sealed class FixedDelegateRecognizer(IEnumerable<string> delegateNames) :
        IDelegateTypeRecognizer
    {
        private readonly ImmutableHashSet<string> _delegateNames =
            delegateNames.ToImmutableHashSet(StringComparer.Ordinal);

        public bool Recognize(CliTypeIdentity type) =>
            _delegateNames.Contains(type.CanonicalName);
    }

    private sealed class FixedDelegateTypeDefinitions(
        ImmutableDictionary<string, TypeDefinitionModel> definitions) :
        ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            definitions[identity.CanonicalName];
    }

    private sealed class FixedMethodInstances : IMethodInstanceResolver
    {
        public MethodInstanceModel ResolveMethodInstance(
            AssemblyIdentity source,
            int metadataToken,
            string methodDisplayName,
            int ilOffset,
            CliGenericContext? genericContext = null) =>
            new(
                Method("Method", 0, MethodSignatureModel.Create(CliValueKind.Void)) with { Key = new(source, metadataToken) },
                CliTypeIdentity.Named(Assembly, "System", "Task", false),
                [],
                Method("Method", metadataToken, MethodSignatureModel.Create(CliValueKind.Void)).Signature);
    }
}
