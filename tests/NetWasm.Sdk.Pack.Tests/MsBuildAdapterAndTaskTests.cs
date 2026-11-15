// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class MsBuildAdapterAndTaskTests
{
    [Fact]
    public void AdapterMapsStandardTaskMetadataToImmutableInputs()
    {
        var file = PackTestFixtures.FileItem("/tmp/a.dll", "lib/NetWasm,Version=v0.1/A.dll");
        file.SetMetadata("TargetFrameworkAlias", "netwasm0.1");
        file.SetMetadata("PackKind", "BuildOutput");
        var dependency = PackTestFixtures.DependencyItem("Dependency", "[1.0.0]");
        dependency.SetMetadata("PrivateAssets", "none");
        var inputs = new MsBuildPackInputAdapter().Adapt(
            "Sample",
            "0.1.0",
            "NetWasm",
            "sample",
            "/tmp/sample.nupkg",
            [file],
            [dependency],
            CanonicalPackPlanBuilder.CanonicalTargetFramework);
        Assert.Equal("/tmp/a.dll", inputs.Files[0].SourcePath);
        Assert.Equal(PackageFileKind.BuildOutput, inputs.Files[0].Kind);
        Assert.Equal("Dependency", inputs.Dependencies[0].Id);
        Assert.Equal("netwasm0.1", inputs.Files[0].TargetFrameworkAlias);
        Assert.Throws<ArgumentNullException>(() => new MsBuildPackInputAdapter().Adapt("a", "1.0.0", "a", "a", "/tmp/a", null!, [], "target"));
        Assert.Throws<ArgumentNullException>(() => new MsBuildPackInputAdapter().Adapt("a", "1.0.0", "a", "a", "/tmp/a", [], null!, "target"));
    }

    [Fact]
    public void AdapterUsesItemSpecFallbacksAndRejectsNullItems()
    {
        var item = new TaskItem("fallback");
        item.SetMetadata("PackKind", "unknown");
        item.SetMetadata("ExpectedLength", "-1");
        item.SetMetadata("DevelopmentDependency", "not-a-bool");
        var inputs = new MsBuildPackInputAdapter().Adapt("id", "1.0.0", "authors", "description", "/tmp/output.nupkg", [item], [item], "target");
        Assert.EndsWith("fallback", inputs.Files.Single().SourcePath, StringComparison.Ordinal);
        Assert.Equal(PackageFileKind.Other, inputs.Files.Single().Kind);
        Assert.Null(inputs.Files.Single().ExpectedLength);
        Assert.EndsWith("fallback", inputs.Dependencies.Single().Id, StringComparison.Ordinal);
        Assert.False(inputs.Dependencies.Single().IsDevelopmentDependency);
        Assert.Throws<ArgumentNullException>(() => new MsBuildPackInputAdapter().Adapt("id", "1.0.0", "a", "d", "/tmp/output.nupkg", [null!], [], "target"));
        item.SetMetadata("ExpectedLength", "3");
        Assert.Equal(3, new MsBuildPackInputAdapter().Adapt("id", "1.0.0", "a", "d", "/tmp/output.nupkg", [item], [], "target").Files.Single().ExpectedLength);
    }

    [Fact]
    public void TaskExecutesEachInjectedCapability()
    {
        var adapter = new RecordingAdapter();
        var command = new RecordingBuilder();
        var desktop = new TaskItem("net10.0");
        desktop.SetMetadata("Alias", "net10.0");
        desktop.SetMetadata("Identifier", ".NETCoreApp");
        desktop.SetMetadata("Version", "v10.0");
        desktop.SetMetadata("AssetFolder", "net10.0");
        var file = PackTestFixtures.FileItem("/tmp/a.dll", "lib/NetWasm,Version=v0.1/A.dll");
        file.SetMetadata("PackKind", nameof(PackageFileKind.BuildOutput));
        var content = PackTestFixtures.FileItem("/tmp/content.txt", "content/content.txt");
        content.SetMetadata("PackKind", nameof(PackageFileKind.Content));
        var projectReference = new TaskItem("Project.Reference");
        projectReference.SetMetadata("Version", "[1.0.0]");
        projectReference.SetMetadata("TargetFramework", CanonicalPackPlanBuilder.CanonicalTargetFramework);
        var task = new CanonicalPackMsBuildTask(adapter, new RecordingProjectReferenceAdapter(), command, new RecordingDiagnosticWriter())
        {
            BuildEngine = new PackTestFixtures.RecordingBuildEngine(),
            PackageId = "Sample",
            PackageVersion = "0.1.0",
            Authors = "NetWasm",
            Description = "sample",
            PackageOutputPath = "/tmp/sample.nupkg",
            PackageFiles = [file, content],
            CanonicalTargetFramework = CanonicalPackPlanBuilder.CanonicalTargetFramework,
            SymbolFiles = [PackTestFixtures.FileItem("/tmp/a.pdb", "lib/NetWasm,Version=v0.1/A.pdb")],
            SourceFiles = [PackTestFixtures.FileItem("/tmp/a.cs", "src/a.cs")],
            IncludeSymbols = true,
            IncludeSource = true,
            TargetProfiles = [desktop],
            ProjectReferencesWithVersions = [projectReference],
            FrameworksWithSuppressedDependencies = [SuppressedFrameworkByMetadata("net10.0")],
            NuspecOutputDirectory = "/tmp/obj",
            ProjectAssetsFile = "/tmp/project.assets.json",
            RepositoryBranch = "refs/heads/main",
            RepositoryCommit = "0123456789abcdef0123456789abcdef01234567",
            SourceDateEpoch = "1700000000"
        };
        Assert.True(task.Execute());
        Assert.True(command.Called);
        Assert.True(adapter.Called);
        Assert.Single(command.Inputs!.TargetIdentities);
        Assert.True(command.Inputs.TargetIdentities[0].SuppressDependencies);
        Assert.Single(command.Inputs.Targets);
        Assert.Equal("net10.0", command.Inputs.Targets[0].Alias);
        Assert.Single(command.Inputs.Dependencies);
        Assert.Equal("Project.Reference", command.Inputs.Dependencies[0].Id);
        Assert.Equal(PackageDependencyOrigin.ProjectReference, command.Inputs.Dependencies[0].Origin);
        Assert.Equal("/tmp/project.assets.json", command.Inputs.Restore.AssetsFilePath);
        Assert.Equal("/tmp/obj/Sample.nuspec", command.Inputs.NuspecOutputPath);
        Assert.Equal("refs/heads/main", command.Inputs.Metadata.RepositoryBranch);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", command.Inputs.Metadata.RepositoryCommit);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), command.Inputs.Determinism.EntryTimestamp);
    }

    private static TaskItem SuppressedFrameworkByMetadata(string alias)
    {
        var item = new TaskItem("different-item-spec");
        item.SetMetadata("Alias", alias);
        return item;
    }

    [Fact]
    public void TaskReturnsFalseAndSanitizedStableErrorOnFailure()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var task = new CanonicalPackMsBuildTask(new RecordingAdapter(), new RecordingProjectReferenceAdapter(), new ThrowingBuilder(), new FilePackDiagnosticWriter())
        {
            BuildEngine = new PackTestFixtures.RecordingBuildEngine(),
            DiagnosticOutputPath = Path.Combine(directory.Path, "diagnostic.json")
        };
        Assert.False(task.Execute());
        var diagnostic = File.ReadAllText(task.DiagnosticOutputPath!);
        Assert.Contains("NWPK014", diagnostic, StringComparison.Ordinal);
        Assert.Contains("The SDK-owned pack operation failed.", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain(ThrowingBuilder.UnsafeDetail, diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskSanitizesPackExceptionAndParsesOptionalLengths()
    {
        var engine = new PackTestFixtures.RecordingBuildEngine();
        var symbol = PackTestFixtures.FileItem("/tmp/a.pdb", "lib/a.pdb");
        symbol.SetMetadata("ExpectedLength", "1");
        var source = PackTestFixtures.FileItem("/tmp/a.cs", "src/a.cs");
        source.SetMetadata("ExpectedLength", "1");
        var task = new CanonicalPackMsBuildTask(new RecordingAdapter(), new RecordingProjectReferenceAdapter(), new ThrowingPackBuilder(), new RecordingDiagnosticWriter())
        {
            BuildEngine = engine,
            PackageId = "Sample",
            PackageVersion = "1.0.0",
            Authors = "authors",
            Description = "description",
            PackageOutputPath = "/tmp/sample.nupkg",
            CanonicalTargetFramework = CanonicalPackPlanBuilder.CanonicalTargetFramework,
            SymbolFiles = [symbol],
            SourceFiles = [source],
            IncludeSymbols = true,
            IncludeSource = true
        };
        Assert.False(task.Execute());
        Assert.Contains(engine.Errors, message => message.Contains("NWPK006", StringComparison.Ordinal));
    }

    [Fact]
    public void TaskRejectsMalformedOrOutOfRangeSourceDateEpoch()
    {
        foreach (var epoch in new[] { "not-a-number", "9223372036854775807" })
        {
            var engine = new PackTestFixtures.RecordingBuildEngine();
            var task = new CanonicalPackMsBuildTask(new RecordingAdapter(), new RecordingProjectReferenceAdapter(), new RecordingBuilder(), new RecordingDiagnosticWriter())
            {
                BuildEngine = engine,
                PackageId = "Sample",
                PackageVersion = "1.0.0",
                Authors = "authors",
                Description = "description",
                PackageOutputPath = "/tmp/sample.nupkg",
                CanonicalTargetFramework = CanonicalPackPlanBuilder.CanonicalTargetFramework,
                SourceDateEpoch = epoch
            };

            Assert.False(task.Execute());
            Assert.Contains(engine.Errors, message => message.Contains("NWPK013", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TaskAdaptsOptionalOutputsFiltersFilesAndUsesFallbackTargetMetadata()
    {
        var buildOutput = PackTestFixtures.FileItem("/tmp/build.dll", "lib/build.dll");
        buildOutput.SetMetadata("PackKind", nameof(PackageFileKind.BuildOutput));
        var content = PackTestFixtures.FileItem("/tmp/content.txt", "content/content.txt");
        content.SetMetadata("PackKind", nameof(PackageFileKind.Content));
        var fallbackTarget = new TaskItem("desktop");
        fallbackTarget.SetMetadata("DependencyGroup", "desktop");
        var adapter = new RecordingAdapter();
        var command = new RecordingBuilder();
        var task = new CanonicalPackMsBuildTask(adapter, new RecordingProjectReferenceAdapter(), command, new RecordingDiagnosticWriter())
        {
            BuildEngine = new PackTestFixtures.RecordingBuildEngine(),
            PackageId = "Sample",
            PackageVersion = "0.1.0",
            Authors = "authors",
            Description = "description",
            PackageOutputPath = "/tmp/sample.nupkg",
            CanonicalTargetFramework = CanonicalPackPlanBuilder.CanonicalTargetFramework,
            PackageFiles = [buildOutput, content],
            IncludeBuildOutput = false,
            IncludeContentInPack = false,
            TargetProfiles = [fallbackTarget],
            RestoreTargetKeys = [new TaskItem("desktop")],
            FrameworksWithSuppressedDependencies = [new TaskItem("desktop")],
            RestoreNuGetVersion = "7.6.0",
            RestoreSdkVersion = "10.0.0"
        };

        Assert.True(task.Execute());
        Assert.NotNull(command.Inputs);
        Assert.Equal(0, adapter.FileCount);
        Assert.Equal("desktop", command.Inputs.Restore.TargetKeys.Single());
        Assert.Equal("desktop", command.Inputs.TargetIdentities.Single().Alias);
        Assert.True(command.Inputs.TargetIdentities.Single().SuppressDependencies);
    }

    [Fact]
    public void DefaultTaskComposesProductionCapabilitiesAndRejectsNulls()
    {
        Assert.NotNull(new CanonicalPackMsBuildTask());
        Assert.Throws<ArgumentNullException>(() => new CanonicalPackMsBuildTask(null!, new RecordingProjectReferenceAdapter(), new RecordingBuilder(), new RecordingDiagnosticWriter()));
        Assert.Throws<ArgumentNullException>(() => new CanonicalPackMsBuildTask(new RecordingAdapter(), null!, new RecordingBuilder(), new RecordingDiagnosticWriter()));
        Assert.Throws<ArgumentNullException>(() => new CanonicalPackMsBuildTask(new RecordingAdapter(), new RecordingProjectReferenceAdapter(), null!, new RecordingDiagnosticWriter()));
        Assert.Throws<ArgumentNullException>(() => new CanonicalPackMsBuildTask(new RecordingAdapter(), new RecordingProjectReferenceAdapter(), new RecordingBuilder(), null!));
    }

    [Fact]
    public void DiagnosticWriterNeverThrowsForOmittedOrUnwritablePaths()
    {
        var writer = new FilePackDiagnosticWriter();
        writer.Write(null, NetWasmPackErrorCode.NWPK014, "safe");
        using var directory = new PackTestFixtures.TemporaryDirectory();
        writer.Write(directory.Path, NetWasmPackErrorCode.NWPK014, "safe");
        writer.Write("\0", NetWasmPackErrorCode.NWPK014, "safe");
    }

    private sealed class RecordingAdapter : IMsBuildPackInputAdapter
    {
        public bool Called { get; private set; }
        public int FileCount { get; private set; }

        public CanonicalPackInputs Adapt(string id, string version, string authors, string description, string outputPath, IReadOnlyList<ITaskItem> files, IReadOnlyList<ITaskItem> dependencies, string canonicalTargetFramework)
        {
            Called = true;
            FileCount = files.Count;
            return PackTestFixtures.Inputs(outputPath) with
            {
                Dependencies = dependencies.Select(item => new CanonicalPackageDependencyInput(
                    item.ItemSpec,
                    item.GetMetadata("Version"),
                    item.GetMetadata("TargetFramework"))).ToArray()
            };
        }
    }

    private sealed class RecordingBuilder : IPackageBuilder
    {
        public bool Called { get; private set; }
        public CanonicalPackInputs? Inputs { get; private set; }

        public CanonicalPackResult BuildPackage(CanonicalPackInputs inputs)
        {
            Called = true;
            Inputs = inputs;
            return new CanonicalPackResult(
                new PackageOutput(inputs.OutputPath, "hash", new PackageIdentity("Sample", "1.0.0")),
                null,
                new CanonicalPackManifest("1", "1", "request", [], "hash", null));
        }
    }

    private sealed class ThrowingBuilder : IPackageBuilder
    {
        // An unexpected failure's message can name local paths, so the diagnostic must not repeat it.
        public const string UnsafeDetail = "unsafe detail naming a local source path";

        public CanonicalPackResult BuildPackage(CanonicalPackInputs inputs) => throw new InvalidOperationException(UnsafeDetail);
    }

    private sealed class ThrowingPackBuilder : IPackageBuilder
    {
        public CanonicalPackResult BuildPackage(CanonicalPackInputs inputs) =>
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "safe failure");
    }

    private sealed class RecordingDiagnosticWriter : IPackDiagnosticWriter
    {
        public void Write(string? outputPath, NetWasmPackErrorCode code, string message) { }
    }

    private sealed class RecordingProjectReferenceAdapter : IProjectReferenceAdapter
    {
        public CanonicalPackageDependencyInput? Adapt(ITaskItem item, string canonicalTargetFramework) =>
            new(item.GetMetadata("PackageId") is { Length: > 0 } id ? id : item.ItemSpec, item.GetMetadata("Version"), canonicalTargetFramework)
            {
                Origin = PackageDependencyOrigin.ProjectReference
            };
    }
}
