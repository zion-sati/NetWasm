// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.ComponentModel.Raw;

public sealed class RawDeploymentFunctionProjectorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RequiresEveryCollaborator(int missing)
    {
        var fixture = Fixture.Create();

        Assert.Throws<ArgumentNullException>(() => new RawDeploymentFunctionProjector(
            missing == 0 ? null! : fixture.Documents,
            missing == 1 ? null! : fixture.Catalogs,
            missing == 2 ? null! : fixture.Plans,
            missing == 3 ? null! : fixture.Signatures,
            missing == 4 ? null! : new PrimitiveTypeFormatter()));
    }

    [Fact]
    public void SelectsOnlyRuntimeOwnedObservationsAndProjectsStableFunctions()
    {
        var fixture = Fixture.Create();

        var projection = fixture.Subject.Project(
            fixture.Application,
            "runtime.wit",
            "runtime");

        Assert.Equal(
            fixture.RuntimeIdentity,
            Assert.Single(fixture.Plans.Request!.FinalImports));
        Assert.Equal(
            fixture.RuntimeIdentity,
            Assert.Single(fixture.Signatures.Request!.ObservedImports).Identity);
        Assert.Collection(
            projection.RequiredImports,
            function =>
            {
                Assert.Equal("example:application/api@1.0.0", function.Interface);
                Assert.Equal("zeta", function.Name);
                Assert.Empty(function.Parameters);
                Assert.Empty(function.Results);
            },
            function =>
            {
                Assert.Equal("example:runtime/api@1.0.0", function.Interface);
                Assert.Equal("alpha", function.Name);
                Assert.Equal("u32", Assert.Single(function.Parameters));
                Assert.Equal("s32", Assert.Single(function.Results));
            });
        Assert.Same(fixture.Application, projection.AdapterPlans[0]);
        Assert.Same(fixture.Runtime, projection.AdapterPlans[1]);
    }

    [Fact]
    public void CoalescesMatchingApplicationAndRuntimeDeclarations()
    {
        var applicationFunction = Function("same", [new("value", Primitive("u32"))], Primitive("s32"));
        var runtimeFunction = Function("same", [new("value", Primitive("u32"))], Primitive("s32"));
        var fixture = Fixture.Create(
            applicationFunction,
            runtimeFunction,
            "example:shared/api@1.0.0",
            "example:shared/api@1.0.0");

        var projection = fixture.Subject.Project(fixture.Application, "runtime.wit", "runtime");

        Assert.Single(projection.RequiredImports);
    }

    [Fact]
    public void RejectsApplicationAndRuntimeParameterDrift()
    {
        var fixture = Fixture.Create(
            Function("same", [new("value", Primitive("bool"))], Primitive("s32")),
            Function("same", [new("value", Primitive("u32"))], Primitive("s32")),
            "example:shared/api@1.0.0",
            "example:shared/api@1.0.0");

        Assert.Throws<CompilerException>(() =>
            fixture.Subject.Project(fixture.Application, "runtime.wit", "runtime"));
    }

    [Fact]
    public void RejectsApplicationAndRuntimeResultDrift()
    {
        var fixture = Fixture.Create(
            Function("same", [new("value", Primitive("u32"))], Primitive("bool")),
            Function("same", [new("value", Primitive("u32"))], Primitive("s32")),
            "example:shared/api@1.0.0",
            "example:shared/api@1.0.0");

        Assert.Throws<CompilerException>(() =>
            fixture.Subject.Project(fixture.Application, "runtime.wit", "runtime"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-kind")]
    [InlineData("different-definition")]
    [InlineData("blank-interface")]
    public void RejectsApplicationLayoutWithoutItsSelectedWitIdentity(string defect)
    {
        var fixture = Fixture.Create();
        var plan = fixture.Application.Plan;
        var imports = plan.Selection.Catalog.Imports.ToBuilder();
        var declaration = Assert.IsType<RawWitImportDeclaration.Callable>(
            imports[fixture.ApplicationIdentity]);
        switch (defect)
        {
            case "missing":
                imports.Remove(fixture.ApplicationIdentity);
                break;
            case "wrong-kind":
                using (var json = JsonDocument.Parse("\"resource\""))
                {
                    imports[fixture.ApplicationIdentity] = new RawWitImportDeclaration.Resource(
                        declaration.InterfaceName,
                        declaration.DeploymentInterfaceName,
                        new(0, "resource", json.RootElement.Clone(), 0),
                        CanonicalAbiFunctionKind.ImportedResourceDrop);
                }
                break;
            case "different-definition":
                imports[fixture.ApplicationIdentity] = declaration with
                {
                    Definition = declaration.Definition with { Name = "replacement" },
                };
                break;
            case "blank-interface":
                imports[fixture.ApplicationIdentity] = declaration with
                {
                    DeploymentInterfaceName = " ",
                };
                break;
            default:
                throw new InvalidOperationException();
        }
        var catalog = plan.Selection.Catalog with { Imports = imports.ToImmutable() };
        var changedPlan = plan with
        {
            Selection = plan.Selection with { Catalog = catalog },
        };
        var changed = Fixture.Validate(
            changedPlan,
            fixture.Application.ObservedImports);

        Assert.Throws<CompilerException>(() =>
            fixture.Subject.Project(changed, "runtime.wit", "runtime"));
    }

    private static WitTypeReference.Primitive Primitive(string name) =>
        new WitTypeReference.Primitive(name);

    private static WitFunction Function(
        string name,
        ImmutableArray<WitParameter> parameters,
        WitTypeReference? result) =>
        new(name, parameters, result, new("freestanding"));

    private sealed record Fixture(
        FixedDocumentReader Documents,
        FixedCatalogBuilder Catalogs,
        RecordingPlanBuilder Plans,
        RecordingSignatureValidator Signatures,
        RawDeploymentFunctionProjector Subject,
        RawValidatedBindingPlan Application,
        RawValidatedBindingPlan Runtime,
        RawCanonicalImportIdentity ApplicationIdentity,
        RawCanonicalImportIdentity RuntimeIdentity)
    {
        public static Fixture Create(
            WitFunction? applicationFunction = null,
            WitFunction? runtimeFunction = null,
            string applicationInterface = "example:application/api@1.0.0",
            string runtimeInterface = "example:runtime/api@1.0.0")
        {
            applicationFunction ??= Function("zeta", [], null);
            runtimeFunction ??= Function(
                "alpha",
                [new("value", Primitive("u32"))],
                Primitive("s32"));
            var applicationIdentity = new RawCanonicalImportIdentity(
                "cm32p2|example:application/api@1",
                "application-call");
            var runtimeIdentity = new RawCanonicalImportIdentity(
                "cm32p2|example:runtime/api@1",
                "runtime-call");
            var unknownIdentity = new RawCanonicalImportIdentity("unknown", "ignored");
            var applicationDocument = Document("application");
            var runtimeDocument = Document("runtime");
            var application = Plan(
                applicationDocument,
                applicationIdentity,
                applicationInterface,
                applicationFunction,
                [
                    Signature(unknownIdentity),
                    Signature(applicationIdentity),
                    Signature(runtimeIdentity),
                ]);
            var runtime = Plan(
                runtimeDocument,
                runtimeIdentity,
                runtimeInterface,
                runtimeFunction,
                [Signature(runtimeIdentity)]);
            var applicationDeclaration = application.Plan.Selection.Catalog.Imports[
                applicationIdentity];
            var runtimeCatalog = runtime.Plan.Selection.Catalog with
            {
                Imports = runtime.Plan.Selection.Catalog.Imports
                    .Add(applicationIdentity, applicationDeclaration),
            };
            var documents = new FixedDocumentReader(runtimeDocument);
            var catalogs = new FixedCatalogBuilder(runtimeCatalog);
            var plans = new RecordingPlanBuilder(runtime.Plan);
            var signatures = new RecordingSignatureValidator(runtime);
            var subject = new RawDeploymentFunctionProjector(
                documents,
                catalogs,
                plans,
                signatures,
                new PrimitiveTypeFormatter());
            return new(
                documents,
                catalogs,
                plans,
                signatures,
                subject,
                application,
                runtime,
                applicationIdentity,
                runtimeIdentity);
        }

        private static WitDocument Document(string worldName)
        {
            var world = new WitWorld(0, worldName, $"example:{worldName}@1.0.0", [], []);
            return new([], [], [world], [], $"{{\"world\":\"{worldName}\"}}");
        }

        private static RawValidatedBindingPlan Plan(
            WitDocument document,
            RawCanonicalImportIdentity identity,
            string deploymentInterface,
            WitFunction function,
            ImmutableArray<RawCoreFunctionImportSignature> observed)
        {
            var signature = new CanonicalAbiCoreSignature(
                [],
                CliValueKind.Void,
                [],
                [],
                false,
                false);
            var memory = new CanonicalAbiMemoryLayout(0, 1, []);
            var canonical = new CanonicalAbiFunction(
                deploymentInterface,
                function.Name,
                default,
                [],
                null);
            var functionLayout = new RawWitFunctionLayout(
                function,
                new(
                    WasmTarget.Wasm32,
                    canonical,
                    identity.Module,
                    identity.Name,
                    signature,
                    memory,
                    null));
            var layout = new RawWitImportLayout.Callable(functionLayout);
            var declaration = new RawWitImportDeclaration.Callable(
                deploymentInterface,
                deploymentInterface,
                function);
            var world = document.Worlds[0];
            var catalog = new RawWitImportCatalog(
                document,
                world,
                WasmTarget.Wasm32,
                ImmutableDictionary<RawCanonicalImportIdentity, RawWitImportDeclaration>.Empty
                    .Add(identity, declaration));
            var runtimeImports = observed
                .Where(import => import.Identity != identity)
                .Select(import => import.Identity)
                .ToImmutableArray();
            var selection = new RawImportBindingSelection(
                catalog,
                [identity],
                [],
                runtimeImports);
            var plan = new RawWitBindingPlan(selection, [layout]);
            return Validate(plan, observed);
        }

        public static RawValidatedBindingPlan Validate(
            RawWitBindingPlan plan,
            ImmutableArray<RawCoreFunctionImportSignature> observed)
        {
            var runtime = observed
                .Where(import => !plan.Selection.WitImports.Contains(import.Identity))
                .ToImmutableArray();
            return new RawFinalImportSignatureValidator(new RawCliCoreSignatureProjector())
                .Validate(new(plan, [], [], observed)
                {
                    RuntimeCoreImports = runtime,
                });
        }

        private static RawCoreFunctionImportSignature Signature(
            RawCanonicalImportIdentity identity) => new(identity, [], []);
    }

    private sealed class FixedDocumentReader(WitDocument document) : IWitDocumentReader
    {
        public WitDocument Read(string witPath) => document;
    }

    private sealed class FixedCatalogBuilder(RawWitImportCatalog catalog) :
        IRawWitImportCatalogBuilder
    {
        public RawWitImportCatalog Build(
            WitDocument document,
            WitWorld world,
            WasmTarget target) => catalog;
    }

    private sealed class RecordingPlanBuilder(RawWitBindingPlan plan) :
        IRawWitBindingPlanBuilder
    {
        public RawImportBindingSelectionRequest? Request { get; private set; }

        public RawWitBindingPlan Build(RawImportBindingSelectionRequest request)
        {
            Request = request;
            return plan;
        }
    }

    private sealed class RecordingSignatureValidator(RawValidatedBindingPlan validated) :
        IRawFinalImportSignatureValidator
    {
        public RawFinalImportSignatureValidationRequest? Request { get; private set; }

        public RawValidatedBindingPlan Validate(
            RawFinalImportSignatureValidationRequest request)
        {
            Request = request;
            return validated;
        }
    }

    private sealed class PrimitiveTypeFormatter : IWitTypeIdentityFormatter
    {
        public string Format(WitDocument document, WitTypeReference reference) =>
            Assert.IsType<WitTypeReference.Primitive>(reference).Name;
    }
}
