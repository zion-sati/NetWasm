using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Functions;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.ComponentModel.Raw;

public sealed class RawBindingServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, "cm32p2")]
    [InlineData(WasmTarget.Wasm64, "cm64p2")]
    public void NamedBindingsFlowFromCatalogThroughLayoutsAndAdapter(
        WasmTarget target,
        string prefix)
    {
        using var provider = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var resourceJson = JsonDocument.Parse("\"resource\"");
        var resource = new WitTypeDefinition(
            0, "file", resourceJson.RootElement.Clone(), 0);
        var function = new WitFunction(
            "fetch", [], null, new("freestanding"));
        var definition = new WitInterface(
            0,
            "api",
            "example:files@1.0.0",
            ImmutableDictionary<string, int>.Empty.Add("file", 0),
            [function]);
        var world = new WitWorld(
            0,
            "main",
            "example:files@1.0.0",
            [new WitWorldItem("input-alias", 0, null)
            {
                CoreBindingName = "input-alias",
            }],
            [new WitWorldItem("output-alias", 0, null)
            {
                CoreBindingName = "output-alias",
            }]);
        var document = new WitDocument([], [definition], [world], [resource], "{}");

        var catalog = provider.GetRequiredService<IRawWitImportCatalogBuilder>()
            .Build(document, world, target);
        var plan = provider.GetRequiredService<IRawWitBindingPlanBuilder>()
            .Build(new(catalog, [.. catalog.Imports.Keys], [], []));
        var projector = provider.GetRequiredService<IRawCliCoreSignatureProjector>();
        var observed = plan.Imports.Select(layout => projector.Project(
            new RawCliFunctionImportSignature(
                layout.Identity,
                layout.Signature.Parameters,
                layout.Signature.Result),
            target)).ToImmutableArray();
        var validated = provider.GetRequiredService<IRawFinalImportSignatureValidator>()
            .Validate(new(plan, [], [], observed));
        var source = Encoding.UTF8.GetString(
            provider.GetRequiredService<IRawAdapterWriter>().Write(validated));

        Assert.Contains(plan.Imports, layout =>
            layout.Identity == new RawCanonicalImportIdentity(
                $"{prefix}|input-alias", "fetch"));
        Assert.Contains(plan.Imports, layout =>
            layout.Identity == new RawCanonicalImportIdentity(
                $"{prefix}|input-alias", "file_drop"));
        foreach (var suffix in new[] { "new", "rep", "drop" })
        {
            Assert.Contains(plan.Imports, layout =>
                layout.Identity == new RawCanonicalImportIdentity(
                    $"{prefix}|_ex_output-alias", "file_" + suffix));
        }
        Assert.Contains($"\"destructor\":\"{prefix}|output-alias|file_dtor\"",
            source,
            StringComparison.Ordinal);
        // The deployment names the placement as the world does, as Jco imports it.
        Assert.Contains("\"interface\":\"input-alias\"",
            source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PublicCompositionPlansEveryImportedAndExportedResourceIntrinsic(WasmTarget target)
    {
        using var provider = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var resourceJson = JsonDocument.Parse("\"resource\"");
        var resource = new WitTypeDefinition(0, "file", resourceJson.RootElement.Clone(), 0);
        var definition = new WitInterface(0, "api", "example:files@1.0.0", ImmutableDictionary<string, int>.Empty.Add("file", 0), []);
        var world = new WitWorld(0, "main", "example:files@1.0.0", [new("example:files/api@1.0.0", 0, null)], [new("example:files/api@1.0.0", 0, null)]);
        var document = new WitDocument([], [definition], [world], [resource], "{}");

        var catalog = provider.GetRequiredService<IRawWitImportCatalogBuilder>().Build(document, world, target);
        var plan = provider.GetRequiredService<IRawWitBindingPlanBuilder>().Build(new(catalog, [.. catalog.Imports.Keys], [], []));

        Assert.Equal(4, plan.Imports.Length);
        Assert.Equal(4, plan.Selection.WitImports.Length);
        Assert.Same(document, plan.Selection.Catalog.Document);
        Assert.All(plan.Imports, layout =>
        {
            var intrinsic = Assert.IsType<RawWitImportLayout.Resource>(layout).Intrinsic;
            Assert.Same(resource, intrinsic.Declaration.Definition);
            Assert.Equal(target, layout.Target);
            Assert.Collection(layout.Signature.Parameters, kind => Assert.Equal(CliValueKind.I4, kind));
            var returnsHandle = intrinsic.Declaration.Kind is CanonicalAbiFunctionKind.ExportedResourceNew or CanonicalAbiFunctionKind.ExportedResourceRep;
            Assert.Equal(returnsHandle ? CliValueKind.I4 : CliValueKind.Void, layout.Signature.Result);
            Assert.False(layout.Signature.IndirectParameters);
            Assert.False(layout.Signature.IndirectResult);
        });
        Assert.Collection(plan.Imports.Cast<RawWitImportLayout.Resource>().Select(layout => layout.Intrinsic.Declaration.Kind).Order(),
            kind => Assert.Equal(CanonicalAbiFunctionKind.ImportedResourceDrop, kind),
            kind => Assert.Equal(CanonicalAbiFunctionKind.ExportedResourceNew, kind),
            kind => Assert.Equal(CanonicalAbiFunctionKind.ExportedResourceRep, kind),
            kind => Assert.Equal(CanonicalAbiFunctionKind.ExportedResourceDrop, kind));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, "cm32p2", 8)]
    [InlineData(WasmTarget.Wasm64, "cm64p2", 16)]
    public void PublicCompositionSelectsAndLaysOutTheFinalWitFunction(WasmTarget target, string module, int parameterSize)
    {
        using var provider = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var function = new WitFunction("accept", [new("text", new WitTypeReference.Primitive("string"))],
            new WitTypeReference.Primitive("bool"), new("freestanding"));
        var world = new WitWorld(0, "main", "example:app@1.0.0", [new("accept", null, function)], []);
        var document = new WitDocument([], [], [world], [], "{}");

        var catalog = provider.GetRequiredService<IRawWitImportCatalogBuilder>().Build(document, world, target);
        var identity = Assert.Single(catalog.Imports.Keys);
        var javaScriptIdentity = new RawCanonicalImportIdentity("netwasm.host.v1", "call");
        var runtimeIdentity = new RawCanonicalImportIdentity(
            $"{(target == WasmTarget.Wasm64 ? "cm64p2" : "cm32p2")}|netwasm:runtime/reactor-host@1",
            "watch");
        var selected = provider.GetRequiredService<IRawImportBindingSelector>().SelectBindings(
            new(catalog, [identity, javaScriptIdentity, runtimeIdentity], [javaScriptIdentity], [runtimeIdentity]));
        var declaration = Assert.IsType<RawWitImportDeclaration.Callable>(catalog.Imports[Assert.Single(selected.WitImports)]);
        var layout = provider.GetRequiredService<IRawWitFunctionLayoutBuilder>().Build(
            document,
            declaration.InterfaceName,
            declaration.CoreInterfaceName,
            declaration.Definition,
            target);
        var plan = provider.GetRequiredService<IRawWitBindingPlanBuilder>().Build(
            new(catalog, [identity, javaScriptIdentity, runtimeIdentity], [javaScriptIdentity], [runtimeIdentity]));
        var address = target == WasmTarget.Wasm64 ? RawCoreValueType.I64 : RawCoreValueType.I32;
        var validated = provider.GetRequiredService<IRawFinalImportSignatureValidator>().Validate(new(
            plan,
            [new(javaScriptIdentity, [CliValueKind.I4], CliValueKind.I4)],
            [new(runtimeIdentity, [CliValueKind.ManagedAddress], CliValueKind.Void)],
            [
                new(runtimeIdentity, [address], []),
                new(identity, [address, address], [RawCoreValueType.I32]),
                new(javaScriptIdentity, [RawCoreValueType.I32], [RawCoreValueType.I32]),
            ]));

        Assert.Equal(new RawCanonicalImportIdentity(module, "accept"), identity);
        Assert.Same(function, declaration.Definition);
        Assert.Same(function, layout.Declaration);
        Assert.Equal(parameterSize, layout.Layout.ParameterMemory.Size);
        Assert.Collection(layout.Layout.Signature.Parameters,
            kind => Assert.Equal(CliValueKind.ManagedAddress, kind),
            kind => Assert.Equal(CliValueKind.ManagedAddress, kind));
        Assert.Equal(CliValueKind.I4, layout.Layout.Signature.Result);
        Assert.Equal(javaScriptIdentity, Assert.Single(selected.JavaScriptImports));
        Assert.Equal(runtimeIdentity, Assert.Single(selected.RuntimeImports));
        var planned = Assert.IsType<RawWitImportLayout.Callable>(Assert.Single(plan.Imports));
        Assert.Equal(identity, planned.Identity);
        Assert.Equal(target, planned.Target);
        Assert.Same(function, planned.Function.Declaration);
        Assert.Equal(parameterSize, planned.Function.Layout.ParameterMemory.Size);
        Assert.Same(plan, validated.Plan);
        Assert.Equal(3, validated.ExpectedImports.Length);
        Assert.Equal([runtimeIdentity, identity, javaScriptIdentity],
            validated.ObservedImports.Select(import => import.Identity));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DeploymentProjectionRetainsSelectedExactInterfaceIdentity(
        WasmTarget target)
    {
        var function = new WitFunction(
            "get-arguments",
            [],
            new WitTypeReference.Primitive("string"),
            new("freestanding"));
        var definition = new WitInterface(
            0,
            "environment",
            "wasi:cli@0.2.11",
            [],
            [function]);
        var world = new WitWorld(
            0,
            "command",
            "wasi:cli@0.2.11",
            [new("wasi:cli/environment@0.2.11", 0, null)],
            []);
        var document = new WitDocument([], [definition], [world], [], "{}");
        var services = new ServiceCollection().AddNetWasmCompiler();
        services.AddSingleton<IWitDocumentReader>(new FixedDocumentReader(document));
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var catalog = provider.GetRequiredService<IRawWitImportCatalogBuilder>()
            .Build(document, world, target);
        var identity = Assert.Single(catalog.Imports.Keys);
        var plan = provider.GetRequiredService<IRawWitBindingPlanBuilder>()
            .Build(new(catalog, [identity], [], []));
        var layout = Assert.Single(plan.Imports);
        var observed = provider.GetRequiredService<IRawCliCoreSignatureProjector>()
            .Project(new(identity, layout.Signature.Parameters, layout.Signature.Result), target);
        var validated = provider.GetRequiredService<IRawFinalImportSignatureValidator>()
            .Validate(new(plan, [], [], [observed]));

        var projection = provider
            .GetRequiredService<IRawDeploymentFunctionProjector>()
            .Project(validated, "runtime-wit", "command");
        var projected = Assert.Single(projection.RequiredImports);

        Assert.Equal("wasi:cli/environment@0.2.11",
            Assert.IsType<RawWitImportDeclaration.Callable>(catalog.Imports[identity])
                .InterfaceName);
        Assert.Equal("wasi:cli/environment@0.2.11", projected.Interface);
        Assert.Equal("get-arguments", projected.Name);
        Assert.Equal(2, projection.AdapterPlans.Length);
        Assert.Same(validated, projection.AdapterPlans[0]);
        Assert.Empty(projection.AdapterPlans[1].Plan.Imports);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void DeploymentProjectionSelectsAndValidatesRuntimeWitImports(
        WasmTarget target,
        bool signatureDrift)
    {
        var function = new WitFunction(
            "accept",
            [new("value", new WitTypeReference.Primitive("s32"))],
            new WitTypeReference.Primitive("s32"),
            new("freestanding"));
        var definition = new WitInterface(
            0,
            "api",
            "example:runtime@1.2.3",
            [],
            [function]);
        var runtimeWorld = new WitWorld(
            0,
            "runtime",
            "example:runtime@1.2.3",
            [new("example:runtime/api@1.2.3", 0, null)],
            []);
        var runtimeDocument = new WitDocument(
            [],
            [definition],
            [runtimeWorld],
            [],
            "{}");
        var services = new ServiceCollection().AddNetWasmCompiler();
        services.AddSingleton<IWitDocumentReader>(
            new FixedDocumentReader(runtimeDocument));
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var runtimeCatalog = provider.GetRequiredService<IRawWitImportCatalogBuilder>()
            .Build(runtimeDocument, runtimeWorld, target);
        var identity = Assert.Single(runtimeCatalog.Imports.Keys);
        var observed = new RawCoreFunctionImportSignature(
            identity,
            [signatureDrift ? RawCoreValueType.F64 : RawCoreValueType.I32],
            [RawCoreValueType.I32]);
        var applicationWorld = new WitWorld(
            0,
            "application",
            "example:application@1.0.0",
            [],
            []);
        var applicationCatalog = provider
            .GetRequiredService<IRawWitImportCatalogBuilder>()
            .Build(new([], [], [applicationWorld], [], "{}"), applicationWorld, target);
        var applicationPlan = provider.GetRequiredService<IRawWitBindingPlanBuilder>()
            .Build(new(applicationCatalog, [identity], [], [identity]));
        var validated = provider.GetRequiredService<IRawFinalImportSignatureValidator>()
            .Validate(new(applicationPlan, [], [], [observed])
            {
                RuntimeCoreImports = [observed],
            });

        var projector = provider.GetRequiredService<IRawDeploymentFunctionProjector>();
        if (signatureDrift)
        {
            Assert.Throws<CompilerException>(() =>
                projector.Project(validated, "runtime-wit", "runtime"));
            return;
        }

        var projection = projector.Project(validated, "runtime-wit", "runtime");
        var projected = Assert.Single(projection.RequiredImports);

        Assert.Equal("example:runtime/api@1.2.3", projected.Interface);
        Assert.Equal("accept", projected.Name);
        Assert.Equal(["s32"], projected.Parameters.ToArray());
        Assert.Equal(["s32"], projected.Results.ToArray());
        Assert.Empty(projection.AdapterPlans[0].Plan.Imports);
        Assert.Single(projection.AdapterPlans[1].Plan.Imports);
    }

    private sealed class FixedDocumentReader(WitDocument document) :
        IWitDocumentReader
    {
        public WitDocument Read(string witPath) => document;
    }

    [Fact]
    public void RejectsMissingCollection()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(() => services!.AddCompilerRawBindings());
    }

    [Fact]
    public void RegistersTheRawFunctionLayoutCapability()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddCompilerRawBindings());

        Assert.Equal(19, services.Count);
        var identity = services[0];
        Assert.Equal(typeof(IRawCanonicalImportIdentityFormatter), identity.ServiceType);
        Assert.Equal(typeof(RawCanonicalImportIdentityFormatter), identity.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, identity.Lifetime);
        var descriptor = services[1];
        Assert.Equal(typeof(IRawCanonicalFunctionLayoutPlanner), descriptor.ServiceType);
        Assert.Equal(typeof(RawCanonicalFunctionLayoutPlanner), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        var wit = services[2];
        Assert.Equal(typeof(IRawWitFunctionLayoutBuilder), wit.ServiceType);
        Assert.Equal(typeof(RawWitFunctionLayoutBuilder), wit.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, wit.Lifetime);
        var catalog = services[3];
        Assert.Equal(typeof(IRawWitImportCatalogBuilder), catalog.ServiceType);
        Assert.Equal(typeof(RawWitImportCatalogBuilder), catalog.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, catalog.Lifetime);
        var selector = services[4];
        Assert.Equal(typeof(IRawImportBindingSelector), selector.ServiceType);
        Assert.Equal(typeof(RawImportBindingSelector), selector.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, selector.Lifetime);
        var resource = services[5];
        Assert.Equal(typeof(IRawResourceIntrinsicLayoutPlanner), resource.ServiceType);
        Assert.Equal(typeof(RawResourceIntrinsicLayoutPlanner), resource.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, resource.Lifetime);
        var dispatcher = services[6];
        Assert.Equal(typeof(IRawWitImportLayoutBuilder), dispatcher.ServiceType);
        Assert.NotNull(dispatcher.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Singleton, dispatcher.Lifetime);
        var plan = services[7];
        Assert.Equal(typeof(IRawWitBindingPlanBuilder), plan.ServiceType);
        Assert.Equal(typeof(RawWitBindingPlanBuilder), plan.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, plan.Lifetime);
        var projector = services[8];
        Assert.Equal(typeof(IRawCliCoreSignatureProjector), projector.ServiceType);
        Assert.Equal(typeof(RawCliCoreSignatureProjector), projector.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, projector.Lifetime);
        var validator = services[9];
        Assert.Equal(typeof(IRawFinalImportSignatureValidator), validator.ServiceType);
        Assert.Equal(typeof(RawFinalImportSignatureValidator), validator.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, validator.Lifetime);
        var adapterWriter = services[10];
        Assert.Equal(typeof(IRawAdapterWriter), adapterWriter.ServiceType);
        Assert.Equal(typeof(RawAdapterWriter), adapterWriter.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, adapterWriter.Lifetime);
        var inspectionProcess = services[11];
        Assert.Equal(typeof(IRawModuleInspectionProcess), inspectionProcess.ServiceType);
        Assert.Equal(typeof(RawModuleInspectionProcess), inspectionProcess.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, inspectionProcess.Lifetime);
        var protocol = services[12];
        Assert.Equal(typeof(IRawModuleInspectionProtocolReader), protocol.ServiceType);
        Assert.Equal(typeof(RawModuleInspectionProtocolReader), protocol.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, protocol.Lifetime);
        var inspection = services[13];
        Assert.Equal(typeof(IRawModuleImportSignatureReader), inspection.ServiceType);
        Assert.Equal(typeof(RawModuleImportSignatureReader), inspection.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, inspection.Lifetime);
        var declarations = services[14];
        Assert.Equal(typeof(IRawCompilerImportDeclarationBuilder), declarations.ServiceType);
        Assert.Equal(typeof(RawCompilerImportDeclarationBuilder), declarations.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, declarations.Lifetime);
        var runtimeDeclarations = services[15];
        Assert.Equal(typeof(IRawCoreRuntimeImportDeclarationBuilder), runtimeDeclarations.ServiceType);
        Assert.Equal(typeof(RawCoreRuntimeImportDeclarationBuilder), runtimeDeclarations.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, runtimeDeclarations.Lifetime);
        var sourceValidator = services[16];
        Assert.Equal(typeof(IRawBuildImportSourceValidator), sourceValidator.ServiceType);
        Assert.Equal(typeof(RawBuildImportSourceValidator), sourceValidator.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, sourceValidator.Lifetime);
        var buildValidator = services[17];
        Assert.Equal(typeof(IRawBuildImportValidator), buildValidator.ServiceType);
        Assert.Equal(typeof(RawBuildImportValidator), buildValidator.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, buildValidator.Lifetime);
        var deploymentFunctions = services[18];
        Assert.Equal(typeof(IRawDeploymentFunctionProjector), deploymentFunctions.ServiceType);
        Assert.Equal(typeof(RawDeploymentFunctionProjector), deploymentFunctions.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, deploymentFunctions.Lifetime);
    }

    [Fact]
    public void PublicCompilerCompositionResolvesRawLayoutPlanner()
    {
        using var provider = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<RawCanonicalFunctionLayoutPlanner>(provider.GetRequiredService<IRawCanonicalFunctionLayoutPlanner>());
        Assert.IsType<RawCanonicalImportIdentityFormatter>(provider.GetRequiredService<IRawCanonicalImportIdentityFormatter>());
        Assert.IsType<RawWitFunctionLayoutBuilder>(provider.GetRequiredService<IRawWitFunctionLayoutBuilder>());
        Assert.IsType<RawWitImportCatalogBuilder>(provider.GetRequiredService<IRawWitImportCatalogBuilder>());
        Assert.IsType<RawImportBindingSelector>(provider.GetRequiredService<IRawImportBindingSelector>());
        Assert.IsType<RawResourceIntrinsicLayoutPlanner>(provider.GetRequiredService<IRawResourceIntrinsicLayoutPlanner>());
        Assert.IsType<RawWitImportLayoutBuilder>(provider.GetRequiredService<IRawWitImportLayoutBuilder>());
        Assert.IsType<RawWitBindingPlanBuilder>(provider.GetRequiredService<IRawWitBindingPlanBuilder>());
        Assert.IsType<RawCliCoreSignatureProjector>(provider.GetRequiredService<IRawCliCoreSignatureProjector>());
        Assert.IsType<RawFinalImportSignatureValidator>(provider.GetRequiredService<IRawFinalImportSignatureValidator>());
        Assert.IsType<RawAdapterWriter>(provider.GetRequiredService<IRawAdapterWriter>());
        Assert.IsType<RawModuleInspectionProcess>(provider.GetRequiredService<IRawModuleInspectionProcess>());
        Assert.IsType<RawModuleInspectionProtocolReader>(provider.GetRequiredService<IRawModuleInspectionProtocolReader>());
        Assert.IsType<RawModuleImportSignatureReader>(provider.GetRequiredService<IRawModuleImportSignatureReader>());
        Assert.IsType<RawCompilerImportDeclarationBuilder>(provider.GetRequiredService<IRawCompilerImportDeclarationBuilder>());
        Assert.IsType<RawCoreRuntimeImportDeclarationBuilder>(provider.GetRequiredService<IRawCoreRuntimeImportDeclarationBuilder>());
        Assert.IsType<RawBuildImportValidator>(provider.GetRequiredService<IRawBuildImportValidator>());
        Assert.IsType<RawDeploymentFunctionProjector>(provider.GetRequiredService<IRawDeploymentFunctionProjector>());
        Assert.IsType<WitCanonicalFunctionBuilder>(provider.GetRequiredService<IWitCanonicalFunctionBuilder>());
    }
}
