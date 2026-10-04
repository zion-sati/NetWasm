using NetWasm.Compiler.ComponentModel.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Tests.ManagedExecutables;

public sealed class HostedNetWasmHostComponentShimWriterTests
{
    [Theory]
    [InlineData("wasm32", "i32")]
    [InlineData("wasm64", "i64")]
    public void WritesTargetAwareHostImportShim(string width, string addressType)
    {
        var modules = new RecordingWasmTextModuleWriter();
        var writer = Assert.IsAssignableFrom<INetWasmHostComponentShimWriter>(
            new HostedNetWasmHostComponentShimWriter(modules));

        writer.Write(new(
            "netwasm-host.wasm",
            new ComponentTarget(width, "0.2", "utf8")));

        Assert.Equal("netwasm-host.wasm", modules.OutputPath);
        Assert.Contains("(func (export \"write_i32\") (param i32))", modules.Source);
        var prefix = width == "wasm64" ? "cm64p2" : "cm32p2";
        Assert.Contains(
            $"(import \"{prefix}|netwasm:diagnostics/terminal@1\" \"report\" " +
            $"(func $report (param i32 i32 {addressType} {addressType} i32 {addressType} {addressType})))",
            modules.Source);
        Assert.Contains(
            $"(param $type i32) (param $message {addressType}) (param $message_length i32) " +
            $"(param $stack_trace {addressType}) (param $stack_trace_length i32)",
            modules.Source);
        Assert.Contains("local.get $message " + addressType +
            ".eqz if (result i32) i32.const 0 else i32.const 1 end", modules.Source);
        Assert.Contains(
            $"{addressType}.eqz if (result {addressType}) {addressType}.const 0 " +
            $"else local.get $message {addressType}.const {(width == "wasm64" ? 12 : 8)} " +
            $"{addressType}.add end",
            modules.Source);
        Assert.Contains("(func (export \"raise_terminal_exception\") unreachable)", modules.Source);
        Assert.Equal(width == "wasm64", modules.Source.Contains(
            "local.get $stack_trace_length i64.extend_i32_u call $report",
            StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsMissingInput()
    {
        var modules = new RecordingWasmTextModuleWriter();
        var writer = Assert.IsAssignableFrom<INetWasmHostComponentShimWriter>(
            new HostedNetWasmHostComponentShimWriter(modules));

        Assert.Throws<ArgumentNullException>(() => new HostedNetWasmHostComponentShimWriter(null!));
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
        Assert.Throws<ArgumentException>(() => writer.Write(new(
            " ",
            ComponentTarget.Wasm32Wasi02)));
        Assert.Throws<ArgumentNullException>(() => writer.Write(new(
            "netwasm-host.wasm",
            null!)));
    }
}
