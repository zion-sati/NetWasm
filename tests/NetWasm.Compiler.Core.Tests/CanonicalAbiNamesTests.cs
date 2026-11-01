using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.Tests;

public sealed class CanonicalAbiNamesTests
{
    private static readonly EntityKey Method = new(
        new AssemblyIdentity("Fixture"), 0x06000001);

    [Theory]
    [InlineData("example:service/api@1.2.3", "example:service/api@1")]
    [InlineData("example:service/api@2.0.0-preview.1", "example:service/api@2")]
    [InlineData("wasi:clocks/monotonic-clock@0.2.11",
        "wasi:clocks/monotonic-clock@0.2")]
    [InlineData("example:service/api@0.3.0+build", "example:service/api@0.3")]
    public void UsesTheWitSemanticCompatibilityTrack(
        string interfaceName,
        string expected)
    {
        var actual = CanonicalAbiNames.CoreInterfaceName(interfaceName);

        Assert.Equal(expected, actual);
    }

    // An interface of an unversioned package keeps its world key as it is, and
    // so does one the world declares inline, whose key is the world's name for it.
    [Theory]
    [InlineData("example:service/api", "example:service/api")]
    [InlineData("api", "api")]
    [InlineData("%type", "%type")]
    [InlineData("TLS-v1", "TLS-v1")]
    public void NamesUnversionedAndInlineInterfacesByTheirWorldKey(string interfaceName, string expected)
    {
        Assert.Equal(expected, CanonicalAbiNames.CoreInterfaceName(interfaceName));
        Assert.Equal($"cm32p2|{expected}|run", CanonicalAbiNames.Export(interfaceName, "run", WasmTarget.Wasm32));
    }

    // NetWasm's earlier package-first form, which bindings made by an older
    // generator carry, is rejected with a pointer to regenerating them.
    [Fact]
    public void RejectsThePackageFirstFormWithARegenerationHint()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CanonicalAbiNames.CoreInterfaceName("example:service@1.2.3/api"));

        Assert.Contains("example:service@1.2.3/api", exception.Message, StringComparison.Ordinal);
        Assert.Contains("regenerate bindings", exception.Message, StringComparison.Ordinal);
    }

    // A WIT name has letters, digits and single hyphens between words.
    [Theory]
    [InlineData("api_name")]
    [InlineData("example:service/@1.0.0")]
    public void RejectsAnInterfaceThatIsNoWitName(string interfaceName)
    {
        Assert.Throws<ArgumentException>(() => CanonicalAbiNames.CoreInterfaceName(interfaceName));
    }

    [Theory]
    [InlineData("example:service/api@invalid")]
    [InlineData("example:service/api@0")]
    public void RejectsInvalidCompatibilityVersions(string interfaceName)
    {
        Assert.Throws<ArgumentException>(() =>
            CanonicalAbiNames.CoreInterfaceName(interfaceName));
    }

    [Fact]
    public void NamesEveryCanonicalAbiBoundaryKindAndTarget()
    {
        const string interfaceName = "example:service/api@1.2.3";
        Assert.Equal("cm32p2", CanonicalAbiNames.ModulePrefix(WasmTarget.Wasm32));
        Assert.Equal("cm64p2", CanonicalAbiNames.ModulePrefix(WasmTarget.Wasm64));
        Assert.Equal("cm32p2_memory", CanonicalAbiNames.Memory(WasmTarget.Wasm32));
        Assert.Equal("cm64p2_realloc", CanonicalAbiNames.Reallocate(WasmTarget.Wasm64));
        Assert.Equal("cm32p2_initialize", CanonicalAbiNames.Initialize(WasmTarget.Wasm32));
        Assert.Equal("cm32p2||run", CanonicalAbiNames.Export("", "run", WasmTarget.Wasm32));
        Assert.Equal(
            "cm32p2|example:service/api@1|run",
            CanonicalAbiNames.Export(interfaceName, "run", WasmTarget.Wasm32));
        Assert.Equal(
            "cm32p2|example:service/api@1|thing_dtor",
            CanonicalAbiNames.Export(
                interfaceName, "[resource-dtor]thing", WasmTarget.Wasm32));
        Assert.Equal(
            "cm32p2|example:service/api@1|run_post",
            CanonicalAbiNames.PostReturn(interfaceName, "run", WasmTarget.Wasm32));

        var expectedNames = new Dictionary<CanonicalAbiFunctionKind, string>
        {
            [CanonicalAbiFunctionKind.Function] = "run",
            [CanonicalAbiFunctionKind.ImportedResourceDrop] = "thing_drop",
            [CanonicalAbiFunctionKind.ExportedResourceNew] = "thing_new",
            [CanonicalAbiFunctionKind.ExportedResourceRep] = "thing_rep",
            [CanonicalAbiFunctionKind.ExportedResourceDrop] = "thing_drop",
            [CanonicalAbiFunctionKind.ExportedResourceDestructor] = "run",
        };
        foreach (var (kind, expectedName) in expectedNames)
        {
            var function = Function(interfaceName, kind);
            Assert.Equal(expectedName, CanonicalAbiNames.ImportName(function));
            var expectedModule = kind is CanonicalAbiFunctionKind.ExportedResourceNew or
                CanonicalAbiFunctionKind.ExportedResourceRep or
                CanonicalAbiFunctionKind.ExportedResourceDrop
                    ? "cm64p2|_ex_example:service/api@1"
                    : "cm64p2|example:service/api@1";
            Assert.Equal(expectedModule,
                CanonicalAbiNames.ImportModule(function, WasmTarget.Wasm64));
        }

        Assert.Equal(
            "cm32p2|example:service/api@1|thing_dtor",
            CanonicalAbiNames.Export(
                Function(interfaceName, CanonicalAbiFunctionKind.ExportedResourceDestructor),
                WasmTarget.Wasm32));
        Assert.Equal(
            "cm32p2|example:service/api@1|run",
            CanonicalAbiNames.Export(Function(interfaceName), WasmTarget.Wasm32));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("/api")]
    [InlineData("example:service/")]
    [InlineData("example:service/api@")]
    [InlineData("service/api@1.0.0")]
    [InlineData("example:service/api name")]
    [InlineData("api name")]
    [InlineData("example:service/api@0.x")]
    public void RejectsEveryMalformedInterfaceBoundary(string interfaceName)
    {
        Assert.Throws<ArgumentException>(() =>
            CanonicalAbiNames.CoreInterfaceName(interfaceName));
    }

    [Fact]
    public void RejectsUnknownTargets()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CanonicalAbiNames.ModulePrefix((WasmTarget)int.MaxValue));
    }

    private static CanonicalAbiFunction Function(
        string interfaceName,
        CanonicalAbiFunctionKind kind = CanonicalAbiFunctionKind.Function) => new(
            interfaceName,
            "run",
            Method,
            [],
            null)
        {
            Kind = kind,
            ResourceName = "thing",
        };
}
