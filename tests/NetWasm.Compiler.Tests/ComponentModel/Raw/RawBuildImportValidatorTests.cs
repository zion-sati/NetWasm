using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Tests.ComponentModel.Raw;

public sealed class RawBuildImportValidatorTests
{
    [Fact]
    public void ValidatesObservedFinalImportsAgainstCompilerAndWitDeclarations()
    {
        var document = Document();
        var runtimeIdentity = new RawCanonicalImportIdentity(
            "netwasm.host.v1", "report_terminal_exception");
        var javaScriptIdentity = new RawCanonicalImportIdentity("consumer.math", "add");
        var witIdentity = new RawCanonicalImportIdentity("cm64p2", "accept");
        var runtimeInspection = new RawModuleInspectionRequest(
            "node", "inspect.mjs", "/tmp/runtime.wasm", "/tmp/binaryen");
        var finalInspection = new RawModuleInspectionRequest(
            "node", "inspect.mjs", "/tmp/final.wasm", "/tmp/binaryen");
        var observed = ImmutableArray.Create(
            new RawCoreFunctionImportSignature(runtimeIdentity,
                [RawCoreValueType.I32, RawCoreValueType.I64, RawCoreValueType.I32], []),
            new RawCoreFunctionImportSignature(witIdentity,
                [RawCoreValueType.I32], [RawCoreValueType.I32]),
            new RawCoreFunctionImportSignature(javaScriptIdentity,
                [RawCoreValueType.I32], [RawCoreValueType.I32]));
        var runtimeImports = ImmutableArray.Create(
            observed[0],
            new RawCoreFunctionImportSignature(
                new("netwasm.application.v1", "resolved_by_linker"), [], []));
        var moduleReader = new RecordingModuleReader(
            runtimeInspection,
            runtimeImports,
            finalInspection,
            observed);
        var services = new ServiceCollection().AddNetWasmCompiler();
        services.AddSingleton<IWitDocumentReader>(new FixedDocumentReader(document));
        services.AddSingleton<IRawModuleImportSignatureReader>(moduleReader);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        var compilation = Compilation(
            [
                Import(runtimeIdentity, CliValueKind.Void,
                    CliValueKind.I4, CliValueKind.NativeInt, CliValueKind.I4),
                Import(javaScriptIdentity, CliValueKind.I4, CliValueKind.I4),
                Import(witIdentity, CliValueKind.I4, CliValueKind.I4),
                Import(new("netwasm.application.v1", "resolved_by_linker")),
            ],
            [new(javaScriptIdentity.Module, javaScriptIdentity.Name, ["i32"], "i32")]);

        var validator = provider.GetRequiredService<IRawBuildImportValidator>();
        var sourceValidator = provider.GetRequiredService<
            IRawBuildImportSourceValidator>();
        var result = validator.Validate(new(
            compilation,
            "/tmp/world.wit",
            "main",
            WasmTarget.Wasm64,
            runtimeInspection,
            finalInspection));
        var sourceResult = sourceValidator.Validate(new(
            new(compilation.FunctionImports, compilation.InteropManifest),
            "/tmp/world.wit",
            "main",
            WasmTarget.Wasm64,
            runtimeInspection,
            finalInspection));

        Assert.Equal(
            [runtimeInspection, finalInspection, runtimeInspection, finalInspection],
            moduleReader.Requests);
        Assert.Equal(observed.ToArray(), result.ObservedImports.ToArray());
        Assert.Equal(3, result.ExpectedImports.Length);
        Assert.Equal([javaScriptIdentity], result.Plan.Selection.JavaScriptImports.ToArray());
        Assert.Equal([runtimeIdentity], result.Plan.Selection.RuntimeImports.ToArray());
        Assert.Equal([witIdentity], result.Plan.Selection.WitImports.ToArray());
        Assert.DoesNotContain(result.ExpectedImports,
            import => import.Identity.Name == "resolved_by_linker");
        Assert.Equal(result.ExpectedImports.Length, sourceResult.ExpectedImports.Length);
        foreach (var (expected, actual) in result.ExpectedImports.Zip(sourceResult.ExpectedImports))
        {
            Assert.Equal(expected.Identity, actual.Identity);
            Assert.Equal(expected.Parameters.ToArray(), actual.Parameters.ToArray());
            Assert.Equal(expected.Results.ToArray(), actual.Results.ToArray());
        }
    }

    [Fact]
    public void RejectsInvalidRequestsAndImplicitObservedImports()
    {
        var document = Document();
        var valid = new RawBuildImportValidationRequest(
            Compilation([], []),
            "/tmp/world.wit",
            null,
            WasmTarget.Wasm64,
            new("node", "inspect.mjs", "/tmp/runtime.wasm", "/tmp/binaryen"),
            new("node", "inspect.mjs", "/tmp/final.wasm", "/tmp/binaryen"));
        var validator = CreateValidator(document, [], []);

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(valid with { Compilation = null! }));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(valid with { RuntimeInspection = null! }));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(valid with { FinalInspection = null! }));
        Assert.Throws<ArgumentException>(() => validator.Validate(valid with { WitPath = "" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => validator.Validate(
            valid with { Target = (WasmTarget)int.MaxValue }));
        Assert.Throws<CompilerException>(() => CreateValidator(document, default, []).Validate(valid));
        Assert.Throws<CompilerException>(() => CreateValidator(document, [], default).Validate(valid));
        Assert.Throws<ArgumentNullException>(() => CreateValidator(null!, [], []).Validate(valid));
    }

    [Fact]
    public void RejectsMissingConstructorDependencies()
    {
        using var provider = new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider();
        var documents = provider.GetRequiredService<IWitDocumentReader>();
        var catalogs = provider.GetRequiredService<IRawWitImportCatalogBuilder>();
        var declarations = provider.GetRequiredService<IRawCompilerImportDeclarationBuilder>();
        var modules = provider.GetRequiredService<IRawModuleImportSignatureReader>();
        var plans = provider.GetRequiredService<IRawWitBindingPlanBuilder>();
        var signatures = provider.GetRequiredService<IRawFinalImportSignatureValidator>();
        var projector = provider.GetRequiredService<IRawCoreRuntimeImportDeclarationBuilder>();

        Assert.Throws<ArgumentNullException>(() => new RawBuildImportSourceValidator(
            null!, catalogs, declarations, modules, plans, signatures, projector));
        Assert.Throws<ArgumentNullException>(() => new RawBuildImportSourceValidator(
            documents, null!, declarations, modules, plans, signatures, projector));
        Assert.Throws<ArgumentNullException>(() => new RawBuildImportSourceValidator(
            documents, catalogs, null!, modules, plans, signatures, projector));
        Assert.Throws<ArgumentNullException>(() => new RawBuildImportSourceValidator(
            documents, catalogs, declarations, null!, plans, signatures, projector));
        Assert.Throws<ArgumentNullException>(() => new RawBuildImportSourceValidator(
            documents, catalogs, declarations, modules, null!, signatures, projector));
        Assert.Throws<ArgumentNullException>(() => new RawBuildImportSourceValidator(
            documents, catalogs, declarations, modules, plans, null!, projector));
        Assert.Throws<ArgumentNullException>(() => new RawBuildImportSourceValidator(
            documents, catalogs, declarations, modules, plans, signatures, null!));
        Assert.Throws<ArgumentNullException>(() => new RawBuildImportValidator(null!));
    }

    [Fact]
    public void KeepsRuntimeModuleWitImportsUnderTheWitOwner()
    {
        var identity = new RawCanonicalImportIdentity("cm64p2", "accept");
        var signature = new RawCoreFunctionImportSignature(
            identity,
            [RawCoreValueType.I32],
            [RawCoreValueType.I32]);
        var request = Request(Compilation([], []));

        var result = CreateValidator(Document(), [signature], [signature]).Validate(request);

        Assert.Equal([identity], result.Plan.Selection.WitImports.ToArray());
        Assert.Empty(result.Plan.Selection.RuntimeImports);
    }

    [Fact]
    public void RejectsRuntimeModuleJavaScriptOwnershipAndDuplicateIdentities()
    {
        var identity = new RawCanonicalImportIdentity("consumer.math", "add");
        var signature = new RawCoreFunctionImportSignature(identity, [], []);
        var compilation = Compilation(
            [Import(identity)],
            [new(identity.Module, identity.Name, [], "void")]);
        Assert.Throws<CompilerException>(() => CreateValidator(
            Document(), [signature], []).Validate(Request(compilation)));

        var runtimeIdentity = new RawCanonicalImportIdentity("runtime", "call");
        var runtime = new RawCoreFunctionImportSignature(runtimeIdentity, [], []);
        Assert.Throws<CompilerException>(() => CreateValidator(
            Document(), [runtime, runtime], []).Validate(Request(Compilation([], []))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsCompilerAndRuntimeModuleSignatureDrift(bool parameters)
    {
        var identity = new RawCanonicalImportIdentity("runtime", "call");
        var compilation = Compilation(
            [Import(identity, CliValueKind.Void, CliValueKind.I4)],
            []);
        var runtime = parameters
            ? new RawCoreFunctionImportSignature(identity, [RawCoreValueType.F64], [])
            : new RawCoreFunctionImportSignature(
                identity,
                [RawCoreValueType.I32],
                [RawCoreValueType.I32]);

        Assert.Throws<CompilerException>(() => CreateValidator(
            Document(), [runtime], []).Validate(Request(compilation)));
    }

    [Fact]
    public void RejectsNullRuntimeModuleImportsAndIdentities()
    {
        Assert.Throws<ArgumentNullException>(() => CreateValidator(
            Document(), [null!], []).Validate(Request(Compilation([], []))));
        Assert.Throws<ArgumentNullException>(() => CreateValidator(
            Document(), [new(null!, [], [])], []).Validate(Request(Compilation([], []))));
    }

    [Theory]
    [InlineData("arbitrary.host", false)]
    [InlineData("arbitrary.host", true)]
    [InlineData("wasi_snapshot_preview1", false)]
    [InlineData("wasi_snapshot_preview1", true)]
    public void UnknownRuntimeObservationsCannotAuthorizeSurvivingFinalImports(string module, bool survives)
    {
        var signature = new RawCoreFunctionImportSignature(new(module, "call"), [], []);
        var validator = CreateValidator(Document(), [signature], survives ? [signature] : []);
        var request = Request(Compilation([], []));

        if (survives)
        {
            Assert.Equal(DiagnosticCode.ComponentContract,
                Assert.Throws<CompilerException>(() => validator.Validate(request)).Diagnostic.Code);
        }
        else Assert.Empty(validator.Validate(request).ExpectedImports);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void SeparateRuntimeWitWorldOwnsFinalSignatures(WasmTarget target, bool drift)
    {
        var pointer = target == WasmTarget.Wasm64 ? RawCoreValueType.I64 : RawCoreValueType.I32;
        var identity = new RawCanonicalImportIdentity(target == WasmTarget.Wasm64 ? "cm64p2" : "cm32p2", "runtime-call");
        var runtime = new RawCoreFunctionImportSignature(identity, [pointer, pointer], []);
        var observed = drift ? runtime with { Parameters = [RawCoreValueType.F64, pointer] } : runtime;
        var validator = CreateValidator(Document(), [runtime], [observed], RuntimeDocument("runtime-call"));
        var request = Request(Compilation([], [], target)) with
        {
            Target = target,
            RuntimeWitPath = "/tmp/runtime.wit",
            RuntimeWorld = "runtime",
        };

        if (drift)
        {
            Assert.Equal(DiagnosticCode.ComponentContract,
                Assert.Throws<CompilerException>(() => validator.Validate(request)).Diagnostic.Code);
        }
        else
        {
            var result = validator.Validate(request);
            Assert.Equal([identity], result.Plan.Selection.RuntimeImports.ToArray());
            Assert.Empty(result.Plan.Selection.WitImports);
            Assert.Equal([pointer, pointer], Assert.Single(result.ExpectedImports).Parameters.ToArray());
        }
    }

    [Fact]
    public void ContradictoryIndependentWitOverlapDoesNotShadowApplicationContract()
    {
        var identity = new RawCanonicalImportIdentity("cm64p2", "accept");
        var observed = new RawCoreFunctionImportSignature(identity, [RawCoreValueType.I32], [RawCoreValueType.I32]);
        var validator = CreateValidator(Document(), [], [observed], RuntimeDocument("accept"));
        var request = Request(Compilation([], [])) with
        {
            RuntimeWitPath = "/tmp/runtime.wit",
            RuntimeWorld = "runtime",
        };

        Assert.Equal(DiagnosticCode.ComponentContract,
            Assert.Throws<CompilerException>(() => validator.Validate(request)).Diagnostic.Code);
    }

    private static WitDocument RuntimeDocument(string name) => new([], [],
        [new(0, "runtime", "example:runtime@1", [new(name, null,
            new(name, [new("value", new WitTypeReference.Primitive("string"))], null,
                new("freestanding")))], [])], [], "{}");

    private static IRawBuildImportValidator CreateValidator(
        WitDocument document,
        ImmutableArray<RawCoreFunctionImportSignature> runtimeImports,
        ImmutableArray<RawCoreFunctionImportSignature> finalImports,
        WitDocument? runtimeDocument = null)
    {
        var services = new ServiceCollection().AddNetWasmCompiler();
        services.AddSingleton<IWitDocumentReader>(new FixedDocumentReader(document, runtimeDocument));
        services.AddSingleton<IRawModuleImportSignatureReader>(new RecordingModuleReader(
            new("node", "inspect.mjs", "/tmp/runtime.wasm", "/tmp/binaryen"),
            runtimeImports,
            new("node", "inspect.mjs", "/tmp/final.wasm", "/tmp/binaryen"),
            finalImports));
        return services.BuildServiceProvider().GetRequiredService<IRawBuildImportValidator>();
    }

    private static WitDocument Document()
    {
        var function = new WitFunction(
            "accept",
            [new("value", new WitTypeReference.Primitive("s32"))],
            new WitTypeReference.Primitive("s32"),
            new("freestanding"));
        return new(
            [],
            [],
            [new(0, "main", "example:test@1", [new("accept", null, function)], [])],
            [],
            "{}");
    }

    private static RawBuildImportValidationRequest Request(CompilationResult compilation) =>
        new(
            compilation,
            "/tmp/world.wit",
            "main",
            WasmTarget.Wasm64,
            new("node", "inspect.mjs", "/tmp/runtime.wasm", "/tmp/binaryen"),
            new("node", "inspect.mjs", "/tmp/final.wasm", "/tmp/binaryen"));

    private static CompilationResult Compilation(
        ImmutableArray<WasmFunctionImport> imports,
        ImmutableArray<HostInteropImport> manifestImports,
        WasmTarget target = WasmTarget.Wasm64) =>
        new([], null!, null!, new(
            1,
            target == WasmTarget.Wasm64 ? "wasm64" : "wasm32",
            new(0, 1, 0),
            new(8, 8, 12, 8, 16, 0, 8),
            manifestImports,
            []), 0)
        {
            FunctionImports = imports,
        };

    private static WasmFunctionImport Import(
        RawCanonicalImportIdentity identity,
        CliValueKind result = CliValueKind.Void,
        params CliValueKind[] parameters) =>
        new(identity.Module, identity.Name, WasmFunctionType.Create(result, parameters));

    private sealed class FixedDocumentReader(WitDocument document, WitDocument? runtimeDocument = null) : IWitDocumentReader
    {
        public WitDocument Read(string witPath) => witPath == "/tmp/runtime.wit" ? runtimeDocument ?? document : document;
    }

    private sealed class RecordingModuleReader(
        RawModuleInspectionRequest runtimeRequest,
        ImmutableArray<RawCoreFunctionImportSignature> runtimeImports,
        RawModuleInspectionRequest finalRequest,
        ImmutableArray<RawCoreFunctionImportSignature> finalImports) : IRawModuleImportSignatureReader
    {
        public List<RawModuleInspectionRequest> Requests { get; } = [];

        public ImmutableArray<RawCoreFunctionImportSignature> Read(
            RawModuleInspectionRequest request)
        {
            Requests.Add(request);
            if (request == runtimeRequest)
            {
                return runtimeImports;
            }
            Assert.Equal(finalRequest, request);
            return finalImports;
        }
    }
}
