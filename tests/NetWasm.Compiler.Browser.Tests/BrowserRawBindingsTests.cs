using System.Collections.Immutable;
using System.Text;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Browser.Tests;

public sealed class BrowserRawBindingsTests
{
    private const string ApplicationPath = "application.wit.json";
    private const string RuntimePath = "runtime.wit.json";

    [Fact]
    public void BuildsADeploymentAdapterFromSuppliedBrowserSignatures()
    {
        var result = BrowserRawBindings.Build(Request([], []));

        Assert.Empty(result.RequiredImports);
        var adapter = Encoding.UTF8.GetString(result.Adapter);
        Assert.Contains("export function createAdapter", adapter,
            StringComparison.Ordinal);
        Assert.Contains("rawAdapterMetadata", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresExplicitImportSignaturesAndValidInputs()
    {
        Assert.Throws<ArgumentNullException>(() => BrowserRawBindings.Build(null!));
        Assert.Throws<ArgumentException>(() => Request(default, []));
        Assert.Throws<ArgumentException>(() => Request([], default));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateRequest(
            [], [], (WasmTarget)int.MaxValue));
    }

    [Fact]
    public void PreservesBindingValidationDiagnostics()
    {
        var unexpected = new RawCoreFunctionImportSignature(
            new("unexpected", "call"), [], []);

        var failure = Assert.Throws<CompilerException>(() =>
            BrowserRawBindings.Build(Request([], [unexpected])));

        Assert.Equal(DiagnosticCode.ComponentContract, failure.Diagnostic.Code);
    }

    private static BrowserRawBindingRequest Request(
        ImmutableArray<RawCoreFunctionImportSignature> runtimeImports,
        ImmutableArray<RawCoreFunctionImportSignature> finalImports) =>
        CreateRequest(runtimeImports, finalImports, WasmTarget.Wasm32);

    private static BrowserRawBindingRequest CreateRequest(
        ImmutableArray<RawCoreFunctionImportSignature> runtimeImports,
        ImmutableArray<RawCoreFunctionImportSignature> finalImports,
        WasmTarget target)
    {
        var source = new RawCompilerImportSource([], new(
            1,
            target == WasmTarget.Wasm64 ? "wasm64" : "wasm32",
            new(0, 1, 0),
            new(4, 4, 8, 4, 8),
            [],
            []));
        return new(
            source,
            ApplicationPath,
            "main",
            RuntimePath,
            "runtime",
            target,
            new Dictionary<string, string>
            {
                [ApplicationPath] = Document("main", "example:application@1.0.0"),
                [RuntimePath] = Document("runtime", "example:runtime@1.0.0"),
            },
            new Dictionary<string, string>(),
            runtimeImports,
            finalImports);
    }

    private static string Document(string world, string package) => $$$"""
        {"packages":[{"name":"{{{package}}}","interfaces":{},"worlds":{"{{{world}}}":0}}],
         "interfaces":[],"types":[],
         "worlds":[{"name":"{{{world}}}","package":0,"imports":{},"exports":{}}]}
        """;
}
