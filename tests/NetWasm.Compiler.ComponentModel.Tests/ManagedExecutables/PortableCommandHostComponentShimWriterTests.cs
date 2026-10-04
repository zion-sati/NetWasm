using NetWasm.Compiler.ComponentModel.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Tests.ManagedExecutables;

public sealed class PortableCommandHostComponentShimWriterTests
{
    [Theory]
    [InlineData("wasm32", "i32")]
    [InlineData("wasm64", "i64")]
    public void TrapsWithoutRetainingDiagnosticsByDefault(string width, string addressType)
    {
        var modules = new RecordingWasmTextModuleWriter();
        var writer = Assert.IsAssignableFrom<INetWasmHostComponentShimWriter>(
            new PortableCommandHostComponentShimWriter(modules));
        writer.Write(new("host.wasm", new ComponentTarget(width, "0.2", "utf8")));
        Assert.Equal("host.wasm", modules.OutputPath);
        Assert.Contains($"(param i32 {addressType} i32 {addressType} i32)", modules.Source);
        Assert.Contains("(func (export \"raise_terminal_exception\") unreachable)", modules.Source);
        Assert.DoesNotContain("command_exception_capture", modules.Source);
        Assert.DoesNotContain("command_exception_write", modules.Source);
        Assert.DoesNotContain("(tag $terminal", modules.Source);
        Assert.DoesNotContain("netwasm:diagnostics", modules.Source);
    }

    [Theory]
    [InlineData("wasm32", "i32")]
    [InlineData("wasm64", "i64")]
    public void StructuredModeCapturesBeforeRaisingThePrivateTag(
        string width,
        string addressType)
    {
        var modules = new RecordingWasmTextModuleWriter();
        var writer = new PortableCommandHostComponentShimWriter(modules);

        writer.Write(new(
            "host.wasm",
            new ComponentTarget(width, "0.2", "utf8"),
            StructuredDiagnostics: true));

        Assert.Contains("(import \"netwasm.runtime.v1\" \"command_exception_capture\"", modules.Source);
        Assert.Contains($"(func $capture (param i32 {addressType} i32 {addressType} i32))", modules.Source);
        Assert.Contains("(tag $terminal (export \"terminal_exception\"))", modules.Source);
        Assert.Contains("local.get 0 local.get 1 local.get 2 local.get 3 local.get 4 call $capture", modules.Source);
        Assert.Contains("(func (export \"raise_terminal_exception\") throw $terminal)", modules.Source);
    }

    [Fact]
    public void RejectsMissingOrUnsupportedInput()
    {
        var modules = new RecordingWasmTextModuleWriter();
        var writer = Assert.IsAssignableFrom<INetWasmHostComponentShimWriter>(
            new PortableCommandHostComponentShimWriter(modules));
        Assert.Throws<ArgumentNullException>(() => new PortableCommandHostComponentShimWriter(null!));
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
        Assert.Throws<ArgumentException>(() => writer.Write(new(" ", ComponentTarget.Wasm32Wasi02)));
        Assert.Throws<ArgumentNullException>(() => writer.Write(new("host.wasm", null!)));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Write(new("host.wasm", new("invalid", "0.2", "utf8"))));
        Assert.Empty(modules.Source);
    }
}
