using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawFinalImportSignatureValidatorTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ValidatesTheCompleteClosureAndPreservesObservedBinaryOrder(WasmTarget target)
    {
        var fixture = new Fixture(target);

        var result = Create(fixture).Validate(fixture.Request());

        Assert.Same(fixture.Plan, result.Plan);
        Assert.True(fixture.Observed.SequenceEqual(result.ObservedImports));
        Assert.Equal(
            fixture.Outputs.Values.OrderBy(value => value.Identity.Module, StringComparer.Ordinal)
                .ThenBy(value => value.Identity.Name, StringComparer.Ordinal).ToArray(),
            result.ExpectedImports.ToArray());
        Assert.Equal(
            [fixture.WitFirst, fixture.WitSecond, fixture.JavaScript, fixture.Runtime],
            fixture.Projected.Select(call => call.Signature.Identity));
        Assert.All(fixture.Projected, call => Assert.Equal(target, call.Target));
        Assert.Equal(fixture.Plan.Imports[0].Signature.Parameters, fixture.Projected[0].Signature.Parameters);
        Assert.Equal(fixture.Plan.Imports[0].Signature.Result, fixture.Projected[0].Signature.Result);
        Assert.Same(fixture.JavaScriptDeclaration, fixture.Projected[2].Signature);
        Assert.Same(fixture.RuntimeDeclaration, fixture.Projected[3].Signature);
    }

    [Fact]
    public void RequiresValidatorDependencyAndRequestGraphBeforeProjection()
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        Assert.Throws<ArgumentNullException>(() => new RawFinalImportSignatureValidator(null!));
        Assert.Throws<ArgumentNullException>(() => Create(fixture).Validate(null!));
        Assert.Throws<ArgumentNullException>(() => Create(fixture).Validate(
            new(null!, [], [], [])));
        Assert.Throws<ArgumentNullException>(() => Create(fixture).Validate(
            fixture.Request() with { Plan = fixture.Plan with { Selection = null! } }));
        Assert.Throws<ArgumentNullException>(() => Create(fixture).Validate(
            fixture.Request() with
            {
                Plan = fixture.Plan with
                {
                    Selection = fixture.Plan.Selection with { Catalog = null! },
                },
            }));
        Assert.Empty(fixture.Projected);
    }

    [Fact]
    public void RequiresEveryCollectionToBeExplicitBeforeProjection()
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        var requests = new[]
        {
            fixture.Request() with { Plan = fixture.Plan with { Imports = default } },
            fixture.Request() with
            {
                Plan = fixture.Plan with
                {
                    Selection = fixture.Plan.Selection with { WitImports = default },
                },
            },
            fixture.Request() with
            {
                Plan = fixture.Plan with
                {
                    Selection = fixture.Plan.Selection with { JavaScriptImports = default },
                },
            },
            fixture.Request() with
            {
                Plan = fixture.Plan with
                {
                    Selection = fixture.Plan.Selection with { RuntimeImports = default },
                },
            },
            fixture.Request() with { JavaScriptImports = default },
            fixture.Request() with { RuntimeImports = default },
            fixture.Request() with { RuntimeCoreImports = default },
            fixture.Request() with { ObservedImports = default },
        };

        Assert.All(requests, request =>
            Assert.Throws<CompilerException>(() => Create(fixture).Validate(request)));
        Assert.Empty(fixture.Projected);
    }

    [Fact]
    public void AcceptsAnAlreadyProjectedRuntimeModuleDeclaration()
    {
        var fixture = new Fixture(WasmTarget.Wasm64);
        var request = fixture.Request() with { RuntimeImports = [] };
        request = request with
        {
            RuntimeCoreImports = [fixture.Outputs[fixture.Runtime]],
        };

        var result = Create(fixture).Validate(request);

        Assert.Equal(fixture.Outputs[fixture.Runtime],
            result.ExpectedImports.Single(import => import.Identity == fixture.Runtime));
        Assert.Equal(3, fixture.Projected.Count);
    }

    [Fact]
    public void RejectsInvalidOrUnselectedCoreRuntimeDeclarations()
    {
        var nullFixture = new Fixture(WasmTarget.Wasm32);
        Assert.Throws<ArgumentNullException>(() => Create(nullFixture).Validate(
            nullFixture.Request() with
            {
                RuntimeImports = [],
                RuntimeCoreImports = [null!],
            }));

        var identityFixture = new Fixture(WasmTarget.Wasm32);
        Assert.Throws<CompilerException>(() => Create(identityFixture).Validate(
            identityFixture.Request() with
            {
                RuntimeImports = [],
                RuntimeCoreImports =
                [
                    identityFixture.Outputs[identityFixture.Runtime] with
                    {
                        Identity = new("different", "member"),
                    },
                ],
            }));

        var malformedFixture = new Fixture(WasmTarget.Wasm32);
        Assert.Throws<CompilerException>(() => Create(malformedFixture).Validate(
            malformedFixture.Request() with
            {
                RuntimeImports = [],
                RuntimeCoreImports =
                [
                    malformedFixture.Outputs[malformedFixture.Runtime] with
                    {
                        Parameters = [(RawCoreValueType)99],
                    },
                ],
            }));
    }

    [Fact]
    public void RejectsUnsupportedTargetBeforeProjection()
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        var request = fixture.Request() with
        {
            Plan = fixture.Plan with
            {
                Selection = fixture.Plan.Selection with
                {
                    Catalog = fixture.Plan.Selection.Catalog with { Target = (WasmTarget)99 },
                },
            },
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => Create(fixture).Validate(request));
        Assert.Empty(fixture.Projected);
    }

    [Fact]
    public void RejectsIncompleteOrNullWitLayoutsBeforeExternalProjection()
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        Assert.Throws<CompilerException>(() => Create(fixture).Validate(fixture.Request() with
        {
            Plan = fixture.Plan with { Imports = fixture.Plan.Imports.RemoveAt(0) },
        }));
        Assert.Throws<CompilerException>(() => Create(fixture).Validate(fixture.Request() with
        {
            Plan = fixture.Plan with { Imports = fixture.Plan.Imports.SetItem(0, null!) },
        }));
        Assert.Empty(fixture.Projected);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RejectsWitIdentityOrTargetDriftBeforeProjection(bool identityDrift, bool targetDrift)
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        var layout = Assert.IsType<RawWitImportLayout.Resource>(fixture.Plan.Imports[0]);
        var changed = new RawWitImportLayout.Resource(layout.Intrinsic with
        {
            Identity = identityDrift ? new("different", "member") : layout.Identity,
            Target = targetDrift ? WasmTarget.Wasm64 : layout.Target,
        });

        Assert.Throws<CompilerException>(() => Create(fixture).Validate(fixture.Request() with
        {
            Plan = fixture.Plan with { Imports = fixture.Plan.Imports.SetItem(0, changed) },
        }));
        Assert.Empty(fixture.Projected);
    }

    [Fact]
    public void RequiresEveryWitCanonicalSignatureBeforeProjection()
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        var layout = Assert.IsType<RawWitImportLayout.Resource>(fixture.Plan.Imports[0]);

        Assert.Throws<ArgumentNullException>(() => Create(fixture).Validate(fixture.Request() with
        {
            Plan = fixture.Plan with
            {
                Imports = fixture.Plan.Imports.SetItem(0,
                    new RawWitImportLayout.Resource(layout.Intrinsic with { Signature = null! })),
            },
        }));
        Assert.Empty(fixture.Projected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExternalDeclarationCountMustMatchSelectedOwnership(bool javaScript)
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        var request = javaScript
            ? fixture.Request() with { JavaScriptImports = [] }
            : fixture.Request() with { RuntimeImports = [] };

        Assert.Throws<CompilerException>(() => Create(fixture).Validate(request));
        Assert.Equal(javaScript ? 2 : 3, fixture.Projected.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectedExternalOwnershipCannotContainDuplicates(bool javaScript)
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        var identity = javaScript ? fixture.JavaScript : fixture.Runtime;
        var declaration = javaScript ? fixture.JavaScriptDeclaration : fixture.RuntimeDeclaration;
        var selection = javaScript
            ? fixture.Plan.Selection with { JavaScriptImports = [identity, identity] }
            : fixture.Plan.Selection with { RuntimeImports = [identity, identity] };
        var request = fixture.Request() with
        {
            Plan = fixture.Plan with { Selection = selection },
            JavaScriptImports = javaScript ? [declaration, declaration] : fixture.Request().JavaScriptImports,
            RuntimeImports = javaScript ? fixture.Request().RuntimeImports : [declaration, declaration],
        };

        Assert.Throws<CompilerException>(() => Create(fixture).Validate(request));
        Assert.Equal(javaScript ? 2 : 3, fixture.Projected.Count);
    }

    [Fact]
    public void ExternalDeclarationsCannotBeNullOrUseAnUnselectedIdentity()
    {
        var nullFixture = new Fixture(WasmTarget.Wasm32);
        Assert.Throws<ArgumentNullException>(() => Create(nullFixture).Validate(
            nullFixture.Request() with { JavaScriptImports = [null!] }));
        Assert.Equal(2, nullFixture.Projected.Count);

        var identityFixture = new Fixture(WasmTarget.Wasm32);
        Assert.Throws<CompilerException>(() => Create(identityFixture).Validate(
            identityFixture.Request() with
            {
                JavaScriptImports = [identityFixture.JavaScriptDeclaration with
                {
                    Identity = new("different", "member"),
                }],
            }));
        Assert.Equal(2, identityFixture.Projected.Count);
    }

    [Fact]
    public void RejectsMultipleDeclaredOwnersAndInvalidProjectorProducts()
    {
        var duplicateFixture = new Fixture(WasmTarget.Wasm32);
        var selection = duplicateFixture.Plan.Selection with
        {
            JavaScriptImports = [duplicateFixture.WitFirst],
        };
        duplicateFixture.Outputs[duplicateFixture.WitFirst] = duplicateFixture.Outputs[duplicateFixture.WitFirst];
        Assert.Throws<CompilerException>(() => Create(duplicateFixture).Validate(
            duplicateFixture.Request() with
            {
                Plan = duplicateFixture.Plan with { Selection = selection },
                JavaScriptImports = [duplicateFixture.JavaScriptDeclaration with
                {
                    Identity = duplicateFixture.WitFirst,
                }],
            }));

        var invalidIdentity = new RawCanonicalImportIdentity("wit", "first");
        var nullFixture = new Fixture(WasmTarget.Wasm32) { NullProjection = invalidIdentity };
        Assert.Throws<ArgumentNullException>(() => Create(nullFixture).Validate(nullFixture.Request()));

        var identityFixture = new Fixture(WasmTarget.Wasm32) { NullIdentityProjection = invalidIdentity };
        Assert.Throws<ArgumentNullException>(() => Create(identityFixture).Validate(identityFixture.Request()));
    }

    [Fact]
    public void RejectsNullAndMalformedObservedImports()
    {
        var malformed = new Func<Fixture, RawCoreFunctionImportSignature>[]
        {
            _ => null!,
            fixture => fixture.Outputs[fixture.WitFirst] with { Identity = null! },
            fixture => fixture.Outputs[fixture.WitFirst] with { Identity = new(" ", "first") },
            fixture => fixture.Outputs[fixture.WitFirst] with { Identity = new("wit", " ") },
            fixture => fixture.Outputs[fixture.WitFirst] with { Parameters = default },
            fixture => fixture.Outputs[fixture.WitFirst] with { Results = default },
            fixture => fixture.Outputs[fixture.WitFirst] with { Parameters = [(RawCoreValueType)99] },
            fixture => fixture.Outputs[fixture.WitFirst] with { Results = [(RawCoreValueType)99] },
        };

        foreach (var createMalformed in malformed)
        {
            var fixture = new Fixture(WasmTarget.Wasm32);
            var observed = fixture.Observed.SetItem(0, createMalformed(fixture));
            Assert.ThrowsAny<Exception>(() => Create(fixture).Validate(
                fixture.Request() with { ObservedImports = observed }));
        }
    }

    [Fact]
    public void RejectsDuplicateExtraAndMissingObservedImports()
    {
        var duplicate = new Fixture(WasmTarget.Wasm32);
        Assert.Throws<CompilerException>(() => Create(duplicate).Validate(duplicate.Request() with
        {
            ObservedImports = duplicate.Observed.Add(duplicate.Observed[0]),
        }));

        var extra = new Fixture(WasmTarget.Wasm32);
        Assert.Throws<CompilerException>(() => Create(extra).Validate(extra.Request() with
        {
            ObservedImports = extra.Observed.Add(new(new("extra", "member"), [], [])),
        }));

        var missing = new Fixture(WasmTarget.Wasm32);
        Assert.Throws<CompilerException>(() => Create(missing).Validate(missing.Request() with
        {
            ObservedImports = missing.Observed.RemoveAt(0),
        }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsObservedParameterOrResultMismatch(bool parameters)
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        var observed = fixture.Observed[0];
        observed = parameters
            ? observed with { Parameters = [RawCoreValueType.F64] }
            : observed with { Results = [RawCoreValueType.F64] };

        Assert.Throws<CompilerException>(() => Create(fixture).Validate(fixture.Request() with
        {
            ObservedImports = fixture.Observed.SetItem(0, observed),
        }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ProjectorFailureStopsWithoutAValidatedProduct(int failingCall)
    {
        var fixture = new Fixture(WasmTarget.Wasm64) { FailingCall = failingCall };

        Assert.Same(fixture.Failure, Assert.Throws<InvalidOperationException>(() =>
            Create(fixture).Validate(fixture.Request())));
        Assert.Equal(failingCall, fixture.Projected.Count);
    }

    private static IRawFinalImportSignatureValidator Create(Fixture fixture) =>
        Assert.IsAssignableFrom<IRawFinalImportSignatureValidator>(
            new RawFinalImportSignatureValidator(fixture));

    private sealed class Fixture : IRawCliCoreSignatureProjector
    {
        public Fixture(WasmTarget target)
        {
            WitFirst = new("wit", "first");
            WitSecond = new("wit", "second");
            JavaScript = new("js", "call");
            Runtime = new("runtime", "watch");
            var world = new WitWorld(0, "main", "example:test@1.0.0", [], []);
            var declaration = new RawWitImportDeclaration.Resource(
                "", new(0, "file", default, 0), CanonicalAbiFunctionKind.ImportedResourceDrop);
            var catalog = new RawWitImportCatalog(
                new([], [], [world], [], "{}"),
                world,
                target,
                ImmutableDictionary<RawCanonicalImportIdentity, RawWitImportDeclaration>.Empty
                    .Add(WitFirst, declaration)
                    .Add(WitSecond, declaration));
            var firstSignature = new CanonicalAbiCoreSignature(
                [CliValueKind.ManagedAddress], CliValueKind.I4, [], [], false, false);
            var secondSignature = new CanonicalAbiCoreSignature(
                [CliValueKind.F8], CliValueKind.Void, [], [], false, false);
            var layouts = ImmutableArray.Create<RawWitImportLayout>(
                new RawWitImportLayout.Resource(new(target, declaration, WitFirst, firstSignature)),
                new RawWitImportLayout.Resource(new(target, declaration, WitSecond, secondSignature)));
            Plan = new(
                new(catalog, [WitFirst, WitSecond], [JavaScript], [Runtime]),
                layouts);
            JavaScriptDeclaration = new(JavaScript, [CliValueKind.NativeInt], CliValueKind.I4);
            RuntimeDeclaration = new(Runtime, [CliValueKind.ManagedReference], CliValueKind.Void);
            var address = target == WasmTarget.Wasm64 ? RawCoreValueType.I64 : RawCoreValueType.I32;
            Outputs = new()
            {
                [WitFirst] = new(WitFirst, [address], [RawCoreValueType.I32]),
                [WitSecond] = new(WitSecond, [RawCoreValueType.F64], []),
                [JavaScript] = new(JavaScript, [address], [RawCoreValueType.I32]),
                [Runtime] = new(Runtime, [address], []),
            };
            Observed = [Outputs[Runtime], Outputs[WitSecond], Outputs[JavaScript], Outputs[WitFirst]];
        }

        public RawCanonicalImportIdentity WitFirst { get; }
        public RawCanonicalImportIdentity WitSecond { get; }
        public RawCanonicalImportIdentity JavaScript { get; }
        public RawCanonicalImportIdentity Runtime { get; }
        public RawWitBindingPlan Plan { get; }
        public RawCliFunctionImportSignature JavaScriptDeclaration { get; }
        public RawCliFunctionImportSignature RuntimeDeclaration { get; }
        public Dictionary<RawCanonicalImportIdentity, RawCoreFunctionImportSignature> Outputs { get; }
        public ImmutableArray<RawCoreFunctionImportSignature> Observed { get; }
        public List<(RawCliFunctionImportSignature Signature, WasmTarget Target)> Projected { get; } = [];
        public int FailingCall { get; init; }
        public InvalidOperationException Failure { get; } = new();
        public RawCanonicalImportIdentity? NullProjection { get; init; }
        public RawCanonicalImportIdentity? NullIdentityProjection { get; init; }

        public RawFinalImportSignatureValidationRequest Request() => new(
            Plan,
            [JavaScriptDeclaration],
            [RuntimeDeclaration],
            Observed);

        public RawCoreFunctionImportSignature Project(
            RawCliFunctionImportSignature signature,
            WasmTarget target)
        {
            Projected.Add((signature, target));
            if (Projected.Count == FailingCall) throw Failure;
            if (signature.Identity == NullProjection) return null!;
            var result = Outputs[signature.Identity];
            return signature.Identity == NullIdentityProjection ? result with { Identity = null! } : result;
        }
    }
}
