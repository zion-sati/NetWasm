using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.ComponentModel.Raw;

public sealed class RawCoreRuntimeImportDeclarationBuilderTests
{
    private static readonly RawCanonicalImportIdentity Identity = new("runtime", "call");

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ProjectsReachedWitImportsInOrdinalModuleAndNameOrder(WasmTarget target)
    {
        RawCanonicalImportIdentity[] identities = [new("z", "call"), new("a", "z"), new("a", "a")];
        var facts = new RecordingFacts();
        var request = Request(target) with
        {
            RuntimeCatalog = Catalog(target, "runtime", identities),
            FinalImports = [.. identities],
        };

        var declarations = Create(facts).Build(request);

        Assert.Equal(2, facts.Plans.Count);
        Assert.Empty(facts.Plans[0].FinalImports);
        Assert.Equal(new RawCanonicalImportIdentity[] { identities[2], identities[1], identities[0] },
            facts.Plans[1].FinalImports.ToArray());
        Assert.Equal(identities.Length, declarations.Count);
        foreach (var identity in identities)
        {
            Assert.Equal(identity, declarations[identity].Identity);
            Assert.Equal(Signature(target).Parameters.ToArray(), declarations[identity].Parameters.ToArray());
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void RuntimeWitAuthorizesIndependentlyOfRuntimeObservations(WasmTarget target, bool observed)
    {
        var facts = new RecordingFacts();
        var signature = Signature(target);
        var request = Request(target) with
        {
            RuntimeCatalog = Catalog(target, "runtime", Identity),
            RuntimeImports = observed ? [signature] : [],
            FinalImports = [Identity],
        };

        var result = Create(facts).Build(request);

        var declaration = Assert.Single(result);
        Assert.Equal(Identity, declaration.Key);
        Assert.Equal(signature.Parameters.ToArray(), declaration.Value.Parameters.ToArray());
        Assert.Empty(declaration.Value.Results);
        Assert.NotSame(signature, declaration.Value);
        Assert.Equal(2, facts.Plans.Count);
        Assert.Same(request.RuntimeCatalog, facts.Plans[1].Catalog);
        Assert.Equal([Identity], facts.Plans[1].FinalImports.ToArray());
        Assert.Empty(facts.Plans[1].JavaScriptImports);
        Assert.Empty(facts.Plans[1].RuntimeImports);
        Assert.Equal(target, Assert.Single(facts.Projections).Target);
    }

    [Fact]
    public void UnknownObservationCannotAuthorizeAnImportEvenWhenItSurvives()
    {
        var facts = new RecordingFacts();
        var request = Request() with { RuntimeImports = [Signature()], FinalImports = [Identity] };

        Assert.Empty(Create(facts).Build(request));
        Assert.All(facts.Plans, plan => Assert.Empty(plan.FinalImports));
        Assert.Empty(facts.Projections);
    }

    // A linked native archive may import from a module the project declares;
    // the observed signature is the declaration the host supplies.
    [Fact]
    public void DeclaredImportModulesAuthorizeTheRuntimeArchiveImports()
    {
        var external = new RawCoreFunctionImportSignature(
            new("host_ui", "create_node"), [RawCoreValueType.I32], [RawCoreValueType.I64]);
        var request = Request() with
        {
            CompilerDeclarations = Declarations() with { ImportModules = ["host_ui"] },
            RuntimeImports = [external, Signature()],
            FinalImports = [external.Identity],
        };

        var declared = Create(new RecordingFacts()).Build(request);

        Assert.Same(external, Assert.Single(declared).Value);
        Assert.Empty(Create(new RecordingFacts()).Build(request with { CompilerDeclarations = Declarations() }));
    }

    [Fact]
    public void EqualWitOverlapRetainsOnlyApplicationOwnershipAndChecksCompilerOverlap()
    {
        var facts = new RecordingFacts();
        var request = Request() with
        {
            ApplicationCatalog = Catalog(WasmTarget.Wasm64, "application", Identity),
            RuntimeCatalog = Catalog(WasmTarget.Wasm64, "runtime", Identity),
            CompilerDeclarations = Declarations(runtime: true),
            RuntimeImports = [Signature()],
            FinalImports = [Identity],
        };

        Assert.Empty(Create(facts).Build(request));
        Assert.Equal(3, facts.Projections.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictingWitOverlapRejectsIndependentlyOfObservation(bool resultDrift)
    {
        var facts = new RecordingFacts
        {
            ApplicationParameter = resultDrift ? CliValueKind.NativeInt : CliValueKind.I4,
            ApplicationResult = resultDrift ? CliValueKind.I4 : CliValueKind.Void,
        };
        var request = Request() with
        {
            ApplicationCatalog = Catalog(WasmTarget.Wasm64, "application", Identity),
            RuntimeCatalog = Catalog(WasmTarget.Wasm64, "runtime", Identity),
            FinalImports = [Identity],
        };

        AssertContractFailure(() => Create(facts).Build(request));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeWitCompilerOverlapMustAgreeWithoutAnyRuntimeObservation(bool resultDrift)
    {
        var facts = new RecordingFacts();
        var declaration = new RawCliFunctionImportSignature(Identity,
            [resultDrift ? CliValueKind.NativeInt : CliValueKind.I4],
            resultDrift ? CliValueKind.I4 : CliValueKind.Void);
        var request = Request() with
        {
            RuntimeCatalog = Catalog(WasmTarget.Wasm64, "runtime", Identity),
            CompilerDeclarations = new(ImmutableDictionary<RawCanonicalImportIdentity, RawCliFunctionImportSignature>.Empty,
                ImmutableDictionary<RawCanonicalImportIdentity, RawCliFunctionImportSignature>.Empty.Add(Identity, declaration)),
            FinalImports = [Identity],
        };

        AssertContractFailure(() => Create(facts).Build(request));
    }

    [Theory]
    [InlineData("application", false)]
    [InlineData("application", true)]
    [InlineData("runtime", false)]
    [InlineData("runtime", true)]
    [InlineData("compiler", false)]
    [InlineData("compiler", true)]
    public void KnownObservationDriftRejectsEvenWhenPruned(string owner, bool resultDrift)
    {
        var facts = new RecordingFacts();
        var signature = resultDrift
            ? Signature() with { Results = [RawCoreValueType.I32] }
            : Signature() with { Parameters = [RawCoreValueType.I32] };
        var request = Request() with
        {
            ApplicationCatalog = Catalog(WasmTarget.Wasm64, "application", owner == "application" ? [Identity] : []),
            RuntimeCatalog = Catalog(WasmTarget.Wasm64, "runtime", owner == "runtime" ? [Identity] : []),
            CompilerDeclarations = Declarations(runtime: owner == "compiler"),
            RuntimeImports = [signature],
        };

        AssertContractFailure(() => Create(facts).Build(request));
    }

    [Fact]
    public void DuplicateApplicationWitObservationsAndJavaScriptCollisionsFailBeforeProjection()
    {
        var request = Request() with
        {
            ApplicationCatalog = Catalog(WasmTarget.Wasm64, "application", Identity),
            RuntimeImports = [Signature(), Signature()],
        };
        var facts = new RecordingFacts();
        AssertContractFailure(() => Create(facts).Build(request));
        Assert.Empty(facts.Plans);

        var javascript = Declarations(javascript: true);
        AssertContractFailure(() => Create(facts).Build(Request() with
        {
            RuntimeImports = [Signature()],
            CompilerDeclarations = javascript,
        }));
        AssertContractFailure(() => Create(facts).Build(Request() with
        {
            RuntimeCatalog = Catalog(WasmTarget.Wasm64, "runtime", Identity),
            FinalImports = [Identity],
            CompilerDeclarations = javascript,
        }));
        Assert.Empty(facts.Plans);
    }

    [Fact]
    public void InvalidPreconditionsNeverCallCollaborators()
    {
        var facts = new RecordingFacts();
        var builder = Create(facts);
        var request = Request();
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
        foreach (var invalid in new RawCoreRuntimeImportDeclarationRequest[]
        {
            request with { ApplicationCatalog = null! }, request with { RuntimeCatalog = null! },
            request with { ApplicationCatalog = request.ApplicationCatalog with { Imports = null! } },
            request with { RuntimeCatalog = request.RuntimeCatalog with { Imports = null! } },
            request with { CompilerDeclarations = null! },
            request with { CompilerDeclarations = request.CompilerDeclarations with { JavaScriptImports = null! } },
            request with { CompilerDeclarations = request.CompilerDeclarations with { RuntimeImports = null! } },
        }) Assert.Throws<ArgumentNullException>(() => builder.Build(invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(request with
        {
            ApplicationCatalog = request.ApplicationCatalog with { Target = (WasmTarget)99 },
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(request with
        {
            RuntimeCatalog = request.RuntimeCatalog with { Target = WasmTarget.Wasm32 },
        }));
        foreach (var invalid in new RawCoreRuntimeImportDeclarationRequest[]
        {
            request with { RuntimeImports = default }, request with { FinalImports = default },
        }) AssertContractFailure(() => builder.Build(invalid));
        Assert.Empty(facts.Plans);
        Assert.Empty(facts.Projections);
    }

    [Fact]
    public void InvalidObservationShapesNeverCallCollaborators()
    {
        var facts = new RecordingFacts();
        var builder = Create(facts);
        foreach (var signature in new RawCoreFunctionImportSignature[]
        {
            Signature() with { Parameters = default }, Signature() with { Results = default },
            Signature() with { Parameters = [(RawCoreValueType)99] },
            Signature() with { Results = [(RawCoreValueType)99] },
        }) AssertContractFailure(() => builder.Build(Request() with { RuntimeImports = [signature] }));
        Assert.Throws<ArgumentNullException>(() => builder.Build(Request() with { RuntimeImports = [null!] }));
        foreach (var identity in new RawCanonicalImportIdentity[] { null!, new(" ", "call"), new("runtime", " ") })
        {
            Assert.ThrowsAny<ArgumentException>(() => builder.Build(Request() with
            {
                RuntimeImports = [Signature() with { Identity = identity }],
            }));
            Assert.ThrowsAny<ArgumentException>(() => builder.Build(Request() with { FinalImports = [identity] }));
        }
        Assert.Empty(facts.Plans);
    }

    [Fact]
    public void RequiredCapabilitiesAndCollaboratorFailuresRemainVisible()
    {
        var facts = new RecordingFacts { Failure = new InvalidOperationException("plan failure") };
        Assert.Throws<ArgumentNullException>(() => new RawCoreRuntimeImportDeclarationBuilder(null!, facts));
        Assert.Throws<ArgumentNullException>(() => new RawCoreRuntimeImportDeclarationBuilder(facts, null!));
        Assert.Same(facts.Failure, Assert.Throws<InvalidOperationException>(() => Create(facts).Build(Request())));
        Assert.Single(facts.Plans);
        Assert.Empty(facts.Projections);
    }

    private static void AssertContractFailure(Action action) =>
        Assert.Equal(DiagnosticCode.ComponentContract, Assert.Throws<CompilerException>(action).Diagnostic.Code);

    private static IRawCoreRuntimeImportDeclarationBuilder Create(RecordingFacts facts) =>
        Assert.IsAssignableFrom<IRawCoreRuntimeImportDeclarationBuilder>(new RawCoreRuntimeImportDeclarationBuilder(facts, facts));

    private static RawCoreFunctionImportSignature Signature(WasmTarget target = WasmTarget.Wasm64) =>
        new(Identity, [target == WasmTarget.Wasm64 ? RawCoreValueType.I64 : RawCoreValueType.I32], []);

    private static RawCoreRuntimeImportDeclarationRequest Request(WasmTarget target = WasmTarget.Wasm64) =>
        new(Catalog(target, "application"), Catalog(target, "runtime"), Declarations(), [], []);

    private static RawCompilerImportDeclarations Declarations(bool runtime = false, bool javascript = false)
    {
        var declaration = new RawCliFunctionImportSignature(Identity, [CliValueKind.NativeInt], CliValueKind.Void);
        var empty = ImmutableDictionary<RawCanonicalImportIdentity, RawCliFunctionImportSignature>.Empty;
        return new(javascript ? empty.Add(Identity, declaration) : empty,
            runtime ? empty.Add(Identity, declaration) : empty);
    }

    private static RawWitImportCatalog Catalog(WasmTarget target, string label, params RawCanonicalImportIdentity[] identities)
    {
        var world = new WitWorld(0, label, "example:test@1", [], []);
        var document = new WitDocument([], [], [world], [], "{}");
        var declaration = new RawWitImportDeclaration.Callable("", new("call", [], null, new("freestanding")));
        return new(document, world, target, identities.ToImmutableDictionary(identity => identity,
            _ => (RawWitImportDeclaration)declaration));
    }

    private sealed class RecordingFacts : IRawWitBindingPlanBuilder, IRawCliCoreSignatureProjector
    {
        public List<RawImportBindingSelectionRequest> Plans { get; } = [];
        public List<(RawCliFunctionImportSignature Signature, WasmTarget Target)> Projections { get; } = [];
        public CliValueKind ApplicationParameter { get; init; } = CliValueKind.NativeInt;
        public CliValueKind ApplicationResult { get; init; } = CliValueKind.Void;
        public Exception? Failure { get; init; }

        public RawWitBindingPlan Build(RawImportBindingSelectionRequest request)
        {
            Plans.Add(request);
            if (Failure is not null) throw Failure;
            var application = request.Catalog.World.Name == "application";
            var signature = new CanonicalAbiCoreSignature(
                [application ? ApplicationParameter : CliValueKind.NativeInt],
                application ? ApplicationResult : CliValueKind.Void, [], [], false, false);
            return new(new(request.Catalog, request.FinalImports, [], []),
                [.. request.FinalImports.Select(identity => new RawWitImportLayout.Resource(
                    new(request.Catalog.Target, null!, identity, signature)))]);
        }

        public RawCoreFunctionImportSignature Project(RawCliFunctionImportSignature signature, WasmTarget target)
        {
            Projections.Add((signature, target));
            return new(signature.Identity,
                [.. signature.Parameters.Select(kind => kind == CliValueKind.NativeInt && target == WasmTarget.Wasm64
                    ? RawCoreValueType.I64 : RawCoreValueType.I32)],
                signature.Result == CliValueKind.Void ? [] : [RawCoreValueType.I32]);
        }
    }
}
