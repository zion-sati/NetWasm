using NetWasm.Compiler.Cli.Tests.TestSupport;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class ComponentizeCliOptionsTests
{
    private static readonly string[] Required =
        ["-coremodule:application.wasm", "-wit:contract.wit", "-out:component.wasm", "-manifest:component.json"];

    [Fact]
    public void ReadsEveryOption()
    {
        var options = Parse([
            .. Required, "-runtimemodule:runtime.wasm", "-world:example:test/main", "-platform:wasm64",
            "-interopmanifest:interop.json", "-jcoversion:1.2.3", "-preview2shimversion:4.5.6", "-optimize:none",
            "-managedinlininghints:Preserve",
        ]);

        Assert.Equal("application.wasm", options.CoreModule);
        Assert.Equal("runtime.wasm", options.RuntimeModule);
        Assert.Equal("contract.wit", options.Wit);
        Assert.Equal("example:test/main", options.World);
        Assert.Equal("component.wasm", options.Output);
        Assert.Equal("component.json", options.Manifest);
        Assert.Equal(WasmTarget.Wasm64, options.Platform);
        Assert.Equal("interop.json", options.InteropManifest);
        Assert.Equal("1.2.3", options.JcoVersion);
        Assert.Equal("4.5.6", options.Preview2ShimVersion);
        Assert.Equal(FinalWasmOptimization.None, options.Optimization);
        Assert.Equal("Preserve", options.ManagedInliningHints);
    }

    [Fact]
    public void DefaultsTheOptionalOptions()
    {
        var options = Parse(Required);

        Assert.Null(options.RuntimeModule);
        Assert.Null(options.World);
        Assert.Equal(WasmTarget.Wasm32, options.Platform);
        Assert.Null(options.InteropManifest);
        Assert.Null(options.JcoVersion);
        Assert.Null(options.Preview2ShimVersion);
        Assert.Equal(FinalWasmOptimization.Oz, options.Optimization);
        Assert.Null(options.ManagedInliningHints);
    }

    [Theory]
    [InlineData("componentize takes no input files: App.dll", "App.dll")]
    [InlineData("no core module was specified (-coremodule)", "-coremodule")]
    [InlineData("no WIT package was specified (-wit)", "-wit")]
    [InlineData("no output file was specified (-out)", "-out")]
    [InlineData("no manifest file was specified (-manifest)", "-manifest")]
    [InlineData("-platform must be 'wasm32' or 'wasm64'", "-platform:wasm128")]
    [InlineData("-optimize must be 'None', 'O0', 'O1', 'O2', 'O3', 'Os' or 'Oz'", "-optimize:speed")]
    [InlineData("option '-managedinlininghints' was specified more than once",
        "-managedinlininghints:Preserve", "-managedinlininghints:Auto")]
    public void RejectsAnInvalidCommandLine(string message, params string[] change)
    {
        // Each case drops the required option it names, or adds what it lists.
        string[] arguments = change[0] is "-coremodule" or "-wit" or "-out" or "-manifest"
            ? [.. Required.Where(argument => !argument.StartsWith(change[0] + ":", StringComparison.Ordinal))]
            : [.. Required, .. change];

        var exception = Assert.Throws<CompilerException>(() => Parse(arguments));

        Assert.Equal(DiagnosticCode.InvalidCommandLine, exception.Diagnostic.Code);
        Assert.Equal(message, exception.Diagnostic.Message);
    }

    [Fact]
    public void RejectsMissingArguments() =>
        Assert.Throws<ArgumentNullException>(() => ComponentizeCliOptions.Parse(null!));

    private static ComponentizeCliOptions Parse(string[] arguments) =>
        ComponentizeCliOptions.Parse(NwcTestArguments.Read(ComponentizeCliOptions.Names, arguments));
}
