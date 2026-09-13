using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.Core;
using NetWasm.Hosting.Capabilities;
using NetWasm.Hosting.Generator;

namespace NetWasm.Hosting.Generator.Tests;

public sealed class HostingPlatformCatalogApplicationTests
{
    [Fact]
    public void ComposesInputsAndWritesTheGeneratedCatalog()
    {
        var fixture = new Fixture();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = fixture.Create().Run(
            ["wit.json", "shim.json", "catalog.mjs"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal("generated", fixture.Files.Writes["catalog.mjs"]);
        Assert.Equal(["wit.json", "shim.json"], fixture.Files.Reads);
        Assert.Same(fixture.Document, fixture.Catalogs.Document);
        Assert.Same(fixture.World, fixture.Catalogs.World);
        Assert.Same(fixture.Catalog, fixture.Projector.Catalog);
        Assert.Same(fixture.Shim, fixture.Projector.Shim);
        Assert.Same(fixture.Projected, fixture.Writer.Catalog);
        Assert.Equal(
            "Generated 1 platform providers from 1 imported interfaces for netwasm:hosting-platform/preview2@1.0.0.\n",
            output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void RejectsInvalidInvocationWithoutReadingFiles()
    {
        var fixture = new Fixture();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = fixture.Create();

        Assert.Equal(2, application.Run([], output, error));
        Assert.Equal(2, application.Run(["wit", "shim", " "], output, error));
        Assert.Empty(fixture.Files.Reads);
        Assert.Contains("usage:", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMissingRuntimeArgumentsAndDependencies()
    {
        var fixture = new Fixture();
        var application = fixture.Create();
        using var writer = new StringWriter();

        Assert.Throws<ArgumentNullException>(() => application.Run(null!, writer, writer));
        Assert.Throws<ArgumentNullException>(() => application.Run([], null!, writer));
        Assert.Throws<ArgumentNullException>(() => application.Run([], writer, null!));
        Assert.Throws<ArgumentNullException>(() => new HostingPlatformCatalogApplication(
            null!, fixture.Documents, fixture.Catalogs, fixture.Shims, fixture.Projector, fixture.Writer));
        Assert.Throws<ArgumentNullException>(() => new HostingPlatformCatalogApplication(
            fixture.Files, null!, fixture.Catalogs, fixture.Shims, fixture.Projector, fixture.Writer));
        Assert.Throws<ArgumentNullException>(() => new HostingPlatformCatalogApplication(
            fixture.Files, fixture.Documents, null!, fixture.Shims, fixture.Projector, fixture.Writer));
        Assert.Throws<ArgumentNullException>(() => new HostingPlatformCatalogApplication(
            fixture.Files, fixture.Documents, fixture.Catalogs, null!, fixture.Projector, fixture.Writer));
        Assert.Throws<ArgumentNullException>(() => new HostingPlatformCatalogApplication(
            fixture.Files, fixture.Documents, fixture.Catalogs, fixture.Shims, null!, fixture.Writer));
        Assert.Throws<ArgumentNullException>(() => new HostingPlatformCatalogApplication(
            fixture.Files, fixture.Documents, fixture.Catalogs, fixture.Shims, fixture.Projector, null!));
    }

    [Fact]
    public void MapsExpectedBoundaryFailuresWithoutWritingAStack()
    {
        var failures = new Exception[]
        {
            new ArgumentException("argument"),
            new IOException("io"),
            new UnauthorizedAccessException("access"),
            new CompilerException(new(
                DiagnosticCode.ComponentContract, "component")),
        };

        foreach (var failure in failures)
        {
            var fixture = new Fixture();
            fixture.Files.Failure = failure;
            using var output = new StringWriter();
            using var error = new StringWriter();

            Assert.Equal(1, fixture.Create().Run(
                ["wit", "shim", "output"], output, error));
            Assert.Equal(failure.Message + Environment.NewLine, error.ToString());
            Assert.DoesNotContain(failure.StackTrace ?? " at ", error.ToString());
            Assert.Empty(fixture.Files.Writes);
        }
    }

    [Fact]
    public void DoesNotHideUnexpectedProgrammingFailures()
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException();
        fixture.Files.Failure = failure;
        using var writer = new StringWriter();

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            fixture.Create().Run(["wit", "shim", "output"], writer, writer)));
    }

    [Fact]
    public void CompositionRootBuildsTheApplication()
    {
        Assert.IsAssignableFrom<IHostingPlatformCatalogApplication>(Program.Create());
        Assert.Equal(2, Program.Main([]));
    }

    private sealed class Fixture
    {
        public RecordingFiles Files { get; } = new();
        public WitWorld World { get; } = new(
            0, "preview2", "netwasm:hosting-platform@1.0.0", [], []);
        public WitDocument Document { get; }
        public WitInterfaceCatalog Catalog { get; } = new(
            "netwasm:hosting-platform/preview2@1.0.0", "normalized",
            [new("wasi:cli/environment@0.2.11", [])]);
        public Preview2ShimIdentity Shim { get; } = new("shim", "version");
        public HostingPlatformCatalog Projected { get; }
        public RecordingDocuments Documents { get; }
        public RecordingCatalogs Catalogs { get; }
        public RecordingShims Shims { get; }
        public RecordingProjector Projector { get; }
        public RecordingWriter Writer { get; } = new();

        public Fixture()
        {
            Document = new([], [], [World], [], "normalized");
            Projected = new(1, Catalog.World, "hash", Shim,
                [new("wasi:cli/environment@0.2.11", NetWasmPlatformCapability.Environment, [])]);
            Documents = new(Document);
            Catalogs = new(Catalog);
            Shims = new(Shim);
            Projector = new(Projected);
            Files.Values["wit.json"] = "wit";
            Files.Values["shim.json"] = "shim";
        }

        public HostingPlatformCatalogApplication Create(
            ITextFileStore? files = default,
            IWitDocumentJsonReader? documents = default,
            IWitInterfaceCatalogBuilder? catalogs = default,
            IPreview2ShimIdentityReader? shims = default,
            IHostingPlatformCatalogProjector? projector = default,
            IHostingPlatformCatalogJavaScriptWriter? writer = default) => new(
                files ?? Files,
                documents ?? Documents,
                catalogs ?? Catalogs,
                shims ?? Shims,
                projector ?? Projector,
                writer ?? Writer);
    }

    private sealed class RecordingFiles : ITextFileStore
    {
        public Dictionary<string, string> Values { get; } = [];
        public Dictionary<string, string> Writes { get; } = [];
        public List<string> Reads { get; } = [];
        public Exception? Failure { get; set; }

        public string Read(string path)
        {
            if (Failure is not null) throw Failure;
            Reads.Add(path);
            return Values[path];
        }

        public void Write(string path, string content) => Writes.Add(path, content);
    }

    private sealed class RecordingDocuments(WitDocument result) : IWitDocumentJsonReader
    {
        public WitDocument Read(string normalizedJson) => result;
    }

    private sealed class RecordingCatalogs(WitInterfaceCatalog result) : IWitInterfaceCatalogBuilder
    {
        public WitDocument? Document { get; private set; }
        public WitWorld? World { get; private set; }

        public WitInterfaceCatalog Build(WitDocument document, WitWorld world)
        {
            Document = document;
            World = world;
            return result;
        }
    }

    private sealed class RecordingShims(Preview2ShimIdentity result) : IPreview2ShimIdentityReader
    {
        public Preview2ShimIdentity Read(string packageJson) => result;
    }

    private sealed class RecordingProjector(HostingPlatformCatalog result) :
        IHostingPlatformCatalogProjector
    {
        public WitInterfaceCatalog? Catalog { get; private set; }
        public Preview2ShimIdentity? Shim { get; private set; }

        public HostingPlatformCatalog Project(
            WitInterfaceCatalog catalog,
            Preview2ShimIdentity shim)
        {
            Catalog = catalog;
            Shim = shim;
            return result;
        }
    }

    private sealed class RecordingWriter : IHostingPlatformCatalogJavaScriptWriter
    {
        public HostingPlatformCatalog? Catalog { get; private set; }

        public string Write(HostingPlatformCatalog catalog)
        {
            Catalog = catalog;
            return "generated";
        }
    }
}
