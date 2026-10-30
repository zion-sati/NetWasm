using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawCanonicalImportIdentityFormatterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, "sample:raw/io@1.2.3", "cm32p2|sample:raw/io@1")]
    [InlineData(WasmTarget.Wasm64, "sample:raw/io@1.2.3", "cm64p2|sample:raw/io@1")]
    [InlineData(WasmTarget.Wasm32, "wasi:filesystem/types@0.2.11", "cm32p2|wasi:filesystem/types@0.2")]
    [InlineData(WasmTarget.Wasm64, "wasi:filesystem/types@0.2.11", "cm64p2|wasi:filesystem/types@0.2")]
    [InlineData(WasmTarget.Wasm32, "", "cm32p2")]
    [InlineData(WasmTarget.Wasm64, "", "cm64p2")]
    public void UsesCompilerIdentityConventionsWithoutChangingTheMember(
        WasmTarget target, string interfaceName, string module)
    {
        var function = new CanonicalAbiFunction(interfaceName, "[method]descriptor.read", default, [], null);

        var identity = CreateFormatter().Format(function, target);

        Assert.Equal(module, identity.Module);
        Assert.Equal("[method]descriptor.read", identity.Name);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, "cm32p2|wasi:filesystem/types@0.2")]
    [InlineData(WasmTarget.Wasm64, "cm64p2|wasi:filesystem/types@0.2")]
    public void ResourceIntrinsicNamesRemainCompilerOwned(WasmTarget target, string module)
    {
        var function = new CanonicalAbiFunction("wasi:filesystem/types@0.2.11", "[resource-drop]descriptor", default, [], null)
        {
            Kind = CanonicalAbiFunctionKind.ImportedResourceDrop,
            ResourceName = "descriptor",
        };

        var identity = CreateFormatter().Format(function, target);

        Assert.Equal(module, identity.Module);
        Assert.Equal("descriptor_drop", identity.Name);
    }

    [Fact]
    public void InvalidNamingInputsRetainTheExistingFailureContract()
    {
        var formatter = CreateFormatter();
        var function = new CanonicalAbiFunction("sample:raw/io@1.2.3", "read", default, [], null);

        Assert.Throws<ArgumentNullException>(() => formatter.Format(null!, WasmTarget.Wasm32));
        Assert.Throws<ArgumentNullException>(() => formatter.Format(function with { InterfaceName = null! }, WasmTarget.Wasm32));
        Assert.Throws<ArgumentException>(() => formatter.Format(function with { FunctionName = " " }, WasmTarget.Wasm32));
        Assert.Throws<ArgumentException>(() => formatter.Format(function with { InterfaceName = "sample:raw@1.2.3/io" }, WasmTarget.Wasm32));
        Assert.Throws<ArgumentOutOfRangeException>(() => formatter.Format(function, (WasmTarget)99));
    }

    private static IRawCanonicalImportIdentityFormatter CreateFormatter() =>
        Assert.IsAssignableFrom<IRawCanonicalImportIdentityFormatter>(new RawCanonicalImportIdentityFormatter());
}
