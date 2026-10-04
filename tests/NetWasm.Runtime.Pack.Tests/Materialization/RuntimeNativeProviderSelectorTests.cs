using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeNativeProviderSelectorTests
{
    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void SelectsExactProvidersAndDeduplicatesPhysicalSymbols(string target)
    {
        var provider = Provider(target);
        var alias = provider with { LibraryName = "alias", Sha256 = provider.Sha256.ToUpperInvariant() };
        var sum = Import("mule", "sum");
        var duplicated = sum with { Parameters = [RuntimeNativeValueType.I32, RuntimeNativeValueType.I32] };
        var selector = Assert.IsAssignableFrom<IRuntimeNativeProviderSelector>(new RuntimeNativeProviderSelector());
        var result = selector.Select(new(target,
            [sum, Import("mule", "other"), duplicated, Import("alias", "sum")],
            [provider, provider with { Path = Path.Combine(Path.GetDirectoryName(provider.Path)!, ".", "mule.a") },
                Provider(target == "wasm32" ? "wasm64" : "wasm32"), alias,
                Provider(target) with { LibraryName = "unused" }]));

        Assert.Equal(["other", "sum"], result.Select(binding => binding.Import.EntryPoint).ToArray());
        Assert.Equal(["mule", "alias"], result.Select(binding => binding.Import.LibraryName).ToArray());
        Assert.All(result, binding =>
        {
            Assert.Equal(provider.Path, binding.Provider.Path);
            Assert.Equal(provider.Sha256, binding.Provider.Sha256);
            Assert.Equal(target, binding.Provider.Target);
            Assert.Equal([RuntimeNativeValueType.I32, RuntimeNativeValueType.I32], binding.Import.Parameters.ToArray());
            Assert.Equal(RuntimeNativeValueType.I32, binding.Import.ReturnType);
        });
    }

    [Fact]
    public void UnusedProvidersDoNotCreateBindingsOrConflicts()
    {
        var provider = Provider("wasm32");
        Assert.Empty(new RuntimeNativeProviderSelector().Select(new("wasm32", [],
            [provider, provider with { Path = Path.GetFullPath("other.a") }])));
    }

    [Fact]
    public void LogicalNamesAndSymbolsAreOrdinalAndExplicitInternalIsSupported()
    {
        var selector = new RuntimeNativeProviderSelector();
        Assert.Throws<InvalidOperationException>(() => selector.Select(new("wasm32",
            [Import("Mule", "sum")], [Provider("wasm32")])));
        var result = selector.Select(new("wasm32",
            [Import("__Internal", "sum"), Import("__Internal", "Sum")],
            [Provider("wasm32") with { LibraryName = "__Internal" }]));
        Assert.Equal(["Sum", "sum"], result.Select(binding => binding.Import.EntryPoint).ToArray());
    }

    [Theory]
    [InlineData("path")]
    [InlineData("digest")]
    public void RejectsConflictingContributions(string conflict)
    {
        var provider = Provider("wasm32");
        var other = conflict == "path" ? provider with { Path = Path.GetFullPath("different.a") }
            : provider with { Sha256 = new string('b', 64) };
        Assert.Throws<InvalidOperationException>(() => new RuntimeNativeProviderSelector().Select(new("wasm32",
            [Import("mule", "sum")], [provider, other])));
    }

    [Theory]
    [InlineData("parameters")]
    [InlineData("return")]
    [InlineData("provider")]
    public void RejectsGlobalSymbolAmbiguity(string conflict)
    {
        var import = Import("mule", "sum");
        var other = conflict switch
        {
            "parameters" => import with { Parameters = [RuntimeNativeValueType.I64] },
            "return" => import with { ReturnType = null },
            "provider" => import with { LibraryName = "other" },
            _ => throw new ArgumentOutOfRangeException(nameof(conflict)),
        };
        Assert.Throws<InvalidOperationException>(() => new RuntimeNativeProviderSelector().Select(new("wasm32",
            [import, other], [Provider("wasm32"), Provider("wasm32") with
            {
                LibraryName = "other", Path = Path.GetFullPath("other.a"),
            }])));
    }

    [Fact]
    public void RejectsMissingOrWrongTargetProvider()
    {
        var selector = new RuntimeNativeProviderSelector();
        Assert.Throws<InvalidOperationException>(() => selector.Select(new("wasm32", [Import("mule", "sum")], [])));
        Assert.Throws<InvalidOperationException>(() => selector.Select(new("wasm32", [Import("mule", "sum")], [Provider("wasm64")])));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("library")]
    [InlineData("nul-library")]
    [InlineData("entry")]
    [InlineData("nul-entry")]
    [InlineData("newline-entry")]
    [InlineData("trace-marker-entry")]
    [InlineData("default-parameters")]
    [InlineData("parameter-kind")]
    [InlineData("return-kind")]
    public void RejectsInvalidImportsBeforeSelection(string invalid)
    {
        var import = Import("mule", "sum");
        import = invalid switch
        {
            "null" => null!,
            "library" => import with { LibraryName = " " },
            "nul-library" => import with { LibraryName = "a\0b" },
            "entry" => import with { EntryPoint = " " },
            "nul-entry" => import with { EntryPoint = "a\0b" },
            "newline-entry" => import with { EntryPoint = "a\nb" },
            "trace-marker-entry" => import with { EntryPoint = "a: definition of b" },
            "default-parameters" => import with { Parameters = default },
            "parameter-kind" => import with { Parameters = [(RuntimeNativeValueType)99] },
            "return-kind" => import with { ReturnType = (RuntimeNativeValueType)99 },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };
        Assert.Throws<InvalidOperationException>(() => new RuntimeNativeProviderSelector().Select(new("wasm32", [import], [])));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("name")]
    [InlineData("target")]
    [InlineData("empty-path")]
    [InlineData("relative-path")]
    [InlineData("nul-path")]
    [InlineData("newline-path")]
    [InlineData("null-digest")]
    [InlineData("short-digest")]
    [InlineData("nonhex-digest")]
    public void RejectsInvalidProviderDescriptors(string invalid)
    {
        var provider = Provider("wasm32");
        provider = invalid switch
        {
            "null" => null!,
            "name" => provider with { LibraryName = " " },
            "target" => provider with { Target = "unknown" },
            "empty-path" => provider with { Path = " " },
            "relative-path" => provider with { Path = "native/mule.a" },
            "nul-path" => provider with { Path = provider.Path + '\0' },
            "newline-path" => provider with { Path = provider.Path + '\n' },
            "null-digest" => provider with { Sha256 = null! },
            "short-digest" => provider with { Sha256 = "abc" },
            "nonhex-digest" => provider with { Sha256 = new string('z', 64) },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };
        Assert.Throws<InvalidOperationException>(() => new RuntimeNativeProviderSelector().Select(new("wasm32", [], [provider])));
    }

    [Fact]
    public void RejectsNullAndUninitializedSelectionRequests()
    {
        var selector = new RuntimeNativeProviderSelector();
        Assert.Throws<ArgumentNullException>(() => selector.Select(null!));
        Assert.Throws<InvalidOperationException>(() => selector.Select(new("unknown", [], [])));
        Assert.Throws<InvalidOperationException>(() => selector.Select(new("wasm32", default, [])));
        Assert.Throws<InvalidOperationException>(() => selector.Select(new("wasm32", [], default)));
    }

    private static RuntimeNativeImport Import(string library, string entry) =>
        new(library, entry, [RuntimeNativeValueType.I32, RuntimeNativeValueType.I32], RuntimeNativeValueType.I32);

    private static RuntimeNativeLibrary Provider(string target) =>
        new("mule", target, Path.GetFullPath(Path.Combine("native", target, "mule.a")), RuntimePackTestData.Digest);
}
