using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using NetWasm.Compiler.Tests.Symbols;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.TestInfrastructure;
using Xunit;

namespace NetWasm.Compiler.Tests;

public sealed class StackTraceArtifactTests
{
    [Fact]
    public void InstrumentedCompilerWritesReachableSymbolsToTheSidecarAlone()
    {
        using var assets = TestAssets.Create();
        const string identity = "UniqueTraceFixture.EntryPoint::UniqueTraceMethod";
        var assembly = assets.CompileSource(
            "UniqueTraceFixture",
            """
            namespace UniqueTraceFixture;

            public static class EntryPoint
            {
                public static int UniqueTraceMethod(int value) => value;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "UniqueTraceFixture.EntryPoint",
            "UniqueTraceMethod",
            [],
            EmitStackTrace: true));

        // The module carries no frame text: it loads the method-symbols sidecar (issue 225).
        Assert.Contains(SymbolSidecarContents.Read(result.MethodSymbols!.Bytes).Texts,
            text => text!.Contains(identity, StringComparison.Ordinal));
        Assert.Equal(-1, result.ApplicationModule.AsSpan().IndexOf(
            Encoding.UTF8.GetBytes(identity)));
        Assert.Equal(-1, result.ApplicationModule.AsSpan().IndexOf(
            Encoding.Unicode.GetBytes(identity)));
    }

    [Fact]
    [Trait("Issue", "64")]
    public void PortablePdbUsesSafeDocumentIdentities()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "SafeTraceFixture",
            """
            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    if (value == 0)
                    {
            #line 42 "/private/developer/work/Secret.cs"
                        return value;
                    }
            #line 73 "/_/src/Mapped.cs"
                    return value + 1;
            #line default
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            EmitStackTrace: true));

        var json = string.Join('\n', SymbolSidecarContents.Read(result.MethodSymbols!.Bytes).Texts);
        Assert.Contains("Secret.cs:line 42", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/private/developer/work", json, StringComparison.Ordinal);
        Assert.Contains("/_/src/Mapped.cs:line 73", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("src/File.cs", null, "src/File.cs")]
    [InlineData("C:/work/developer/File.cs", null, "File.cs")]
    [InlineData("//server/share/File.cs", null, "File.cs")]
    [InlineData("/mapped", "source=/mapped", "/mapped")]
    [InlineData("/private/source/File.cs", "missing-equals,source=/other", "File.cs")]
    [InlineData("a", null, "a")]
    [InlineData("123", null, "123")]
    [InlineData("abc", null, "abc")]
    [InlineData("a:x", null, "a:x")]
    public void SourceDocumentIdentityFormattingIsPortable(
        string document,
        string? pathMap,
        string expected)
    {
        var formatter = new SourceDocumentIdentityFormatter();

        var identity = formatter.Format(
            document,
            Options(false) with { PathMap = pathMap });

        Assert.Equal(expected, identity);
    }

    [Fact]
    public void SourceDocumentIdentityFormattingHandlesProjectParentsAndHostPolicy()
    {
        var formatter = new SourceDocumentIdentityFormatter();
        var options = Options(false) with
        {
            ProjectDirectory = "/mapped/project",
            PathMap = "source=/",
        };

        Assert.Equal("mapped", formatter.Format("/mapped", options));
        Assert.Equal(
            "File.cs",
            formatter.Format("/mapped/other/File.cs", options));
        Assert.True(SourceDocumentIdentityFormatter.IsForeignWindowsPath(
            "C:/source/File.cs",
            isWindows: false));
        Assert.False(SourceDocumentIdentityFormatter.IsForeignWindowsPath(
            "C:/source/File.cs",
            isWindows: true));
    }

    [Fact]
    [Trait("Issue", "64")]
    public void PortablePdbDoesNotTreatForeignWindowsPathAsProjectRelative()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "ForeignPathTraceFixture",
            """
            public static class EntryPoint
            {
                public static int Run(int value)
                {
            #line 12 "C:\\work\\developer\\Secret.cs"
                    return value;
            #line default
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            EmitStackTrace: true,
            ProjectDirectory: assets.Directory));

        var json = string.Join('\n', SymbolSidecarContents.Read(result.MethodSymbols!.Bytes).Texts);
        Assert.Contains("Secret.cs:line 12", json, StringComparison.Ordinal);
        Assert.DoesNotContain("work/developer", json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void PortablePdbPreservesConfiguredPathMapDestination()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "ConfiguredPathMapTraceFixture",
            """
            public static class EntryPoint
            {
                public static int Run(int value)
                {
            #line 23 "/mapped/src/Program.cs"
                    return value;
            #line default
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            EmitStackTrace: true,
            ProjectDirectory: assets.Directory,
            PathMap: assets.Directory + "=/mapped"));

        var json = string.Join('\n', SymbolSidecarContents.Read(result.MethodSymbols!.Bytes).Texts);
        Assert.Contains("/mapped/src/Program.cs:line 23", json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void MalformedPortablePdbFallsBackToMethodSymbols()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "MalformedTracePdb",
            "public static class EntryPoint { public static int Run(int value) => value; }");
        File.WriteAllText(Path.ChangeExtension(assembly, ".pdb"), "not a portable PDB");

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            EmitStackTrace: true));

        // The fixture's frames have no lines; CoreLib's own frames keep theirs.
        var frames = SymbolSidecarContents.Read(result.MethodSymbols!.Bytes).Texts
            .Where(text => text!.StartsWith("EntryPoint::", StringComparison.Ordinal))
            .ToArray();
        Assert.Contains("EntryPoint::Run", frames);
        Assert.DoesNotContain(frames, frame => frame!.Contains(":line ", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Issue", "64")]
    public void MalformedSourceLinkRootFallsBackToPdbDocument()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdbAndSourceLink(
            "MalformedSourceLinkFixture",
            """
            public static class EntryPoint
            {
                public static int Run(int value)
                {
            #line 31 "Safe/Program.cs"
                    return value;
            #line default
                }
            }
            """,
            "[]");

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            EmitStackTrace: true));

        var json = string.Join('\n', SymbolSidecarContents.Read(result.MethodSymbols!.Bytes).Texts);
        Assert.Contains("EntryPoint::Run", json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void PortablePdbPreservesSourceLinkDocumentIdentity()
    {
        using var assets = TestAssets.Create();
        var sourceRoot = assets.Directory.Replace('\\', '/').TrimEnd('/');
        var sourceLink = JsonSerializer.Serialize(new
        {
            documents = new Dictionary<string, string>
            {
                [sourceRoot + "/*"] =
                    "https://raw.githubusercontent.com/example/repository/commit/*",
            },
        });
        var document = sourceRoot + "/Mapped.cs";
        var assembly = assets.CompileSourceWithPortablePdbAndSourceLink(
            "SourceLinkTraceFixture",
            $$"""
            public static class EntryPoint
            {
                public static int Run(int value)
                {
            #line 17 "{{document}}"
                    return value;
            #line default
                }
            }
            """,
            sourceLink);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            EmitStackTrace: true));

        var json = string.Join('\n', SymbolSidecarContents.Read(result.MethodSymbols!.Bytes).Texts);
        Assert.Contains(
            "https://raw.githubusercontent.com/example/repository/commit/Mapped.cs:line 17",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain(sourceRoot, json, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceLocationReaderRejectsMissingInputsAndSkipsDisabledTracing()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new StackTraceSourceLocationReader(null!));
        var reader = new StackTraceSourceLocationReader(
            new SourceDocumentIdentityFormatter());

        Assert.Throws<ArgumentNullException>(() => reader.Read(null!, Options(true)));
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DisabledTraceFixture",
            "public static class EntryPoint { public static void Run() { } }");
        using var metadata = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        Assert.Throws<ArgumentNullException>(() => reader.Read(metadata.Snapshot, null!));
        Assert.Empty(reader.Read(metadata.Snapshot, Options(false)));
    }

    [Fact]
    public void MissingPortablePdbLeavesSourceLocationsEmpty()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "MissingTracePdb",
            "public static class EntryPoint { public static int Run(int value) => value; }");
        File.Delete(Path.ChangeExtension(assembly, ".pdb"));
        using var metadata = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);

        var locations = new StackTraceSourceLocationReader(
            new SourceDocumentIdentityFormatter()).Read(
                metadata.Snapshot,
                Options(true));

        Assert.DoesNotContain(
            locations.Keys,
            key => key.Assembly == metadata.Snapshot.EntryAssemblyIdentity);
    }

    [Fact]
    public void OptionalSourceMetadataFailuresLeaveMethodOnlyFrames()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "OptionalTraceMetadata",
            "public static class EntryPoint { public static int Run(int value) => value; }");
        using var metadata = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        Exception[] failures =
        [
            new BadImageFormatException(),
            new IOException(),
            new UnauthorizedAccessException(),
            new JsonException(),
            new ArgumentException(),
        ];

        foreach (var failure in failures)
        {
            var locations = new StackTraceSourceLocationReader(
                new ThrowingDocumentFormatter(failure)).Read(
                    metadata.Snapshot,
                    Options(true));
            Assert.Empty(locations);
        }
    }

    [Fact]
    public void PortablePdbUsesExactSourceLinkDocumentMapping()
    {
        using var assets = TestAssets.Create();
        var source = assets.Directory.Replace('\\', '/').TrimEnd('/') + "/Exact.cs";
        const string expected =
            "https://raw.githubusercontent.com/example/repository/commit/Exact.cs";
        var sourceLink = JsonSerializer.Serialize(new
        {
            documents = new Dictionary<string, string>
            {
                ["/different/Exact.cs"] = "https://example.invalid/different.cs",
                [source] = expected,
            },
        });
        var assembly = assets.CompileSourceWithPortablePdbAndSourceLink(
            "ExactSourceLinkFixture",
            $$"""
            public static class EntryPoint
            {
                public static int Run(int value)
                {
            #line 19 "{{source}}"
                    return value;
            #line default
                }
            }
            """,
            sourceLink);
        using var metadata = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);

        var locations = new StackTraceSourceLocationReader(
            new SourceDocumentIdentityFormatter()).Read(
                metadata.Snapshot,
                Options(true));

        Assert.Contains(
            locations.Values.SelectMany(static value => value),
            location => location.Document == expected && location.StartLine == 19);
    }

    [Fact]
    public void PortablePdbRetainsHiddenSequencePointOffsets()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "HiddenSequencePointFixture",
            """
            public static class EntryPoint
            {
                public static int Run(int value)
                {
            #line hidden
                    value++;
            #line default
                    return value;
                }
            }
            """);
        using var metadata = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);

        var locations = new StackTraceSourceLocationReader(
            new SourceDocumentIdentityFormatter()).Read(
                metadata.Snapshot,
                Options(true));

        Assert.Contains(
            locations.Values.SelectMany(static value => value),
            location => location.Document is null && location.StartLine == 0);
    }

    [Fact]
    public void InvalidSourceLinkEntriesAndNonMatchingPatternsAreIgnored()
    {
        using var assets = TestAssets.Create();
        const string sourceLink =
            """
            {
              "documents": {
                "number": 42,
                "empty": "",
                "two*wildcards*": "https://example.invalid/two/*",
                "wrong-prefix/*": "https://example.invalid/prefix/*",
                "abc*tail": "https://example.invalid/suffix/*",
                "abc*bcd": "https://example.invalid/overlap/*",
                "different": "https://example.invalid/exact.cs"
              }
            }
            """;
        var assembly = assets.CompileSourceWithPortablePdbAndSourceLink(
            "IgnoredSourceLinkFixture",
            """
            public static class EntryPoint
            {
                public static int Run(int value)
                {
            #line 27 "abcd"
                    return value;
            #line default
                }
            }
            """,
            sourceLink);
        using var metadata = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);

        var locations = new StackTraceSourceLocationReader(
            new SourceDocumentIdentityFormatter()).Read(
                metadata.Snapshot,
                Options(true));

        Assert.Contains(
            locations.Values.SelectMany(static value => value),
            location => location.Document == "abcd" && location.StartLine == 27);
        Assert.DoesNotContain(
            locations.Values.SelectMany(static value => value),
            location => location.Document?.StartsWith(
                "https://example.invalid/",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public void SourceLocationReaderHandlesOptionalPdbDocumentPolicies()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "SourceLocationPolicyFixture",
            "public static class EntryPoint { public static void Run() { } }");
        using var stream = File.OpenRead(Path.ChangeExtension(assembly, ".pdb"));
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        var metadata = provider.GetMetadataReader();
        var document = Assert.Single(metadata.Documents);
        var reader = new StackTraceSourceLocationReader(
            new SourceDocumentIdentityFormatter());

        Assert.True(StackTraceSourceLocationReader.HasMethodDebugInformation(1, 1));
        Assert.False(StackTraceSourceLocationReader.HasMethodDebugInformation(2, 1));
        Assert.Equal(
            document,
            StackTraceSourceLocationReader.SelectDocument(default, document));
        Assert.Equal(
            document,
            StackTraceSourceLocationReader.SelectDocument(document, default));
        Assert.Null(StackTraceSourceLocationReader.ReadDocument(metadata, default));
        var documentName = Assert.IsType<string>(
            StackTraceSourceLocationReader.ReadDocument(metadata, document));

        var noDocument = reader.CreateLocation(
            3,
            27,
            null,
            [],
            Options(true));
        var blankDocument = reader.CreateLocation(
            3,
            27,
            " ",
            [],
            Options(true));
        var sourceDocument = reader.CreateLocation(
            3,
            27,
            documentName,
            [],
            Options(true));

        Assert.Null(noDocument.Document);
        Assert.Equal(0, noDocument.StartLine);
        Assert.Null(blankDocument.Document);
        Assert.Equal(0, blankDocument.StartLine);
        Assert.NotNull(sourceDocument.Document);
        Assert.Equal(27, sourceDocument.StartLine);
    }

    private static CompilerOptions Options(bool emitStackTrace) =>
        new(
            "application.dll",
            [],
            "Example.Program",
            "Main",
            [],
            EmitStackTrace: emitStackTrace);

    private sealed class ThrowingDocumentFormatter(Exception failure) :
        ISourceDocumentIdentityFormatter
    {
        public string Format(string document, CompilerOptions options) =>
            throw failure;
    }
}
