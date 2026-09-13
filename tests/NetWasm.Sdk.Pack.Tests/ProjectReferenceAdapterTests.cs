using Microsoft.Build.Utilities;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class ProjectReferenceAdapterTests
{
    [Fact]
    public void AdapterRequiresAnIdentityResolver()
    {
        Assert.Throws<ArgumentNullException>(() => new ProjectReferenceAdapter(null!));
    }

    [Fact]
    public void AdapterUsesEvaluatedPackageMetadataInsteadOfProjectPath()
    {
        var item = new TaskItem("../Desktop/Desktop.csproj");
        item.SetMetadata("PackageId", "Desktop.Library");
        item.SetMetadata("ProjectVersion", "2.1.0");
        item.SetMetadata("TargetFramework", "net10.0");
        item.SetMetadata("PrivateAssets", "all");

        var dependency = new ProjectReferenceAdapter(new FixedIdentityResolver()).Adapt(item, CanonicalPackPlanBuilder.CanonicalTargetFramework);

        Assert.NotNull(dependency);
        Assert.Equal("Desktop.Library", dependency!.Id);
        Assert.Equal("2.1.0", dependency.VersionRange);
        Assert.Equal("net10.0", dependency.TargetFramework);
        Assert.Equal("all", dependency.PrivateAssets);
        Assert.Equal(PackageDependencyOrigin.ProjectReference, dependency.Origin);
    }

    [Fact]
    public void AdapterConsumesAnAuthoritativeResolvedIdentityRecord()
    {
        var item = new TaskItem("Desktop.Library");
        item.SetMetadata("ProjectReferenceResolution", "resolved");
        item.SetMetadata("ProjectPath", "/projects/Desktop/Desktop.csproj");
        item.SetMetadata("Version", "2.1.0");
        item.SetMetadata("TargetFramework", "net10.0");

        var dependency = new ProjectReferenceAdapter(new FixedIdentityResolver(returnIdentity: false)).Adapt(item, CanonicalPackPlanBuilder.CanonicalTargetFramework);

        Assert.NotNull(dependency);
        Assert.Equal("Desktop.Library", dependency!.Id);
        Assert.Equal(PackageDependencyOrigin.ProjectReference, dependency.Origin);
    }

    [Fact]
    public void AdapterPreservesExplicitRangeAndFloatingVersionSyntax()
    {
        var item = new TaskItem("Project.Reference");
        item.SetMetadata("PackageId", "Project.Reference");
        item.SetMetadata("Version", "1.0.0");
        item.SetMetadata("VersionRange", "(,2.0.0]");

        var range = new ProjectReferenceAdapter(new FixedIdentityResolver()).Adapt(item, CanonicalPackPlanBuilder.CanonicalTargetFramework);
        Assert.Equal("(,2.0.0]", range!.VersionRange);

        item.SetMetadata("VersionRange", "1.*");
        var floating = new ProjectReferenceAdapter(new FixedIdentityResolver()).Adapt(item, CanonicalPackPlanBuilder.CanonicalTargetFramework);
        Assert.Equal("1.*", floating!.VersionRange);
    }

    [Fact]
    public void AdapterHardFailsWhenStockReferenceHasNoEvaluatedIdentity()
    {
        var item = new TaskItem("../Desktop/Desktop.csproj");
        item.SetMetadata("ProjectVersion", "2.1.0");

        Assert.Equal(
            NetWasmPackErrorCode.NWPK015,
            Assert.Throws<NetWasmPackException>(() => new ProjectReferenceAdapter(new FixedIdentityResolver(returnIdentity: false)).Adapt(item, CanonicalPackPlanBuilder.CanonicalTargetFramework)).Code);
    }

    [Fact]
    public void AdapterResolvesMixedSdkReferenceFromAuthoritativeEvaluation()
    {
        var item = new TaskItem("../Desktop/Desktop.csproj");
        item.SetMetadata("TargetFramework", "net10.0");
        item.SetMetadata("ProjectPath", "../Desktop/Authoritative.csproj");
        item.SetMetadata("Configuration", "Release");
        item.SetMetadata("Platform", "AnyCPU");
        item.SetMetadata("RuntimeIdentifier", "browser-wasm");
        var resolver = new RecordingIdentityResolver();

        var dependency = new ProjectReferenceAdapter(resolver).Adapt(item, CanonicalPackPlanBuilder.CanonicalTargetFramework);

        Assert.NotNull(dependency);
        Assert.Equal("Resolved.Desktop", dependency!.Id);
        Assert.EndsWith("Desktop/Authoritative.csproj", resolver.ProjectPath, StringComparison.Ordinal);
        Assert.Equal("net10.0", resolver.Request?.TargetFramework);
        Assert.Equal("Release", resolver.Request?.Configuration);
        Assert.Equal("AnyCPU", resolver.Request?.Platform);
        Assert.Equal("browser-wasm", resolver.Request?.RuntimeIdentifier);
    }

    [Fact]
    public void AdapterSkipsAuthoritativelyNonPackableReference()
    {
        var item = new TaskItem("../Desktop/Desktop.csproj");
        var resolver = new FixedIdentityResolver(returnIdentity: true, packable: false);

        Assert.Null(new ProjectReferenceAdapter(resolver).Adapt(item, CanonicalPackPlanBuilder.CanonicalTargetFramework));
    }

    [Fact]
    public void AdapterSkipsReferencesExplicitlyExcludedFromPacking()
    {
        var item = new TaskItem("../Desktop/Desktop.csproj");
        item.SetMetadata("Pack", "false");

        Assert.Null(new ProjectReferenceAdapter(new FixedIdentityResolver()).Adapt(item, CanonicalPackPlanBuilder.CanonicalTargetFramework));
    }

    [Fact]
    public void AdapterRejectsMissingVersionAndPathLikeResolvedIdentity()
    {
        var missingVersion = new TaskItem("Project.Reference");
        missingVersion.SetMetadata("PackageId", "Resolved.Project");
        Assert.Equal(NetWasmPackErrorCode.NWPK015, Assert.Throws<NetWasmPackException>(() =>
            new ProjectReferenceAdapter(new FixedIdentityResolver()).Adapt(missingVersion, CanonicalPackPlanBuilder.CanonicalTargetFramework)).Code);

        var pathIdentity = new TaskItem("../Project.Reference.csproj");
        pathIdentity.SetMetadata("PackageId", "../bad");
        pathIdentity.SetMetadata("Version", "1.0.0");
        Assert.Equal(NetWasmPackErrorCode.NWPK015, Assert.Throws<NetWasmPackException>(() =>
            new ProjectReferenceAdapter(new FixedIdentityResolver(returnIdentity: false)).Adapt(pathIdentity, CanonicalPackPlanBuilder.CanonicalTargetFramework)).Code);
        var csprojIdentity = new TaskItem("Project.csproj");
        csprojIdentity.SetMetadata("PackageId", "../bad");
        csprojIdentity.SetMetadata("Version", "1.0.0");
        Assert.Equal(NetWasmPackErrorCode.NWPK015, Assert.Throws<NetWasmPackException>(() =>
            new ProjectReferenceAdapter(new FixedIdentityResolver()).Adapt(csprojIdentity, CanonicalPackPlanBuilder.CanonicalTargetFramework)).Code);
        var evaluatedCsproj = new TaskItem("Project.csproj");
        evaluatedCsproj.SetMetadata("ProjectReferenceResolution", "resolved");
        evaluatedCsproj.SetMetadata("ProjectPath", "Project.csproj");
        evaluatedCsproj.SetMetadata("Version", "1.0.0");
        Assert.NotNull(new ProjectReferenceAdapter(new FixedIdentityResolver()).Adapt(evaluatedCsproj, CanonicalPackPlanBuilder.CanonicalTargetFramework));
        var evaluatedFsproj = new TaskItem("Project.fsproj");
        evaluatedFsproj.SetMetadata("ProjectReferenceResolution", "resolved");
        evaluatedFsproj.SetMetadata("ProjectPath", "Project.fsproj");
        evaluatedFsproj.SetMetadata("Version", "1.0.0");
        Assert.NotNull(new ProjectReferenceAdapter(new FixedIdentityResolver()).Adapt(evaluatedFsproj, CanonicalPackPlanBuilder.CanonicalTargetFramework));
    }

    [Fact]
    public void MsBuildResolverReadsTheSelectedProjectEvaluation()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var projectPath = Path.Combine(directory.Path, "Desktop.csproj");
        File.WriteAllText(projectPath, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><PackageId>Desktop.Evaluated</PackageId><PackageVersion>2.0.0</PackageVersion><IsPackable>true</IsPackable></PropertyGroup></Project>");

        var identity = new MsBuildProjectPackageIdentityResolver().Resolve(projectPath, new ProjectEvaluationRequest("net10.0", "Release", "AnyCPU", "browser-wasm"));

        Assert.NotNull(identity);
        Assert.Equal("Desktop.Evaluated", identity!.PackageId);
        Assert.Equal("2.0.0", identity.PackageVersion);
        Assert.True(identity.IsPackable);
    }

    [Fact]
    public void MsBuildResolverReturnsNullForMissingEvidence()
    {
        var resolver = new MsBuildProjectPackageIdentityResolver();
        Assert.Null(resolver.Resolve(string.Empty, new ProjectEvaluationRequest("net10.0")));
        Assert.Null(resolver.Resolve("/tmp/no-such-netwasm-project.csproj", new ProjectEvaluationRequest("net10.0")));
    }

    [Fact]
    public void MsBuildResolverRejectsMalformedProjectsAndAllowsOmittedGlobals()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var projectPath = Path.Combine(directory.Path, "Malformed.csproj");
        File.WriteAllText(projectPath, "<Project><PropertyGroup>");
        Assert.Equal(NetWasmPackErrorCode.NWPK015, Assert.Throws<NetWasmPackException>(() =>
            new MsBuildProjectPackageIdentityResolver().Resolve(projectPath, new ProjectEvaluationRequest("net10.0"))).Code);
        Assert.Throws<ArgumentNullException>(() => new MsBuildProjectPackageIdentityResolver().Resolve(projectPath, null!));

        var missingVersion = Path.Combine(directory.Path, "MissingVersion.csproj");
        File.WriteAllText(missingVersion, "<Project><PropertyGroup><PackageId>Missing.Version</PackageId></PropertyGroup></Project>");
        Assert.Null(new MsBuildProjectPackageIdentityResolver().Resolve(missingVersion, new ProjectEvaluationRequest("net10.0")));
    }

    private sealed class FixedIdentityResolver(bool returnIdentity = true, bool packable = true) : IProjectPackageIdentityResolver
    {
        public ProjectPackageIdentity? Resolve(string projectPath, ProjectEvaluationRequest request) =>
            returnIdentity ? new ProjectPackageIdentity("Resolved.Desktop", "2.1.0", packable) : null;
    }

    private sealed class RecordingIdentityResolver : IProjectPackageIdentityResolver
    {
        public string? ProjectPath { get; private set; }
        public ProjectEvaluationRequest? Request { get; private set; }

        public ProjectPackageIdentity Resolve(string projectPath, ProjectEvaluationRequest request)
        {
            ProjectPath = projectPath;
            Request = request;
            return new ProjectPackageIdentity("Resolved.Desktop", "2.1.0");
        }
    }
}
