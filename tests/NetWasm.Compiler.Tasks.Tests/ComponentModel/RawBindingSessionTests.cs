using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.ComponentModel;
using NetWasm.Compiler.Tasks.Composition;

namespace NetWasm.Compiler.Tasks.Tests.ComponentModel;

public sealed class RawBindingSessionTests
{
    [Fact]
    public void SessionDelegatesValidationAdapterAndFunctionProjection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DisposableProbe>();
        var provider = services.BuildServiceProvider();
        var probe = provider.GetRequiredService<DisposableProbe>();
        var validator = new RecordingValidator();
        var adapters = new RecordingAdapterWriter();
        var functions = new RecordingFunctionProjector();
        var session = AsSession(new RawBindingSession(
            provider,
            validator,
            adapters,
            functions));
        var manifest = new HostInteropManifest(2, "wasm32", null!, null!, [], [])
        {
            ImportModules = ["app.optional", "app.native"],
        };
        var request = new RawBuildImportSourceValidationRequest(
            new([], manifest),
            "wit",
            null,
            WasmTarget.Wasm32,
            new("node", "inspect", "/runtime", "/binaryen"),
            new("node", "inspect", "/final", "/binaryen"))
        {
            RuntimeWitPath = "runtime-wit",
            RuntimeWorld = "runtime-world",
        };

        var result = session.Build(request);
        session.Dispose();

        Assert.Same(request, validator.Request);
        Assert.True(adapters.Called);
        Assert.True(functions.Called);
        Assert.Equal(("runtime-wit", "runtime-world"), functions.RuntimeWit);
        Assert.Equal([1, 2, 3], result.Adapter);
        Assert.Equal("wasi:cli/environment@0.2.11",
            Assert.Single(result.RequiredImports).Interface);
        // Issue #128: only a declared module the final module imports is supplied.
        Assert.Equal(["app.native"], result.ImportModules.ToArray());
        Assert.True(probe.IsDisposed);
    }

    [Fact]
    public void ConstructorAndFactoryRejectMissingCapabilities()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var validator = new RecordingValidator();
        var adapters = new RecordingAdapterWriter();
        var functions = new RecordingFunctionProjector();

        Assert.Throws<ArgumentNullException>(() =>
            new RawBindingSession(provider, null!, adapters, functions));
        Assert.Throws<ArgumentNullException>(() =>
            new RawBindingSession(provider, validator, null!, functions));
        Assert.Throws<ArgumentNullException>(() =>
            new RawBindingSession(provider, validator, adapters, null!));
        Assert.Throws<ArgumentNullException>(() => new RawBindingSessionFactory(null!));
        using var session = CompilerTaskComposition.CreateRawBindingSessionFactory().Create(
            CompilerTaskComposition.CreateWasmToolsCommand("node", "command", "module"));
        Assert.IsType<RawBindingSession>(session);
    }

    private static IRawBindingSession AsSession(object session) =>
        (IRawBindingSession)session;

    private sealed class RecordingValidator : IRawBuildImportSourceValidator
    {
        public RawBuildImportSourceValidationRequest? Request { get; private set; }

        public RawValidatedBindingPlan Validate(
            RawBuildImportSourceValidationRequest request)
        {
            Request = request;
            return NativeImportPlan();
        }
    }

    // A final module that imports one function from a declared module, as a
    // linked native archive does.
    private static RawValidatedBindingPlan NativeImportPlan()
    {
        var document = new WitDocument([], [], [new(0, "empty", "example:test@1", [], [])], [], "{}");
        var catalog = new RawWitImportCatalog(document, document.Worlds[0], WasmTarget.Wasm32,
            ImmutableDictionary<RawCanonicalImportIdentity, RawWitImportDeclaration>.Empty);
        var identity = new RawCanonicalImportIdentity("app.native", "create_node");
        var signature = new RawCoreFunctionImportSignature(identity, [RawCoreValueType.I32], [RawCoreValueType.I64]);
        return new RawFinalImportSignatureValidator(new UnusedProjector()).Validate(new(
            new(new(catalog, [], [], [identity]), []), [], [], [signature])
        {
            RuntimeCoreImports = [signature],
        });
    }

    private sealed class UnusedProjector : IRawCliCoreSignatureProjector
    {
        public RawCoreFunctionImportSignature Project(RawCliFunctionImportSignature signature, WasmTarget target) =>
            throw new InvalidOperationException("the plan has no CLI signatures");
    }

    private sealed class RecordingAdapterWriter : IRawAdapterWriter
    {
        public bool Called { get; private set; }

        public byte[] Write(RawValidatedBindingPlan plan)
        {
            Called = true;
            return [1, 2, 3];
        }

        public byte[] WriteDeployment(RawAdapterWriteRequest request)
        {
            Called = true;
            Assert.Equal(2, request.Plans.Length);
            return [1, 2, 3];
        }
    }

    private sealed class RecordingFunctionProjector : IRawDeploymentFunctionProjector
    {
        public bool Called { get; private set; }
        public (string Path, string? World)? RuntimeWit { get; private set; }

        public RawDeploymentBindingProjection Project(
            RawValidatedBindingPlan plan,
            string runtimeWitPath,
            string? runtimeWorld)
        {
            Called = true;
            RuntimeWit = (runtimeWitPath, runtimeWorld);
            return new(
                [plan, plan],
                [new("wasi:cli/environment@0.2.11", "get-arguments", [], ["list<string>"])]);
        }
    }

    private sealed class DisposableProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }
}
