using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Tests.ComponentModel.Raw;

public sealed class RawCompilerImportDeclarationBuilderTests
{
    private static readonly RawCanonicalImportIdentity WitIdentity = new("cm64p2", "accept");
    private static readonly RawCanonicalImportIdentity JavaScriptIdentity = new("consumer.math", "add");
    private static readonly RawCanonicalImportIdentity RuntimeIdentity = new("netwasm.host.v1", "report_terminal_exception");

    [Fact]
    public void ClassifiesExactCompilerDeclarationsByTheirDeclaredOwner()
    {
        var javaScript = Import(JavaScriptIdentity, CliValueKind.I4, CliValueKind.I4, CliValueKind.I4);
        var runtime = Import(RuntimeIdentity, CliValueKind.Void, CliValueKind.I4, CliValueKind.NativeInt, CliValueKind.I4);
        var wit = Import(WitIdentity, CliValueKind.I4, CliValueKind.I4);
        var result = CreateBuilder().Build(new(
            Compilation(
                [javaScript, runtime, wit],
                [new(JavaScriptIdentity.Module, JavaScriptIdentity.Name, [], "i32")],
                "wasm64"),
            Catalog(WasmTarget.Wasm64, WitIdentity)));

        var declaredJavaScript = Assert.Single(result.JavaScriptImports);
        Assert.Equal(JavaScriptIdentity, declaredJavaScript.Key);
        Assert.Equal(javaScript.Type.Parameters, declaredJavaScript.Value.Parameters);
        Assert.Equal(javaScript.Type.Result, declaredJavaScript.Value.Result);
        var declaredRuntime = Assert.Single(result.RuntimeImports);
        Assert.Equal(RuntimeIdentity, declaredRuntime.Key);
        Assert.Equal(runtime.Type.Parameters, declaredRuntime.Value.Parameters);
        Assert.Equal(runtime.Type.Result, declaredRuntime.Value.Result);
        Assert.DoesNotContain(WitIdentity, result.JavaScriptImports.Keys);
        Assert.DoesNotContain(WitIdentity, result.RuntimeImports.Keys);
    }

    [Fact]
    public void CarriesTheImportModulesTheManifestDeclares()
    {
        var source = Compilation();
        var declared = source with
        {
            InteropManifest = source.InteropManifest with { ImportModules = ["env", "libc", "env"] },
        };
        var undeclared = source with { InteropManifest = source.InteropManifest with { ImportModules = default } };

        Assert.Equal(
            ["env", "libc"],
            CreateBuilder().Build(new(declared, Catalog())).ImportModules.Order(StringComparer.Ordinal));
        Assert.Empty(CreateBuilder().Build(new(undeclared, Catalog())).ImportModules);
    }

    [Fact]
    public void RejectsMissingRequestOrRequiredObjects()
    {
        var builder = CreateBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
        Assert.Throws<ArgumentNullException>(() => builder.Build(new(null!, Catalog())));
        Assert.Throws<ArgumentNullException>(() => builder.Build(new(
            new([], null!), Catalog())));
        Assert.Throws<ArgumentNullException>(() => builder.Build(new(Compilation(), null!)));
        Assert.Throws<ArgumentNullException>(() => builder.Build(new(
            Compilation(), Catalog() with { Imports = null! })));
    }

    [Fact]
    public void RejectsImplicitCollectionsAndTargetDrift()
    {
        var builder = CreateBuilder();
        Assert.Throws<CompilerException>(() => builder.Build(new(
            Compilation() with { FunctionImports = default }, Catalog())));
        Assert.Throws<CompilerException>(() => builder.Build(new(
            new([], new(
                1,
                "wasm32",
                new(0, 1, 0),
                new(4, 4, 8, 4, 8, 0, 4),
                default,
                [])),
            Catalog())));
        Assert.Throws<CompilerException>(() => builder.Build(new(
            Compilation(), Catalog(WasmTarget.Wasm64))));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(new(
            Compilation(manifestTarget: "wasm32"),
            Catalog((WasmTarget)int.MaxValue))));
    }

    [Fact]
    public void RejectsInvalidOrCollidingManifestIdentities()
    {
        var builder = CreateBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.Build(new(
            Compilation(manifestImports: [null!]), Catalog())));
        Assert.Throws<ArgumentException>(() => builder.Build(new(
            Compilation(manifestImports: [new("", "call", [], "void")]), Catalog())));
        Assert.Throws<CompilerException>(() => builder.Build(new(
            Compilation(manifestImports:
            [
                new("host", "call", [], "void"),
                new("host", "call", [], "void"),
            ]), Catalog())));
        Assert.Throws<CompilerException>(() => builder.Build(new(
            Compilation(
                [Import(WitIdentity)],
                [new(WitIdentity.Module, WitIdentity.Name, [], "void")]),
            Catalog(WasmTarget.Wasm32, WitIdentity))));
    }

    [Fact]
    public void RejectsInvalidCompilerImports()
    {
        var builder = CreateBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.Build(new(
            Compilation([null!]), Catalog())));
        Assert.Throws<ArgumentNullException>(() => builder.Build(new(
            Compilation([new("host", "call", null!)]), Catalog())));
        Assert.Throws<CompilerException>(() => builder.Build(new(
            Compilation([new("host", "call", new(default, CliValueKind.Void))]), Catalog())));
        Assert.Throws<CompilerException>(() => builder.Build(new(
            Compilation([Import(new("host", "call"), CliValueKind.Void, CliValueKind.Void)]), Catalog())));
        Assert.Throws<CompilerException>(() => builder.Build(new(
            Compilation([Import(new("host", "call"), (CliValueKind)int.MaxValue)]), Catalog())));
        Assert.Throws<CompilerException>(() => builder.Build(new(
            Compilation([
                Import(new("host", "call")),
                Import(new("host", "call")),
            ]), Catalog())));
    }

    [Fact]
    public void RejectsManifestImportMissingFromCompilerInventory()
    {
        Assert.Throws<CompilerException>(() => CreateBuilder().Build(new(
            Compilation(
                [],
                [new(JavaScriptIdentity.Module, JavaScriptIdentity.Name, [], "void")]),
            Catalog())));
    }

    private static RawCompilerImportDeclarationBuilder CreateBuilder() =>
        new RawCompilerImportDeclarationBuilder();

    private static WasmFunctionImport Import(
        RawCanonicalImportIdentity identity,
        CliValueKind result = CliValueKind.Void,
        params CliValueKind[] parameters) =>
        new(identity.Module, identity.Name, WasmFunctionType.Create(result, parameters));

    private static RawCompilerImportSource Compilation(
        ImmutableArray<WasmFunctionImport> imports = default,
        ImmutableArray<HostInteropImport> manifestImports = default,
        string manifestTarget = "wasm32") =>
        new(imports.IsDefault ? [] : imports, new(
            1,
            manifestTarget,
            new(0, 1, 0),
            new(4, 4, 8, 4, 8, 0, 4),
            manifestImports.IsDefault ? [] : manifestImports,
            []));

    private static RawWitImportCatalog Catalog(
        WasmTarget target = WasmTarget.Wasm32,
        params RawCanonicalImportIdentity[] identities) =>
        new(
            new([], [], [], [], "{}"),
            new(0, "main", "example:test@1", [], []),
            target,
            identities.ToImmutableDictionary(
                identity => identity,
                _ => (RawWitImportDeclaration)new RawWitImportDeclaration.Callable(
                    "",
                    new("accept", [], null, new("freestanding")))));
}
