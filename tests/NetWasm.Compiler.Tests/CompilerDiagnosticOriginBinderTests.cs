using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticOriginBinderTests
{
    private static readonly AssemblyIdentity Assembly = new("Library");
    private static readonly CompilerDiagnostic Diagnostic = new(DiagnosticCode.AssemblyResolution, "Missing requirement.")
    {
        Context = new(new(Assembly, 0x06000001), "Library.Run"),
    };
    private static readonly CompilationReferenceOrigin Origin = new("cache/library.dll",
        CompilationReferenceRole.Implementation, "Example.Library", "2.3.4", "lib/netstandard2.1/library.dll");

    [Fact]
    public void FormattingPreservesTheDependencyOrderAndOriginBinding()
    {
        var root = new CompilerDiagnosticContext(new(new("Application"), 1), "Application.Root");
        var library = new CompilerDiagnosticContext(new(Assembly, 2), "Library.Base");
        var diagnostic = Diagnostic with
        {
            DependencyChain = [new(root, CompilerDependencyKind.BaseType), new(library, CompilerDependencyKind.Interface)],
        };
        var binder = Assert.IsAssignableFrom<ICompilerDiagnosticOriginBinder>(new CompilerDiagnosticOriginBinder());
        var result = binder.Bind(diagnostic, [new(Assembly, Origin.Path)], [Origin]);
        Assert.Equal(diagnostic.DependencyChain, result.DependencyChain);
        Assert.Contains("Application.Root (BaseType) -> Library.Base (Interface)", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("required through", (Diagnostic with { DependencyChain = default }).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BindUsesTheLoadedInputAndPreservesOriginalDiagnostic()
    {
        var binder = Assert.IsAssignableFrom<ICompilerDiagnosticOriginBinder>(new CompilerDiagnosticOriginBinder());
        var result = binder.Bind(Diagnostic, [new(Assembly, Origin.Path)],
            [Origin with { Path = "view.dll", Role = CompilationReferenceRole.Metadata }, Origin, Origin]);
        Assert.Equal(Origin, result.Origin);
        Assert.Same(Diagnostic.Context, result.Context);
        Assert.Equal(Diagnostic.Message, result.Message);
        Assert.Null(Diagnostic.Origin);
        Assert.Contains("Example.Library 2.3.4; lib/netstandard2.1/library.dll", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("cache/", result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("no-context")]
    [InlineData("existing-origin")]
    [InlineData("no-assemblies")]
    [InlineData("no-origins")]
    [InlineData("unknown-assembly")]
    [InlineData("ambiguous-assembly")]
    [InlineData("metadata-only")]
    [InlineData("wrong-path")]
    [InlineData("ambiguous-origin")]
    public void BindLeavesUncertainOrAttributedFailuresUnchanged(string scenario)
    {
        var binder = Assert.IsAssignableFrom<ICompilerDiagnosticOriginBinder>(new CompilerDiagnosticOriginBinder());
        var diagnostic = scenario switch
        {
            "no-context" => Diagnostic with { Context = null },
            "existing-origin" => Diagnostic with { Origin = Origin },
            _ => Diagnostic,
        };
        var assemblies = scenario switch
        {
            "no-assemblies" => default(ImmutableArray<CompilationAssemblySource>),
            "unknown-assembly" => [new(new("Other"), Origin.Path)],
            "ambiguous-assembly" => [new(Assembly, Origin.Path), new(Assembly, "other.dll")],
            _ => [new(Assembly, Origin.Path)],
        };
        var origins = scenario switch
        {
            "no-origins" => default(ImmutableArray<CompilationReferenceOrigin>),
            "metadata-only" => [Origin with { Role = CompilationReferenceRole.Metadata }],
            "wrong-path" => [Origin with { Path = "other.dll" }],
            "ambiguous-origin" => [Origin, Origin with { PackageVersion = "3.0.0" }],
            _ => [Origin],
        };
        Assert.Same(diagnostic, binder.Bind(diagnostic, assemblies, origins));
    }

    [Theory]
    [InlineData(null, null, null, false)]
    [InlineData("Package", null, null, true)]
    [InlineData("Package", "1.0", null, true)]
    [InlineData("Package", null, "lib/library.dll", true)]
    public void FormattingUsesOnlyAvailablePackageMetadata(string? package, string? version, string? asset, bool display)
    {
        var diagnostic = Diagnostic with
        {
            Origin = new("private-cache/library.dll", CompilationReferenceRole.Implementation, package, version, asset),
        };
        Assert.Equal(display, diagnostic.ToString().Contains("[package", StringComparison.Ordinal));
        Assert.DoesNotContain("private-cache", diagnostic.ToString(), StringComparison.Ordinal);
        if (version is not null) Assert.Contains(version, diagnostic.ToString(), StringComparison.Ordinal);
        if (asset is not null) Assert.Contains(asset, diagnostic.ToString(), StringComparison.Ordinal);
        var binder = Assert.IsAssignableFrom<ICompilerDiagnosticOriginBinder>(new CompilerDiagnosticOriginBinder());
        Assert.Throws<ArgumentNullException>(() => binder.Bind(null!, [], []));
    }
}
