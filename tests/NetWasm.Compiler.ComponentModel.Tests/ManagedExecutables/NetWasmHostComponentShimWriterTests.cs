using NetWasm.Compiler.ComponentModel.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Tests.ManagedExecutables;

public sealed class NetWasmHostComponentShimWriterTests
{
    [Theory]
    [InlineData("wasm32", "i32")]
    [InlineData("wasm64", "i64")]
    public void WritesTargetAwareHostImportShim(string width, string addressType)
    {
        var modules = new RecordingWasmTextModuleWriter();
        var writer = Assert.IsAssignableFrom<INetWasmHostComponentShimWriter>(
            new NetWasmHostComponentShimWriter(modules));

        writer.Write(new(
            "netwasm-host.wasm",
            new ComponentTarget(width, "0.2", "utf8")));

        Assert.Equal("netwasm-host.wasm", modules.OutputPath);
        Assert.Contains("(func (export \"write_i32\") (param i32))", modules.Source);
        Assert.Contains(
            $"(func (export \"report_terminal_exception_v1\") (param i32 {addressType} i32) unreachable)",
            modules.Source);
    }

    [Fact]
    public void RejectsMissingInput()
    {
        var modules = new RecordingWasmTextModuleWriter();
        var writer = Assert.IsAssignableFrom<INetWasmHostComponentShimWriter>(
            new NetWasmHostComponentShimWriter(modules));

        Assert.Throws<ArgumentNullException>(() => new NetWasmHostComponentShimWriter(null!));
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
        Assert.Throws<ArgumentException>(() => writer.Write(new(
            " ",
            ComponentTarget.Wasm32Wasi02)));
        Assert.Throws<ArgumentNullException>(() => writer.Write(new(
            "netwasm-host.wasm",
            null!)));
    }
}
