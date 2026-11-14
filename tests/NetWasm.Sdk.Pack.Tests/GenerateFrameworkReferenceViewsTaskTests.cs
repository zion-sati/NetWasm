using Microsoft.Build.Utilities;
using Mono.Cecil;
using NetWasm.Sdk.Pack.References;
using Xunit;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class GenerateFrameworkReferenceViewsTaskTests
{
    [Fact]
    public void PreservesOriginalAndMetadataWhileRegeneratingChangedViews()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var input = WriteAssembly(directory.Path, "Backport");
        var platform = WriteAssembly(directory.Path, "Platform");
        var original = File.ReadAllBytes(input);
        var item = new TaskItem(input);
        item.SetMetadata("NuGetPackageId", "Backport");
        item.SetMetadata("Aliases", "global");
        var builder = new RecordingBuilder();
        var task = new GenerateFrameworkReferenceViewsTask(new Selecting(), builder)
        {
            References = [item],
            PlatformPath = platform,
            OutputDirectory = Path.Combine(directory.Path, "views"),
            BuildEngine = new PackTestFixtures.RecordingBuildEngine()
        };
        Assert.True(task.Execute());
        var view = Assert.Single(task.Views);
        Assert.Equal(input, view.GetMetadata("NetWasmOriginalReference"));
        Assert.Equal("true", view.GetMetadata("NetWasmMetadataReference"));
        Assert.Equal("Backport", view.GetMetadata("NuGetPackageId"));
        Assert.Equal("global", view.GetMetadata("Aliases"));
        Assert.Equal(original, builder.Request!.Implementation);
        Assert.Equal(File.ReadAllBytes(platform), builder.Request.Platform);
        Assert.Equal(original, File.ReadAllBytes(input));
        File.SetLastWriteTimeUtc(view.ItemSpec, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var timestamp = File.GetLastWriteTimeUtc(view.ItemSpec);
        Assert.True(task.Execute());
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(view.ItemSpec));
        builder.Image = [2];
        Assert.True(task.Execute());
        Assert.Equal(view.ItemSpec, Assert.Single(task.Views).ItemSpec);
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(view.ItemSpec));
        Assert.Equal(original, File.ReadAllBytes(input));
    }

    [Fact]
    public void UnselectedAndNonPackageReferencesProduceNoView()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var input = WriteAssembly(directory.Path, "Unrelated");
        var item = new TaskItem(input);
        item.SetMetadata("NuGetPackageId", "Unrelated");
        var builder = new RecordingBuilder();
        var task = new GenerateFrameworkReferenceViewsTask(new FrameworkReferenceTypeSelector(), builder)
        {
            References = [item, new TaskItem(Path.Combine(directory.Path, "not-read.dll"))],
            PlatformPath = input,
            OutputDirectory = Path.Combine(directory.Path, "views"),
            BuildEngine = new PackTestFixtures.RecordingBuildEngine()
        };
        Assert.True(task.Execute());
        Assert.Empty(task.Views);
        Assert.Null(builder.Request);
        Assert.False(Directory.Exists(task.OutputDirectory));
    }

    [Fact]
    public void FailedInvocationClearsPriorOutputItems()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var input = WriteAssembly(directory.Path, "Backport");
        var item = new TaskItem(input);
        item.SetMetadata("NuGetPackageId", "Backport");
        var task = new GenerateFrameworkReferenceViewsTask(new Selecting(), new RecordingBuilder())
        {
            References = [item],
            PlatformPath = input,
            OutputDirectory = Path.Combine(directory.Path, "views"),
            BuildEngine = new PackTestFixtures.RecordingBuildEngine()
        };
        Assert.True(task.Execute());
        Assert.Single(task.Views);
        task.PlatformPath = Path.Combine(directory.Path, "missing.dll");
        Assert.False(task.Execute());
        Assert.Empty(task.Views);
    }

    private static string WriteAssembly(string directory, string name)
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new(name, new Version(1, 0)), name, ModuleKind.Dll);
        var path = Path.Combine(directory, name + ".dll");
        assembly.Write(path);
        return path;
    }

    private sealed class Selecting : IFrameworkReferenceTypeSelector
    {
        public IReadOnlyList<string> SelectTypes(FrameworkReferenceSelection request) => ["System.Shared"];
    }

    private sealed class RecordingBuilder : IFrameworkReferenceViewBuilder
    {
        public FrameworkReferenceViewRequest? Request { get; private set; }
        public byte[] Image { get; set; } = [1];
        public byte[] Build(FrameworkReferenceViewRequest request)
        {
            Request = request;
            return Image;
        }
    }
}
