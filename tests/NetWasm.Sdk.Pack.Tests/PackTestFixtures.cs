using System.Collections.Immutable;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace NetWasm.Sdk.Pack.Tests;

internal static class PackTestFixtures
{
    public static CanonicalPackPlanBuilder Builder()
    {
        var restoreValidator = new RestoreEvidenceValidator();
        var fingerprintBuilder = new PackRequestFingerprintBuilder(new InMemoryPackageFileReader(new Dictionary<string, byte[]>
        {
            ["sample.dll"] = [1],
            ["README.md"] = [2],
            ["LICENSE.txt"] = [3],
            ["icon.svg"] = [4]
        }));
        return new CanonicalPackPlanBuilder(
            new ProfileRegistry(),
            new DependencyPolicy(),
            new PackagePathValidator(),
            new PackageIdentityValidator(),
            new MetadataPolicy(),
            restoreValidator,
            new FileRestoreEvidenceReader(restoreValidator),
            new PackCacheValidator(fingerprintBuilder),
            new TargetIdentityPolicy(),
            new LocalLinkTargetResolver());
    }

    public static CanonicalPackInputs Inputs(
        string? outputPath = null,
        string? target = null,
        IReadOnlyList<CanonicalPackageFileInput>? files = null,
        IReadOnlyList<CanonicalPackageDependencyInput>? dependencies = null) =>
        new(
            "NetWasm.Sample",
            "0.2.0",
            "NetWasm",
            "sample package",
            outputPath ?? Path.Combine(Path.GetTempPath(), "sample.nupkg"),
            files ?? [new CanonicalPackageFileInput("sample.dll", "lib/NetWasm,Version=v0.1/Sample.dll")],
            dependencies ?? [new CanonicalPackageDependencyInput("Dependency", "[1.0.0]", target ?? CanonicalPackPlanBuilder.CanonicalTargetFramework)],
            target ?? CanonicalPackPlanBuilder.CanonicalTargetFramework);

    public static CanonicalPackage Package(string outputPath, ImmutableArray<CanonicalPackageFile>? files = null) =>
        new(
            "NetWasm.Sample",
            "0.2.0",
            "NetWasm",
            "sample package",
            outputPath,
            files ?? [new CanonicalPackageFile("sample.dll", "lib/NetWasm,Version=v0.1/Sample.dll")],
            [new CanonicalPackageDependencyGroup(
                CanonicalPackPlanBuilder.CanonicalTargetFramework,
                [new CanonicalPackageDependency("Dependency", "[1.0.0]")])]);

    public static TaskItem FileItem(string source, string target)
    {
        var item = new TaskItem(source);
        item.SetMetadata("SourcePath", source);
        item.SetMetadata("PackagePath", target);
        return item;
    }

    public static TaskItem DependencyItem(string id, string version, string framework = CanonicalPackPlanBuilder.CanonicalTargetFramework)
    {
        var item = new TaskItem(id);
        item.SetMetadata("Version", version);
        item.SetMetadata("TargetFramework", framework);
        return item;
    }

    public sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"netwasm-pack-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    public sealed class RecordingBuildEngine : IBuildEngine
    {
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "test.proj";
        public List<string> Errors { get; } = [];

        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? string.Empty);
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => true;
    }
}
