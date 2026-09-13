using NetWasm.Compiler.Cli;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class ComponentizeCliOptionsTests
{
    [Fact]
    public void DefaultsToWasm32AndRetainsAdapterProvenance()
    {
        var options = ComponentizeCliOptions.Parse([
            "--core-module", "application.wasm",
            "--runtime-module", "runtime.wasm",
            "--wit", "contract.wit",
            "--world", "example:test/main",
            "--output", "component.wasm",
            "--manifest", "component.json",
            "--target", "wasm64",
            "--interop-manifest", "interop.json",
            "--jco-version", "1.2.3",
            "--preview2-shim-version", "4.5.6",
        ]);

        Assert.Equal(WasmTarget.Wasm64, options.Target);
        Assert.Equal("example:test/main", options.World);
        Assert.Equal("interop.json", options.InteropManifest);
        Assert.Equal("1.2.3", options.JcoVersion);
        Assert.Equal("4.5.6", options.Preview2ShimVersion);
    }

    [Fact]
    public void RejectsUnsupportedTargetWidth()
    {
        var exception = Assert.Throws<CompilerException>(() =>
            ComponentizeCliOptions.Parse([
                "--core-module", "application.wasm",
                "--wit", "contract.wit",
                "--output", "component.wasm",
                "--manifest", "component.json",
                "--target", "wasm128",
            ]));

        Assert.Contains("wasm32", exception.Diagnostic.Message);
    }

    [Fact]
    public void RejectsUnknownOptionAndMissingValues()
    {
        Assert.Throws<CompilerException>(() => ComponentizeCliOptions.Parse([
            "--unknown", "value"]));
        Assert.Throws<CompilerException>(() => ComponentizeCliOptions.Parse([
            "--core-module"]));
        Assert.Throws<CompilerException>(() => ComponentizeCliOptions.Parse([
            "--core-module", "application.wasm", "--wit", "contract.wit",
            "--output", "component.wasm", "--manifest", "component.json",
            "--core-module", "duplicate.wasm"]));
    }

    [Fact]
    public void RejectsMissingRequiredFields()
    {
        Assert.Throws<CompilerException>(() => ComponentizeCliOptions.Parse([]));
        Assert.Throws<CompilerException>(() => ComponentizeCliOptions.Parse([
            "--core-module", "application.wasm"]));
        Assert.Throws<CompilerException>(() => ComponentizeCliOptions.Parse([
            "--core-module", "application.wasm", "--wit", "contract.wit"]));
        Assert.Throws<CompilerException>(() => ComponentizeCliOptions.Parse([
            "--core-module", "application.wasm", "--wit", "contract.wit",
            "--output", "component.wasm"]));
    }
}
