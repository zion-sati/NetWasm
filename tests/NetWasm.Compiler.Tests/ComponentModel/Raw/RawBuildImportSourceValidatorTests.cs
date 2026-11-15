// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.ComponentModel.Raw;

public sealed class RawBuildImportSourceValidatorTests
{
    [Fact]
    public void ReadsIndependentWorldsAndDelegatesOnlyAuthorizedDeclarations()
    {
        var stages = new RecordingStages();

        var result = Create(stages).Validate(stages.Request);

        Assert.Same(stages.Result, result);
        Assert.Equal(["document:application.wit", "catalog:application", "declarations",
            "document:runtime.wit", "catalog:runtime", "module:runtime", "module:final",
            "runtime-declarations", "plan", "signatures"], stages.Calls);
        Assert.Same(stages.Request.Source, stages.DeclarationsRequest!.Source);
        Assert.Equal("application", stages.RuntimeRequest!.ApplicationCatalog.World.Name);
        Assert.Equal("runtime", stages.RuntimeRequest.RuntimeCatalog.World.Name);
        Assert.Equal(stages.RuntimeImports.ToArray(), stages.RuntimeRequest.RuntimeImports.ToArray());
        Assert.Equal(stages.FinalImports.Select(import => import.Identity).ToArray(), stages.RuntimeRequest.FinalImports.ToArray());
        Assert.Equal(stages.Declarations.JavaScriptImports.Keys.ToArray(), stages.PlanRequest!.JavaScriptImports.ToArray());
        Assert.Equal(stages.Declarations.RuntimeImports.Keys.OrderBy(identity => identity.Name).ToArray(),
            stages.PlanRequest.RuntimeImports.OrderBy(identity => identity.Name).ToArray());
        Assert.Equal(stages.JavaScript, Assert.Single(stages.SignatureRequest!.JavaScriptImports).Identity);
        Assert.Equal(stages.Compiler, Assert.Single(stages.SignatureRequest.RuntimeImports).Identity);
        Assert.Same(stages.CoreSignature, Assert.Single(stages.SignatureRequest.RuntimeCoreImports));
        Assert.Equal(stages.FinalImports.ToArray(), stages.SignatureRequest.ObservedImports.ToArray());
    }

    [Fact]
    public void FacadeForwardsRuntimeWorldAndCompilerFactsUnchanged()
    {
        var source = new RecordingSource();
        var compilation = new CompilationResult([], null!, null!, null!, 0);
        var request = new RawBuildImportValidationRequest(compilation, "application.wit", "application",
            WasmTarget.Wasm32, new("node", "inspect", "runtime", "tools"), new("node", "inspect", "final", "tools"))
        {
            RuntimeWitPath = "runtime.wit",
            RuntimeWorld = "runtime",
        };

        Assert.Same(source.Result, new RawBuildImportValidator(source).Validate(request));
        Assert.Equal("runtime.wit", source.Request!.RuntimeWitPath);
        Assert.Equal("runtime", source.Request.RuntimeWorld);
        Assert.Equal("application.wit", source.Request.WitPath);
        Assert.Equal("application", source.Request.World);
        Assert.Equal(WasmTarget.Wasm32, source.Request.Target);
        Assert.Equal(compilation.FunctionImports.ToArray(), source.Request.Source.FunctionImports.ToArray());
        Assert.Same(compilation.InteropManifest, source.Request.Source.InteropManifest);
        Assert.Same(request.RuntimeInspection, source.Request.RuntimeInspection);
        Assert.Same(request.FinalInspection, source.Request.FinalInspection);
    }

    [Fact]
    public void InvalidRequestsStopAllCapabilities()
    {
        var stages = new RecordingStages();
        var validator = Create(stages);
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
        foreach (var request in new RawBuildImportSourceValidationRequest[]
        {
            stages.Request with { Source = null! }, stages.Request with { RuntimeInspection = null! },
            stages.Request with { FinalInspection = null! },
        }) Assert.Throws<ArgumentNullException>(() => validator.Validate(request));
        Assert.Throws<ArgumentException>(() => validator.Validate(stages.Request with { WitPath = " " }));
        Assert.Throws<ArgumentException>(() => validator.Validate(stages.Request with { RuntimeWitPath = " " }));
        Assert.Throws<ArgumentOutOfRangeException>(() => validator.Validate(stages.Request with { Target = (WasmTarget)99 }));
        Assert.Empty(stages.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingDocumentStopsBeforeInspection(bool runtime)
    {
        var stages = new RecordingStages { MissingApplication = !runtime, MissingRuntime = runtime };

        Assert.Throws<ArgumentNullException>(() => Create(stages).Validate(stages.Request));
        Assert.DoesNotContain(stages.Calls, call => call.StartsWith("module:", StringComparison.Ordinal));
        Assert.Null(stages.RuntimeRequest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImplicitModuleObservationsStopBeforeDeclarationAndPlanWork(bool runtime)
    {
        var stages = new RecordingStages();
        if (runtime) stages.RuntimeImports = default;
        else stages.FinalImports = default;

        Assert.Equal(DiagnosticCode.ComponentContract,
            Assert.Throws<CompilerException>(() => Create(stages).Validate(stages.Request)).Diagnostic.Code);
        Assert.Null(stages.RuntimeRequest);
        Assert.Null(stages.PlanRequest);
        Assert.Null(stages.SignatureRequest);
    }

    [Fact]
    public void DeclarationFailureStopsPlanningAndValidation()
    {
        var failure = new InvalidOperationException("declaration failure");
        var stages = new RecordingStages { Failure = failure };

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => Create(stages).Validate(stages.Request)));
        Assert.NotNull(stages.RuntimeRequest);
        Assert.Null(stages.PlanRequest);
        Assert.Null(stages.SignatureRequest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void RequiredCapabilitiesCannotBeAbsent(int missing)
    {
        var stages = new RecordingStages();
        Assert.Throws<ArgumentNullException>(() => new RawBuildImportSourceValidator(
            missing == 0 ? null! : stages, missing == 1 ? null! : stages,
            missing == 2 ? null! : stages, missing == 3 ? null! : stages,
            missing == 4 ? null! : stages, missing == 5 ? null! : stages,
            missing == 6 ? null! : stages));
        Assert.Empty(stages.Calls);
    }

    private static IRawBuildImportSourceValidator Create(RecordingStages stages) =>
        Assert.IsAssignableFrom<IRawBuildImportSourceValidator>(new RawBuildImportSourceValidator(
            stages, stages, stages, stages, stages, stages, stages));

    private static WitDocument Document(string name) => new([], [],
        [new(0, name, "example:test@1", [], [])], [], "{}");

    private static RawValidatedBindingPlan EmptyResult()
    {
        var document = Document("empty");
        var catalog = new RawWitImportCatalog(document, document.Worlds[0], WasmTarget.Wasm64,
            ImmutableDictionary<RawCanonicalImportIdentity, RawWitImportDeclaration>.Empty);
        // Construct passive test input; this actor is not a collaborator of the subject.
        return new RawFinalImportSignatureValidator(new UnusedProjector()).Validate(new(
            new(new(catalog, [], [], []), []), [], [], []));
    }

    private sealed class UnusedProjector : IRawCliCoreSignatureProjector
    {
        public RawCoreFunctionImportSignature Project(RawCliFunctionImportSignature signature, WasmTarget target) =>
            throw new InvalidOperationException("empty test result has no signatures");
    }

    private sealed class RecordingSource : IRawBuildImportSourceValidator
    {
        public RawBuildImportSourceValidationRequest? Request { get; private set; }
        public RawValidatedBindingPlan Result { get; } = EmptyResult();
        public RawValidatedBindingPlan Validate(RawBuildImportSourceValidationRequest request)
        {
            Request = request;
            return Result;
        }
    }

    private sealed class RecordingStages : IWitDocumentReader, IRawWitImportCatalogBuilder,
        IRawCompilerImportDeclarationBuilder, IRawModuleImportSignatureReader,
        IRawCoreRuntimeImportDeclarationBuilder, IRawWitBindingPlanBuilder, IRawFinalImportSignatureValidator
    {
        public RawCanonicalImportIdentity JavaScript { get; } = new("consumer", "javascript");
        public RawCanonicalImportIdentity Compiler { get; } = new("runtime", "compiler");
        public RawCanonicalImportIdentity Core { get; } = new("runtime", "core");
        public RawValidatedBindingPlan Result { get; } = EmptyResult();
        public List<string> Calls { get; } = [];
        public bool MissingApplication { get; init; }
        public bool MissingRuntime { get; init; }
        public Exception? Failure { get; init; }
        public RawCompilerImportDeclarationRequest? DeclarationsRequest { get; private set; }
        public RawCoreRuntimeImportDeclarationRequest? RuntimeRequest { get; private set; }
        public RawImportBindingSelectionRequest? PlanRequest { get; private set; }
        public RawFinalImportSignatureValidationRequest? SignatureRequest { get; private set; }
        public RawCoreFunctionImportSignature CoreSignature { get; }
        public RawCompilerImportDeclarations Declarations { get; }
        public ImmutableArray<RawCoreFunctionImportSignature> RuntimeImports { get; set; }
        public ImmutableArray<RawCoreFunctionImportSignature> FinalImports { get; set; }
        public RawBuildImportSourceValidationRequest Request { get; } = new(
            new([], null!), "application.wit", "application", WasmTarget.Wasm64,
            new("node", "inspect", "runtime", "tools"), new("node", "inspect", "final", "tools"))
        { RuntimeWitPath = "runtime.wit", RuntimeWorld = "runtime" };

        public RecordingStages()
        {
            CoreSignature = new(Core, [], []);
            RuntimeImports = [CoreSignature];
            FinalImports = [CoreSignature, new(Compiler, [], []), new(JavaScript, [], [])];
            var empty = ImmutableDictionary<RawCanonicalImportIdentity, RawCliFunctionImportSignature>.Empty;
            Declarations = new(empty.Add(JavaScript, new(JavaScript, [], CliValueKind.Void)),
                empty.Add(Core, new(Core, [], CliValueKind.Void)).Add(Compiler, new(Compiler, [], CliValueKind.Void)));
        }

        public WitDocument Read(string witPath)
        {
            Calls.Add("document:" + witPath);
            var application = witPath == "application.wit";
            return (application ? MissingApplication : MissingRuntime) ? null! : Document(application ? "application" : "runtime");
        }

        public RawWitImportCatalog Build(WitDocument document, WitWorld world, WasmTarget target)
        {
            Calls.Add("catalog:" + world.Name);
            return new(document, world, target, ImmutableDictionary<RawCanonicalImportIdentity, RawWitImportDeclaration>.Empty);
        }

        public RawCompilerImportDeclarations Build(RawCompilerImportDeclarationRequest request)
        {
            Calls.Add("declarations");
            DeclarationsRequest = request;
            return Declarations;
        }

        public ImmutableArray<RawCoreFunctionImportSignature> Read(RawModuleInspectionRequest request)
        {
            Calls.Add("module:" + request.ModulePath);
            return request.ModulePath == "runtime" ? RuntimeImports : FinalImports;
        }

        public ImmutableDictionary<RawCanonicalImportIdentity, RawCoreFunctionImportSignature> Build(RawCoreRuntimeImportDeclarationRequest request)
        {
            Calls.Add("runtime-declarations");
            RuntimeRequest = request;
            if (Failure is not null) throw Failure;
            return ImmutableDictionary<RawCanonicalImportIdentity, RawCoreFunctionImportSignature>.Empty.Add(Core, CoreSignature);
        }

        public RawWitBindingPlan Build(RawImportBindingSelectionRequest request)
        {
            Calls.Add("plan");
            PlanRequest = request;
            return new(new(request.Catalog, [], [JavaScript], [Core, Compiler]), []);
        }

        public RawValidatedBindingPlan Validate(RawFinalImportSignatureValidationRequest request)
        {
            Calls.Add("signatures");
            SignatureRequest = request;
            return Result;
        }
    }
}
