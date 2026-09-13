using System.Collections.Immutable;
using NetWasm.Hosting.Capabilities;
using NetWasm.Hosting.Deployment;
using NetWasm.Hosting.Execution;
using NetWasm.Hosting.Tests.Deployment;
using NetWasm.Hosting.Tests.Execution;

namespace NetWasm.Hosting.Tests.Capabilities;

public sealed class CapabilityBindingValidatorTests
{
    private readonly ICapabilityBindingValidator _subject = Assert.IsAssignableFrom<ICapabilityBindingValidator>(
        new CapabilityBindingValidator(new DeploymentManifestValidator(), new NetWasmExecutionRequestValidator()));

    [Theory]
    [InlineData(NetWasmPlatformCapability.Baseline)]
    [InlineData(NetWasmPlatformCapability.Environment)]
    [InlineData(NetWasmPlatformCapability.PreopenedDirectories)]
    [InlineData(NetWasmPlatformCapability.Network)]
    [InlineData(NetWasmPlatformCapability.WallClock)]
    [InlineData(NetWasmPlatformCapability.MonotonicClock)]
    [InlineData(NetWasmPlatformCapability.Randomness)]
    public void AcceptsEveryGrantedPlatformCapability(NetWasmPlatformCapability capability)
    {
        var data = CapabilityBindingFixture.Create();
        var grants = data.Request.Grants with { Network = NetWasmNetworkPolicy.AllowAll };
        data = CapabilityBindingFixture.WithPlatformCapability(capability, grants);
        _subject.Validate(data.Manifest, data.Request, data.PlatformProviders, data.ApplicationProviders);
    }

    [Fact]
    public void AcceptsEmptyGranularEnvironmentAndPreopenGrants()
    {
        var data = CapabilityBindingFixture.Create();
        var grants = data.Request.Grants with { Environment = [], Preopens = [] };
        foreach (var capability in new[] { NetWasmPlatformCapability.Environment, NetWasmPlatformCapability.PreopenedDirectories })
        {
            data = CapabilityBindingFixture.WithPlatformCapability(capability, grants);
            _subject.Validate(data.Manifest, data.Request with { Environment = [] }, data.PlatformProviders, data.ApplicationProviders);
        }
    }

    [Fact]
    public void AcceptsNetWasmReservedPlatformModuleAndExtraProviderMember()
    {
        var data = CapabilityBindingFixture.Create();
        var required = data.Manifest.RequiredImports[0] with { Interface = "netwasm:platform/process@1.0.0" };
        var extra = required with { Name = "status.2", Results = ["u8"] };
        data = data with
        {
            Manifest = data.Manifest with
            {
                RequiredImportModules = [required.Interface, data.Manifest.RequiredImportModules[1]],
                RequiredImports = [required, data.Manifest.RequiredImports[1]],
            },
            PlatformProviders = [new(required.Interface, NetWasmPlatformCapability.Baseline, [required, extra])],
        };
        _subject.Validate(data.Manifest, data.Request, data.PlatformProviders, data.ApplicationProviders);
    }

    [Fact]
    public void AcceptsCanonicalDigitHyphenAndDotProviderModuleSyntax()
    {
        var data = CapabilityBindingFixture.Create();
        var module = "1example:log-v2.provider/logger@1.0.0";
        var function = data.Manifest.RequiredImports[1] with { Interface = module };
        var binding = data.Request.ApplicationImports[0] with { Module = module };
        data = data with
        {
            Manifest = data.Manifest with
            {
                RequiredImportModules = [data.Manifest.RequiredImportModules[0], module],
                RequiredImports = [data.Manifest.RequiredImports[0], function],
            },
            Request = data.Request with { ApplicationImports = [binding] },
            ApplicationProviders = [new(module, [function])],
        };
        _subject.Validate(data.Manifest, data.Request, data.PlatformProviders, data.ApplicationProviders);
    }

    [Theory]
    [InlineData("[constructor]descriptor")]
    [InlineData("[method]descriptor.read-via-stream")]
    [InlineData("[static]descriptor.open-at")]
    [InlineData("[resource-drop]descriptor")]
    [InlineData("[resource-dtor]descriptor")]
    [InlineData("[export-resource-new]descriptor")]
    [InlineData("[export-resource-rep]descriptor")]
    [InlineData("[export-resource-drop]descriptor")]
    public void AcceptsCanonicalWitResourceProviderMembers(string name)
    {
        var data = CapabilityBindingFixture.Create();
        var provider = data.PlatformProviders[0];
        var function = provider.Functions[0] with { Name = name };
        data = data with
        {
            Manifest = data.Manifest with { RequiredImports = [function, data.Manifest.RequiredImports[1]] },
            PlatformProviders = [provider with { Functions = [function] }],
        };

        _subject.Validate(data.Manifest, data.Request, data.PlatformProviders, data.ApplicationProviders);
    }

    [Fact]
    public void AcceptsDeploymentWithoutImportsOrProviders()
    {
        var data = CapabilityBindingFixture.Create();
        _subject.Validate(
            data.Manifest with { RequiredImportModules = [], RequiredImports = [] },
            data.Request with { ApplicationImports = [] },
            [],
            []);
    }

    [Fact]
    public void AcceptsRuntimeOwnedReactorImportsWithoutAnExternalProvider()
    {
        const string module = "netwasm:runtime/reactor-host@1.0.0";
        var data = CapabilityBindingFixture.Create();
        var watch = new DeploymentFunction(module, "watch", ["u32", "s64"], []);
        var cancel = new DeploymentFunction(module, "cancel", ["u32"], []);
        data = data with
        {
            Manifest = data.Manifest with
            {
                RequiredImportModules = [.. data.Manifest.RequiredImportModules, module],
                RequiredImports = [.. data.Manifest.RequiredImports, watch, cancel],
            },
        };

        _subject.Validate(data.Manifest, data.Request, data.PlatformProviders, data.ApplicationProviders);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("wall-clock")]
    [InlineData("monotonic-clock")]
    [InlineData("randomness")]
    [InlineData("invalid")]
    public void RejectsDeniedOrInvalidPlatformCapability(string mutation)
    {
        var data = CapabilityBindingFixture.Create();
        var grants = data.Request.Grants;
        var capability = mutation switch
        {
            "network" => NetWasmPlatformCapability.Network,
            "wall-clock" => NetWasmPlatformCapability.WallClock,
            "monotonic-clock" => NetWasmPlatformCapability.MonotonicClock,
            "randomness" => NetWasmPlatformCapability.Randomness,
            _ => (NetWasmPlatformCapability)99,
        };
        grants = mutation switch
        {
            "wall-clock" => grants with { Clocks = [NetWasmClock.Monotonic] },
            "monotonic-clock" => grants with { Clocks = [NetWasmClock.Wall] },
            "randomness" => grants with { Randomness = false },
            _ => grants with { Network = NetWasmNetworkPolicy.DenyAll },
        };
        data = CapabilityBindingFixture.WithPlatformCapability(capability, grants);
        Assert.ThrowsAny<ArgumentException>(() =>
            _subject.Validate(data.Manifest, data.Request, data.PlatformProviders, data.ApplicationProviders));
    }

    [Theory]
    [InlineData("build-identity")]
    [InlineData("default-platforms")]
    [InlineData("default-applications")]
    [InlineData("null-platform")]
    [InlineData("null-application")]
    [InlineData("nonreserved-platform")]
    [InlineData("reserved-application-wasi")]
    [InlineData("reserved-application-netwasm")]
    [InlineData("unused-platform")]
    [InlineData("unused-application")]
    [InlineData("duplicate-platform")]
    [InlineData("duplicate-application")]
    [InlineData("unrequired-binding")]
    [InlineData("binding-without-provider")]
    [InlineData("missing-artifact")]
    [InlineData("wrong-artifact-role")]
    [InlineData("wrong-artifact-media")]
    [InlineData("wrong-artifact-hash")]
    [InlineData("provider-without-binding")]
    [InlineData("missing-platform-provider")]
    [InlineData("missing-application-provider")]
    [InlineData("missing-platform-member")]
    [InlineData("missing-application-member")]
    [InlineData("parameter-drift")]
    [InlineData("result-drift")]
    public void RejectsIncompleteAmbiguousOrUnauthorizedBinding(string mutation)
    {
        var data = Mutate(CapabilityBindingFixture.Create(), mutation);
        Assert.ThrowsAny<ArgumentException>(() =>
            _subject.Validate(data.Manifest, data.Request, data.PlatformProviders, data.ApplicationProviders));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("module")]
    [InlineData("@1.0.0")]
    [InlineData("module@")]
    [InlineData("a@b@1.0.0")]
    [InlineData(":module@1.0.0")]
    [InlineData("bad/Module@1.0.0")]
    [InlineData("bad_module@1.0.0")]
    [InlineData("module@1.0")]
    [InlineData("module@1..0")]
    [InlineData("module@1.x.0")]
    [InlineData(" module@1.0.0")]
    [InlineData("module@1.0.0\n")]
    public void RejectsMalformedProviderModule(string? module)
    {
        var data = CapabilityBindingFixture.Create();
        data = data with { ApplicationProviders = [data.ApplicationProviders[0] with { Module = module! }] };
        Assert.ThrowsAny<ArgumentException>(() =>
            _subject.Validate(data.Manifest, data.Request, data.PlatformProviders, data.ApplicationProviders));
    }

    [Theory]
    [InlineData("default-functions")]
    [InlineData("empty-functions")]
    [InlineData("null-function")]
    [InlineData("wrong-interface")]
    [InlineData("duplicate-function")]
    [InlineData("default-parameters")]
    [InlineData("default-results")]
    [InlineData("null-parameter")]
    [InlineData("empty-result")]
    [InlineData("padded-parameter")]
    [InlineData("control-result")]
    public void RejectsMalformedProviderFunctionInventory(string mutation)
    {
        var data = CapabilityBindingFixture.Create();
        var provider = data.PlatformProviders[0];
        var function = provider.Functions[0];
        var functions = mutation switch
        {
            "default-functions" => default,
            "empty-functions" => ImmutableArray<DeploymentFunction>.Empty,
            "null-function" => [null!],
            "wrong-interface" => [function with { Interface = "wasi:cli/exit@0.2.11" }],
            "duplicate-function" => [function, function],
            "default-parameters" => [function with { Parameters = default }],
            "default-results" => [function with { Results = default }],
            "null-parameter" => [function with { Parameters = [null!] }],
            "empty-result" => [function with { Results = [""] }],
            "padded-parameter" => [function with { Parameters = [" padded"] }],
            _ => [function with { Results = ["bad\0result"] }],
        };
        data = data with { PlatformProviders = [provider with { Functions = functions }] };
        Assert.ThrowsAny<ArgumentException>(() =>
            _subject.Validate(data.Manifest, data.Request, data.PlatformProviders, data.ApplicationProviders));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Get")]
    [InlineData("bad_name")]
    [InlineData("bad\0name")]
    [InlineData("[method]descriptor")]
    [InlineData("[method].read")]
    [InlineData("[method]descriptor.read.more")]
    [InlineData("[constructor]descriptor.open")]
    [InlineData("[resource-drop]")]
    [InlineData("[resource-drop]Descriptor")]
    [InlineData("[export-resource-new]descriptor.more")]
    public void RejectsMalformedProviderFunctionName(string? name)
    {
        var data = CapabilityBindingFixture.Create();
        var provider = data.PlatformProviders[0];
        data = data with
        {
            PlatformProviders = [provider with { Functions = [provider.Functions[0] with { Name = name! }] }],
        };
        Assert.ThrowsAny<ArgumentException>(() =>
            _subject.Validate(data.Manifest, data.Request, data.PlatformProviders, data.ApplicationProviders));
    }

    [Fact]
    public void RejectsNullInputsBeforeDelegation()
    {
        var data = CapabilityBindingFixture.Create();
        var subject = new CapabilityBindingValidator(
            new ManifestValidationStub(_ => Assert.Fail("Must not validate.")),
            new ExecutionRequestValidationStub(_ => Assert.Fail("Must not validate.")));
        Assert.Throws<ArgumentNullException>(() =>
            subject.Validate(null!, data.Request, data.PlatformProviders, data.ApplicationProviders));
        Assert.Throws<ArgumentNullException>(() =>
            subject.Validate(data.Manifest, null!, data.PlatformProviders, data.ApplicationProviders));
    }

    [Fact]
    public void DelegatesStructuralValidationInOrderAndPropagatesFailure()
    {
        var data = CapabilityBindingFixture.Create();
        var calls = new List<string>();
        var manifestFailure = new ArgumentException("Rejected manifest.");
        var subject = new CapabilityBindingValidator(
            new ManifestValidationStub(_ =>
            {
                calls.Add("manifest");
                throw manifestFailure;
            }),
            new ExecutionRequestValidationStub(_ => calls.Add("request")));
        Assert.Same(manifestFailure, Assert.Throws<ArgumentException>(() =>
            subject.Validate(data.Manifest, data.Request, data.PlatformProviders, data.ApplicationProviders)));
        Assert.Equal(["manifest"], calls);

        calls.Clear();
        var requestFailure = new ArgumentException("Rejected request.");
        subject = new CapabilityBindingValidator(
            new ManifestValidationStub(_ => calls.Add("manifest")),
            new ExecutionRequestValidationStub(_ =>
            {
                calls.Add("request");
                throw requestFailure;
            }));
        Assert.Same(requestFailure, Assert.Throws<ArgumentException>(() =>
            subject.Validate(data.Manifest, data.Request, data.PlatformProviders, data.ApplicationProviders)));
        Assert.Equal(["manifest", "request"], calls);
    }

    [Fact]
    public void RejectsMissingValidators()
    {
        Assert.Throws<ArgumentNullException>(() => new CapabilityBindingValidator(null!, new NetWasmExecutionRequestValidator()));
        Assert.Throws<ArgumentNullException>(() => new CapabilityBindingValidator(new DeploymentManifestValidator(), null!));
        Assert.Throws<ArgumentNullException>(() => new CapabilityBindingValidator(
            new DeploymentManifestValidator(),
            new NetWasmExecutionRequestValidator(),
            null!));
    }

    private static CapabilityBindingFixtureData Mutate(CapabilityBindingFixtureData data, string mutation)
    {
        var platform = data.PlatformProviders[0];
        var application = data.ApplicationProviders[0];
        var platformFunction = platform.Functions[0];
        var applicationFunction = application.Functions[0];
        var binding = data.Request.ApplicationImports[0];
        var artifact = data.Manifest.Artifacts[^1];
        return mutation switch
        {
            "build-identity" => data with { Request = data.Request with { BuildFingerprint = new string('f', 64) } },
            "default-platforms" => data with { PlatformProviders = default },
            "default-applications" => data with { ApplicationProviders = default },
            "null-platform" => data with { PlatformProviders = [null!] },
            "null-application" => data with { ApplicationProviders = [null!] },
            "nonreserved-platform" => data with
            {
                PlatformProviders = [platform with { Module = application.Module, Functions = application.Functions }],
            },
            "reserved-application-wasi" => data with
            {
                ApplicationProviders = [application with { Module = platform.Module, Functions = platform.Functions }],
            },
            "reserved-application-netwasm" => data with
            {
                ApplicationProviders = [application with
                {
                    Module = "netwasm:platform/process@1.0.0",
                    Functions = [applicationFunction with { Interface = "netwasm:platform/process@1.0.0" }],
                }],
            },
            "unused-platform" => data with
            {
                PlatformProviders = [platform with
                {
                    Module = "wasi:cli/exit@0.2.11",
                    Functions = [platformFunction with { Interface = "wasi:cli/exit@0.2.11", Name = "exit" }],
                }],
            },
            "unused-application" => data with
            {
                ApplicationProviders = [application with
                {
                    Module = "example:unused/provider@1.0.0",
                    Functions = [applicationFunction with { Interface = "example:unused/provider@1.0.0" }],
                }],
            },
            "duplicate-platform" => data with { PlatformProviders = [platform, platform] },
            "duplicate-application" => data with { ApplicationProviders = [application, application] },
            "unrequired-binding" => data with
            {
                Request = data.Request with
                {
                    ApplicationImports = [binding with { Module = "example:unused/provider@1.0.0" }],
                },
            },
            "binding-without-provider" => data with { ApplicationProviders = [] },
            "missing-artifact" => data with
            {
                Request = data.Request with { ApplicationImports = [binding with { ArtifactPath = "imports/missing.mjs" }] },
            },
            "wrong-artifact-role" => data with
            {
                Manifest = data.Manifest with { Artifacts = [.. data.Manifest.Artifacts[..^1], artifact with { Role = "asset" }] },
            },
            "wrong-artifact-media" => data with
            {
                Manifest = data.Manifest with
                {
                    Artifacts = [.. data.Manifest.Artifacts[..^1], artifact with { MediaType = "application/javascript" }],
                },
            },
            "wrong-artifact-hash" => data with
            {
                Manifest = data.Manifest with
                {
                    Artifacts = [.. data.Manifest.Artifacts[..^1], artifact with { Sha256 = new string('f', 64) }],
                },
            },
            "provider-without-binding" => data with { Request = data.Request with { ApplicationImports = [] } },
            "missing-platform-provider" => data with { PlatformProviders = [] },
            "missing-application-provider" => data with
            {
                ApplicationProviders = [],
                Request = data.Request with { ApplicationImports = [] },
            },
            "missing-platform-member" => data with
            {
                PlatformProviders = [platform with { Functions = [platformFunction with { Name = "other" }] }],
            },
            "missing-application-member" => data with
            {
                ApplicationProviders = [application with { Functions = [applicationFunction with { Name = "other" }] }],
            },
            "parameter-drift" => data with
            {
                ApplicationProviders = [application with
                {
                    Functions = [applicationFunction with { Parameters = ["list<string>"] }],
                }],
            },
            _ => data with
            {
                ApplicationProviders = [application with { Functions = [applicationFunction with { Results = ["u32"] }] }],
            },
        };
    }
}
