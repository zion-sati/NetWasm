using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Compiler.Tasks.Framework;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.Tests.Framework;

public sealed class FrameworkAssemblySelectionTests
{
    // The program reaches System.Linq, which reaches System.Collections; System.Xml is never referenced.
    [Fact]
    public void SelectsTheFrameworkAssembliesTheProgramReachesTransitively()
    {
        var metadata = new MapReader(new()
        {
            ["/app/App.dll"] = (new(1, 0), ["NetWasm.CoreLib", "System.Linq"]),
            ["/fw/System.Linq.dll"] = (new(11, 0, 0, 0), ["NetWasm.CoreLib", "System.Collections"]),
            ["/fw/System.Collections.dll"] = (new(11, 0, 0, 0), ["NetWasm.CoreLib", "System.Linq"]),
        });
        var selection = new FrameworkAssemblySelector(metadata).Select(
            ["/app/App.dll"],
            ["/fw/System.Xml.dll", "/fw/System.Linq.dll", "/fw/System.Collections.dll"], []);

        Assert.Equal(["/fw/System.Collections.dll", "/fw/System.Linq.dll"], selection.Framework);
        Assert.Empty(selection.Displaced);
        Assert.DoesNotContain("/fw/System.Xml.dll", metadata.Read);
    }

    // A package's copy of a framework assembly at or below the framework's version gives way to it, and the
    // walk continues from the framework's copy; a newer package copy stays.
    [Theory]
    [InlineData(8, true)]
    [InlineData(11, true)]
    [InlineData(12, false)]
    public void TheHigherVersionWinsAndATieGoesToTheFramework(int packageMajor, bool frameworkWins)
    {
        var metadata = new MapReader(new()
        {
            ["/app/App.dll"] = (new(1, 0), ["System.Text.Json"]),
            ["/pkg/System.Text.Json.dll"] = (new(packageMajor, 0, 0, 0), ["System.Memory"]),
            ["/fw/System.Text.Json.dll"] = (new(11, 0, 0, 0), ["System.Text.Encodings.Web"]),
            ["/fw/System.Text.Encodings.Web.dll"] = (new(11, 0, 0, 0), []),
            ["/fw/System.Memory.dll"] = (new(11, 0, 0, 0), []),
        });
        var selection = new FrameworkAssemblySelector(metadata).Select(
            ["/app/App.dll", "/pkg/System.Text.Json.dll"],
            ["/fw/System.Text.Json.dll", "/fw/System.Text.Encodings.Web.dll", "/fw/System.Memory.dll"], []);

        Assert.Equal(frameworkWins
            ? ["/fw/System.Text.Encodings.Web.dll", "/fw/System.Text.Json.dll"]
            : ["/fw/System.Memory.dll"], selection.Framework);
        Assert.Equal(frameworkWins ? ["/pkg/System.Text.Json.dll"] : [], selection.Displaced);
    }

    // A program file without metadata keeps its place: it has no version to compare and references nothing.
    [Fact]
    public void AProgramFileWithoutMetadataGivesWayToTheFrameworksAssembly()
    {
        var metadata = new MapReader(new()
        {
            ["/fw/System.Linq.dll"] = (new(11, 0, 0, 0), []),
        });
        var selection = new FrameworkAssemblySelector(metadata).Select(
            ["/pkg/System.Linq.dll", "/app/native.dll", "/pkg/Other.dll"], ["/fw/System.Linq.dll", "/fw/Other.dll"], []);

        // An unreadable framework copy (Other) cannot replace the program's.
        Assert.Equal(["/fw/System.Linq.dll"], selection.Framework);
        Assert.Equal(["/pkg/System.Linq.dll"], selection.Displaced);
    }

    // A package built for netstandard references netstandard only; the walk continues through the facade's
    // references, so the owners it forwards to reach the compiler. The facade itself is not selected.
    [Fact]
    public void AReferenceToTheFacadeReachesTheAssembliesItForwardsTo()
    {
        var metadata = new MapReader(new()
        {
            ["/app/App.dll"] = (new(1, 0), ["NetWasm.CoreLib", "Package"]),
            ["/pkg/Package.dll"] = (new(1, 0), ["netstandard"]),
            ["/ref/netstandard.dll"] = (new(2, 1, 0, 0), ["NetWasm.CoreLib", "System.Text.RegularExpressions", "System.Xml.ReaderWriter"]),
            ["/fw/System.Text.RegularExpressions.dll"] = (new(11, 0, 0, 0), []),
            ["/fw/System.Xml.ReaderWriter.dll"] = (new(11, 0, 0, 0), []),
        });
        var selection = new FrameworkAssemblySelector(metadata).Select(
            ["/app/App.dll", "/pkg/Package.dll"],
            ["/fw/System.Text.RegularExpressions.dll", "/fw/System.Xml.ReaderWriter.dll", "/fw/System.Linq.dll"],
            ["/ref/netstandard.dll", "/ref/netstandard.dll"]);

        Assert.Equal(["/fw/System.Text.RegularExpressions.dll", "/fw/System.Xml.ReaderWriter.dll"], selection.Framework);
        Assert.Equal(1, metadata.Read.Count(path => path == "/ref/netstandard.dll"));
        Assert.Empty(new FrameworkAssemblySelector(metadata).Select(["/pkg/Package.dll"], ["/fw/System.Xml.ReaderWriter.dll"], []).Framework);
    }

    [Fact]
    public void RejectsDuplicateFrameworkNamesAndMissingInputs()
    {
        var selector = new FrameworkAssemblySelector(new MapReader([]));

        Assert.Throws<ArgumentException>(() => selector.Select([], ["/a/System.Linq.dll", "/b/System.Linq.dll"], []));
        Assert.Throws<ArgumentNullException>(() => selector.Select(null!, [], []));
        Assert.Throws<ArgumentNullException>(() => selector.Select([], null!, []));
        Assert.Throws<ArgumentNullException>(() => selector.Select([], [], null!));
        Assert.Throws<ArgumentNullException>(() => new FrameworkAssemblySelector(null!));
    }

    [Fact]
    public void ReadsAnAssemblysVersionAndReferencesAndNothingFromAnImageWithoutMetadata()
    {
        var reader = new AssemblyMetadataReader();
        var assembly = typeof(FrameworkAssemblySelector).Assembly;
        var summary = reader.Read(assembly.Location);
        var directory = Directory.CreateTempSubdirectory("netwasm-framework-selection");
        try
        {
            var native = Path.Combine(directory.FullName, "native.dll");
            File.WriteAllBytes(native, NativeImage());

            Assert.NotNull(summary);
            Assert.Equal(assembly.GetName().Version, summary.Version);
            Assert.Contains("System.Reflection.Metadata", summary.References);
            Assert.Null(reader.Read(native));
            Assert.Throws<ArgumentException>(() => reader.Read(" "));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void TaskReturnsTheSelectedFrameworkItems()
    {
        var linq = new TaskItem("/fw/System.Linq.dll");
        linq.SetMetadata("Origin", "framework");
        var package = new TaskItem("/pkg/System.Text.Json.dll");
        package.SetMetadata("NuGetPackageId", "System.Text.Json");
        var selector = new FixedSelector(new(["/fw/System.Linq.dll"], ["/pkg/System.Text.Json.dll"]));
        var task = new NetWasmSelectFrameworkAssembliesTask(selector)
        {
            BuildEngine = new RecordingBuildEngine(),
            ProgramAssemblies = [new TaskItem("/app/App.dll"), package, new TaskItem("/app/App.dll")],
            FrameworkAssemblies = [linq, new TaskItem("/fw/System.Xml.dll"), new TaskItem("/fw/System.Xml.dll")],
            Facades = [new TaskItem("/ref/netstandard.dll"), new TaskItem("/ref/netstandard.dll")],
        };

        Assert.True(task.Execute());
        var selected = Assert.Single(task.SelectedAssemblies);
        Assert.Equal("/fw/System.Linq.dll", selected.ItemSpec);
        Assert.Equal("framework", selected.GetMetadata("Origin"));
        Assert.Equal(["/ref/netstandard.dll"], selector.Facades);
        var displaced = Assert.Single(task.DisplacedAssemblies);
        Assert.Equal("System.Text.Json", displaced.GetMetadata("NuGetPackageId"));
    }

    [Fact]
    public void TaskReportsAnUnreadableAssembly()
    {
        var build = new RecordingBuildEngine();
        var task = new NetWasmSelectFrameworkAssembliesTask(new FixedSelector(null))
        {
            BuildEngine = build,
            ProgramAssemblies = [new TaskItem("/app/App.dll")],
        };

        Assert.False(task.Execute());
        Assert.Contains("NWSDK104", Assert.Single(build.Errors), StringComparison.Ordinal);
        Assert.Empty(task.SelectedAssemblies);
        Assert.Empty(task.DisplacedAssemblies);
        Assert.Throws<ArgumentNullException>(() => new NetWasmSelectFrameworkAssembliesTask(null!));
        Assert.NotNull(new NetWasmSelectFrameworkAssembliesTask());
    }

    // A PE image with one section and no CLI header, as a native library has.
    private static byte[] NativeImage()
    {
        var blob = new BlobBuilder();
        new NativePEBuilder().Serialize(blob);
        return blob.ToArray();
    }

    private sealed class NativePEBuilder() : PEBuilder(PEHeaderBuilder.CreateLibraryHeader(), deterministicIdProvider: null)
    {
        protected override ImmutableArray<Section> CreateSections() =>
            [new Section(".text", SectionCharacteristics.ContainsCode | SectionCharacteristics.MemRead | SectionCharacteristics.MemExecute)];

        protected override PEDirectoriesBuilder GetDirectories() => new();

        protected override BlobBuilder SerializeSection(string name, SectionLocation location)
        {
            var section = new BlobBuilder();
            section.WriteByte(0xC3);
            return section;
        }
    }

    private sealed class MapReader(Dictionary<string, (Version Version, string[] References)> assemblies) : IAssemblyMetadataReader
    {
        public List<string> Read { get; } = [];

        AssemblyMetadataSummary? IAssemblyMetadataReader.Read(string assemblyPath)
        {
            Read.Add(assemblyPath);
            return assemblies.TryGetValue(assemblyPath, out var assembly) ? new(assembly.Version, assembly.References) : null;
        }
    }

    // Returns its selection, or throws as an unreadable image does when given none.
    private sealed class FixedSelector(FrameworkAssemblySelection? selection) : IFrameworkAssemblySelector
    {
        public IReadOnlyList<string>? Facades { get; private set; }

        public FrameworkAssemblySelection Select(
            IReadOnlyList<string> programAssemblies, IReadOnlyList<string> frameworkAssemblies, IReadOnlyList<string> facades)
        {
            Facades = facades;
            return selection ?? throw new BadImageFormatException("not an assembly");
        }
    }

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<string> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? string.Empty);
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            System.Collections.IDictionary globalProperties,
            System.Collections.IDictionary targetOutputs) => true;
    }
}
