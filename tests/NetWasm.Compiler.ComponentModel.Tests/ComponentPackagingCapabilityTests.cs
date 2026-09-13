using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class ComponentPackagingCapabilityTests
{
    [Fact]
    public void AcceptsWasm32PreviewTwo()
    {
        new ComponentPackagingCapability().EnsureSupported(
            ComponentTarget.Wasm32Wasi02);
    }

    [Fact]
    public void ReportsExternalMemory64ComponentToolchainLimitation()
    {
        var exception = Assert.Throws<CompilerException>(() =>
            new ComponentPackagingCapability().EnsureSupported(
                ComponentTarget.Wasm64Wasi02));

        Assert.Equal(DiagnosticCode.ComponentToolchain, exception.Diagnostic.Code);
        Assert.Equal("NW1010", exception.Diagnostic.Id);
        Assert.Contains("cm32p2", exception.Diagnostic.Message);
        Assert.Contains("use wasm32 for component output",
            exception.Diagnostic.Message);
        Assert.Contains("emit a wasm64 core module",
            exception.Diagnostic.Message);
    }

    [Fact]
    public void RejectsUnknownTargetWidth()
    {
        var exception = Assert.Throws<CompilerException>(() =>
            new ComponentPackagingCapability().EnsureSupported(
                new ComponentTarget("wasm128", "0.2", "utf8")));

        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
        Assert.Contains("wasm128", exception.Diagnostic.Message);
    }

    [Fact]
    public void RejectsWasiVersionOtherThanPreviewTwo()
    {
        var exception = Assert.Throws<CompilerException>(() =>
            new ComponentPackagingCapability().EnsureSupported(
                new ComponentTarget("wasm32", "0.3", "utf8")));

        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
        Assert.Contains("only WASI 0.2", exception.Diagnostic.Message);
    }
}
