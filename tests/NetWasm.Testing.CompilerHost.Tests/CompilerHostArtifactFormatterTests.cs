using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Testing.CompilerHost;

namespace NetWasm.Testing.CompilerHost.Tests;

public sealed class CompilerHostArtifactFormatterTests
{
    private readonly CompilerHostArtifactFormatter _formatter = new CompilerHostArtifactFormatter();

    [Theory]
    [InlineData(WasmTarget.Wasm32, "wasm32", 4)]
    [InlineData(WasmTarget.Wasm64, "wasm64", 8)]
    public void FormatsBothLinkedContractsWithTargetLocalLayout(WasmTarget target, string name, int width)
    {
        var request = Request with
        {
            Target = target,
            RuntimeLayoutPath = "layout.json",
            InteropManifestPath = "interop.json",
            EmitStackTrace = true,
            StackTraceSymbolsPath = "symbols.json",
        };
        var result = Result with
        {
            InteropManifest = new(1, name, new(0, 1, 0), new(width, 8, 12, 8, 16), [], []),
        };

        var artifacts = ((ICompilerHostArtifactFormatter)_formatter).Format(request, result);

        Assert.Equal(["module.wasm", "symbols.json", "layout.json", "interop.json"],
            artifacts.Select(artifact => artifact.Path));
        Assert.Equal(result.ApplicationModule, artifacts[0].Bytes);
        Assert.Equal(result.StackTraceSymbols!.Bytes, artifacts[1].Bytes);
        using var layout = JsonDocument.Parse(artifacts[2].Bytes.AsMemory());
        Assert.Equal(3, layout.RootElement.EnumerateObject().Count());
        Assert.Equal(2, layout.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(name, layout.RootElement.GetProperty("target").GetString());
        Assert.Equal(1234, layout.RootElement.GetProperty("applicationStaticDataEnd").GetInt32());
        using var interop = JsonDocument.Parse(artifacts[3].Bytes.AsMemory());
        Assert.Equal(1, interop.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(name, interop.RootElement.GetProperty("target").GetString());
        Assert.Equal(width, interop.RootElement.GetProperty("targetLayout").GetProperty("managedReferenceSize").GetInt32());
        Assert.Empty(interop.RootElement.GetProperty("imports").EnumerateArray());
        Assert.Empty(interop.RootElement.GetProperty("exports").EnumerateArray());
    }

    [Fact]
    public void LegacyRequestsNeedNoSidecarsAndSnapshotModuleBytes()
    {
        var result = Result with { InteropManifest = null!, StackTraceSymbols = null };

        var artifact = Assert.Single(((ICompilerHostArtifactFormatter)_formatter).Format(Request, result));
        result.ApplicationModule[0] = 99;

        Assert.Equal("module.wasm", artifact.Path);
        Assert.Equal(new byte[] { 0, 97, 115, 109 }, artifact.Bytes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SidecarsCanBeRequestedIndependently(bool layout)
    {
        var request = Request with
        {
            RuntimeLayoutPath = layout ? "layout.json" : null,
            InteropManifestPath = layout ? null : "interop.json",
        };

        var artifacts = ((ICompilerHostArtifactFormatter)_formatter).Format(request, Result);

        Assert.Equal(2, artifacts.Length);
        Assert.Equal(layout ? "layout.json" : "interop.json", artifacts[1].Path);
    }

    [Fact]
    public void RejectsMissingRequestedArtifactData()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ((ICompilerHostArtifactFormatter)_formatter).Format(Request with { EmitStackTrace = true }, Result with { StackTraceSymbols = null }));
        Assert.Throws<ArgumentNullException>(() =>
            ((ICompilerHostArtifactFormatter)_formatter).Format(Request with { InteropManifestPath = "interop.json" }, Result with { InteropManifest = null! }));
        Assert.Throws<ArgumentNullException>(() =>
            ((ICompilerHostArtifactFormatter)_formatter).Format(Request, Result with { ApplicationModule = null! }));
    }

    [Fact]
    public void RejectsInvalidLayoutValues()
    {
        var request = Request with { RuntimeLayoutPath = "layout.json" };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ((ICompilerHostArtifactFormatter)_formatter).Format(request with { Target = (WasmTarget)99 }, Result));
        Assert.Throws<InvalidOperationException>(() =>
            ((ICompilerHostArtifactFormatter)_formatter).Format(request, Result with { StaticDataEnd = -1 }));
        using var layout = JsonDocument.Parse(((ICompilerHostArtifactFormatter)_formatter).Format(request, Result with { StaticDataEnd = 0 })[1].Bytes.AsMemory());
        Assert.Equal(0, layout.RootElement.GetProperty("applicationStaticDataEnd").GetInt32());
    }

    [Fact]
    public void RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => ((ICompilerHostArtifactFormatter)_formatter).Format(null!, Result));
        Assert.Throws<ArgumentNullException>(() => ((ICompilerHostArtifactFormatter)_formatter).Format(Request, null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingRequiredOutputPaths(string? path)
    {
        Assert.ThrowsAny<ArgumentException>(() => ((ICompilerHostArtifactFormatter)_formatter).Format(Request with { ModulePath = path! }, Result));
        Assert.ThrowsAny<ArgumentException>(() => ((ICompilerHostArtifactFormatter)_formatter).Format(Request with
        {
            EmitStackTrace = true,
            StackTraceSymbolsPath = path,
        }, Result));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("module.wasm")]
    public void RejectsEmptyOrConflictingSidecarPaths(string path)
    {
        Assert.Throws<ArgumentException>(() => ((ICompilerHostArtifactFormatter)_formatter).Format(Request with { RuntimeLayoutPath = path }, Result));
        Assert.Throws<ArgumentException>(() => ((ICompilerHostArtifactFormatter)_formatter).Format(Request with { InteropManifestPath = path }, Result));
        Assert.Throws<ArgumentException>(() => ((ICompilerHostArtifactFormatter)_formatter).Format(Request with { EmitStackTrace = true, StackTraceSymbolsPath = path }, Result));
    }

    private static CompilationRequest Request => new(
        "entry.dll", [], "Entry", "Run", [], WasmTarget.Wasm32, null, [],
        ImmutableDictionary<string, string>.Empty, "module.wasm");

    private static CompilationResult Result => new(
        [0, 97, 115, 109], null!, null!,
        new(1, "wasm32", new(0, 1, 0), new(4, 8, 12, 8, 16), [], []), 1234)
    {
        StackTraceSymbols = new([1, 2, 3], "application/json", "symbols.json", "hash"),
    };
}
