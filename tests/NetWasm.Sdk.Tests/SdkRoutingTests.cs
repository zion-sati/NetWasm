using System.Diagnostics;
using System.Xml.Linq;

namespace NetWasm.Sdk.Tests;

public sealed class SdkRoutingTests
{
    private static string RepositoryRoot()
    {
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "eng/NetWasm.ReleaseVersion.txt")))
            root = Directory.GetParent(root)?.FullName ?? throw new DirectoryNotFoundException();
        return root;
    }

    [Fact]
    public async Task TaskAssemblyOutputFollowsAnExternalSdkBuildRoot()
    {
        var repositoryRoot = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(repositoryRoot, "src/NetWasm.Sdk/NetWasm.Sdk.csproj")))
        {
            var parent = Directory.GetParent(repositoryRoot)?.FullName;
            Assert.NotNull(parent);
            repositoryRoot = parent!;
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("msbuild");
        process.StartInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "src/NetWasm.Sdk/NetWasm.Sdk.csproj"));
        process.StartInfo.ArgumentList.Add("/getProperty:NetWasmSdkPackTaskOutput");
        process.StartInfo.ArgumentList.Add("/p:BaseOutputPath=/tmp/netwasm-sdk-external");
        process.StartInfo.ArgumentList.Add("/p:Configuration=Release");
        process.StartInfo.ArgumentList.Add("/nologo");
        process.StartInfo.ArgumentList.Add("/v:minimal");

        Assert.True(process.Start());
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = (await outputTask).Trim();
        _ = await errorTask;

        Assert.Equal(0, process.ExitCode);
        Assert.Equal("/tmp/netwasm-sdk-external/Release/net10.0/NetWasm.Sdk.Pack.dll", output);
    }

    [Fact]
    public void SingleNetWasmTargetMapsTheCanonicalIdentityAndRoutesPack()
    {
        using var project = EvaluationProject.Create("<TargetFramework>netwasm0.1</TargetFramework>");

        Assert.Equal("NetWasm", project.Property("TargetFrameworkIdentifier"));
        Assert.Equal("v0.1", project.Property("TargetFrameworkVersion"));
        Assert.Equal("NetWasm,Version=v0.1", project.Property("TargetFrameworkMoniker"));
        Assert.Equal("NetWasm,Version=v0.1", project.Property("NuGetTargetMoniker"));
        Assert.Equal("true", project.Property("NetWasmSdkCustomProfileEnabled"));
        Assert.EndsWith("Sdk/NuGetBuildTasksPackTargets.targets", project.Property("NuGetBuildTasksPackTargets"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GenerateNuspec", project.Property("PackDependsOn"));
        Assert.Equal("true", project.Property("NetWasmSdkCustomGenerateNuspecOverride"));
        Assert.Equal("true", project.Property("IsPackable"));
        Assert.Equal("true", project.Property("DisableStandardFrameworkResolution"));
        var releaseVersion = File.ReadAllText(Path.Combine(RepositoryRoot(), "eng/NetWasm.ReleaseVersion.txt")).Trim();
        Assert.Equal(releaseVersion, project.Property("NetWasmSdkPackageVersion"));
        Assert.Equal("false", project.Property("CopyBuildOutputToPublishDirectory"));
        Assert.Equal("false", project.Property("CopyOutputSymbolsToPublishDirectory"));
        Assert.Equal(["custom"], project.Items("NetWasmSdkPackRoute"));
    }

    [Fact]
    public void OuterDualTargetBuildRoutesOnceFromTheDeclaredTargetList()
    {
        using var project = EvaluationProject.Create("<TargetFrameworks>netwasm0.1;net10.0</TargetFrameworks>");

        var profile = project.Item("NetWasmSdkProfile");
        Assert.True(profile.Length == 1, string.Join(",", profile));
        Assert.Equal("netwasm0.1", profile[0].Split('|')[0]);
        Assert.Contains("NetWasm,Version=v0.1", profile[0]);
        Assert.Equal("true", project.Property("NetWasmSdkCustomProfileEnabled"));
        Assert.Equal(["custom"], project.Items("NetWasmSdkPackRoute"));
        Assert.EndsWith("Sdk/NuGetBuildTasksPackTargets.targets", project.Property("NuGetBuildTasksPackTargets"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GenerateNuspec", project.Property("PackDependsOn"));
        Assert.Equal("true", project.Property("NetWasmSdkCustomGenerateNuspecOverride"));
        Assert.Equal("true", project.Property("IsPackable"));
    }

    [Fact]
    public void DesktopInnerBuildRetainsStockPackTargetWhenSiblingNetWasmTargetIsDeclared()
    {
        using var project = EvaluationProject.Create(
            "<TargetFrameworks>netwasm0.1;net10.0</TargetFrameworks>",
            "net10.0");

        Assert.Empty(project.Items("NetWasmSdkProfile"));
        Assert.Equal(string.Empty, project.Property("NetWasmSdkCustomProfileEnabled"));
        Assert.EndsWith("NuGet.Build.Tasks.Pack.targets", project.Property("NuGetBuildTasksPackTargets"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, project.Property("NetWasmSdkCustomGenerateNuspecOverride"));
    }

    [Fact]
    public void NetWasmInnerBuildMapsTheCanonicalIdentityIndependentlyOfSiblingTargets()
    {
        using var project = EvaluationProject.Create(
            "<TargetFrameworks>netwasm0.1;net10.0</TargetFrameworks>",
            "netwasm0.1");

        Assert.Single(project.Items("NetWasmSdkProfile"));
        Assert.Equal("NetWasm", project.Property("TargetFrameworkIdentifier"));
        Assert.Equal("v0.1", project.Property("TargetFrameworkVersion"));
        Assert.Equal("NetWasm,Version=v0.1", project.Property("NuGetTargetMoniker"));
        Assert.Equal("true", project.Property("NetWasmSdkCustomProfileEnabled"));
        Assert.Equal("true", project.Property("DisableStandardFrameworkResolution"));
    }

    [Fact]
    public void NetWasmReferenceContractUsesPackageAssetsWithoutManualAssemblyPaths()
    {
        var repositoryRoot = FindRepositoryRoot();
        var tfmProps = File.ReadAllText(Path.Combine(repositoryRoot, "src/NetWasm.Sdk/Sdk/NetWasm.Sdk.Tfm.props"));

        Assert.Contains("<PropertyGroup Condition=\"'$(TargetFramework)' == 'netwasm0.1'\">", tfmProps, StringComparison.Ordinal);
        Assert.Contains("<DisableStandardFrameworkResolution>true</DisableStandardFrameworkResolution>", tfmProps, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"$(NetWasmRefPackageId)\"", tfmProps, StringComparison.Ordinal);
        Assert.Contains("IncludeAssets=\"compile\"", tfmProps, StringComparison.Ordinal);
        Assert.Contains("PrivateAssets=\"all\"", tfmProps, StringComparison.Ordinal);
        Assert.DoesNotContain("NetWasmRefPackageFolder", tfmProps, StringComparison.Ordinal);
        Assert.DoesNotContain("NetWasmRefAssemblyPath", tfmProps, StringComparison.Ordinal);
        Assert.DoesNotContain("NetWasmRefAssemblyDirectory", tfmProps, StringComparison.Ordinal);
        Assert.DoesNotContain("_TargetFrameworkDirectories", tfmProps, StringComparison.Ordinal);
        Assert.DoesNotContain("_FullFrameworkReferenceAssemblyPaths", tfmProps, StringComparison.Ordinal);
        Assert.DoesNotContain("<HintPath>", tfmProps, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceContractValidationConsumesResolvedPackageCompileAssets()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sdkTargets = File.ReadAllText(Path.Combine(repositoryRoot, "src/NetWasm.Sdk/Sdk/Sdk.targets"));

        Assert.Contains("DependsOnTargets=\"ResolvePackageAssets\"", sdkTargets, StringComparison.Ordinal);
        Assert.Contains("@(ResolvedCompileFileDefinitions)", sdkTargets, StringComparison.Ordinal);
        Assert.Contains("NuGetPackageId", sdkTargets, StringComparison.Ordinal);
        Assert.Contains("/ref/NetWasm,Version=v0.1/NetWasm.CoreLib.dll", sdkTargets, StringComparison.Ordinal);
        Assert.Contains("Code=\"NWSDK001\"", sdkTargets, StringComparison.Ordinal);
        Assert.DoesNotContain("NetWasmRefAssemblyPath", sdkTargets, StringComparison.Ordinal);
        Assert.DoesNotContain("<HintPath>", sdkTargets, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceContractValidationRejectsMissingOrWrongResolvedCompileAsset()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sdkTargets = File.ReadAllText(Path.Combine(repositoryRoot, "src/NetWasm.Sdk/Sdk/Sdk.targets"));

        Assert.Contains("'@(_NetWasmSdkResolvedReferenceContract)' == ''", sdkTargets, StringComparison.Ordinal);
        Assert.Contains("'$(NetWasmRefPackageId)'", sdkTargets, StringComparison.Ordinal);
        Assert.Contains("EndsWith('/ref/NetWasm,Version=v0.1/NetWasm.CoreLib.dll')", sdkTargets, StringComparison.Ordinal);
    }

    [Fact]
    public void RawWasmModeSkipsComponentizationWhileRetainingTheSdkRawBuildPipeline()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sdkTargets = File.ReadAllText(Path.Combine(repositoryRoot, "src/NetWasm.Sdk/Sdk/Sdk.targets"));
        var toolchainTargets = File.ReadAllText(Path.Combine(repositoryRoot, "src/NetWasm.Sdk/Sdk/NetWasm.Toolchain.targets"));

        Assert.Contains(
            "<NetWasmRawWasm Condition=\"'$(NetWasmRawWasm)' == ''\">false</NetWasmRawWasm>",
            sdkTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "Condition=\"'$(NetWasmRawWasm)' != 'true' AND '$(NetWasmComponentizeDependsOn)' == ''\"",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "<Target Name=\"NetWasmSdkBuildComponentDeployment\"",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "DependsOnTargets=\"$(NetWasmComponentizeDependsOn)\"",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "<Target Name=\"NetWasmSdkBuildRawDeployment\"",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "DependsOnTargets=\"$(NetWasmRawBuildDependsOn)\"",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "DependsOnTargets=\"NetWasmSdkResolveBuildEnvironment;NetWasmSdkBuildComponentDeployment;NetWasmSdkBuildRawDeployment\"",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "'$(NetWasmRawWasm)' == 'true'\">$([System.IO.Path]::Combine('$(NetWasmHostingJavaScriptRoot)', 'launcher-raw.mjs'))",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "'launcher-component.mjs'",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "'browser-raw-bootstrap.mjs'",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "'browser-component-bootstrap.mjs'",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DependsOnTargets=\"NetWasmSdkResolveBuildEnvironment;$(NetWasmComponentizeDependsOn);$(NetWasmRawBuildDependsOn)\"",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "<_NetWasmDeploymentRequiredImportModule Include=\"@(_NetWasmRequiredImportModule)\"",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "<NetWasmComponentWitVariant Include=\"$(NetWasmToolchainAsyncCommandWitPackagePath)\"",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "<ActivationInterfacePrefix>wasi:http@0.2.11/</ActivationInterfacePrefix>",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "@(_NetWasmRequiredImport->'%(Interface)')",
            toolchainTargets,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LocalExecutionDefaultsFollowOrdinaryDotNetExpectations()
    {
        var repositoryRoot = FindRepositoryRoot();
        var toolchainTargets = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src/NetWasm.Sdk/Sdk/NetWasm.Toolchain.targets"));
        var compilerTargets = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src/NetWasm.Compiler.Tasks/buildTransitive/NetWasm.Compiler.Tasks.targets"));

        Assert.Contains(
            "<NetWasmNetworkPolicy Condition=\"'$(NetWasmNetworkPolicy)' == ''\">allowAll</NetWasmNetworkPolicy>",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "<NetWasmRandomness Condition=\"'$(NetWasmRandomness)' == ''\">true</NetWasmRandomness>",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "<NetWasmClockGrant Include=\"wall;monotonic\"",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "<RunWorkingDirectory Condition=\"'$(OutputType)' == 'Exe' AND '$(RunWorkingDirectory)' == ''\">$(MSBuildProjectDirectory)</RunWorkingDirectory>",
            toolchainTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "<NetWasmManagedStackTrace Condition=\"'$(NetWasmManagedStackTrace)' == '' AND '$(Configuration)' == 'Debug'\">true</NetWasmManagedStackTrace>",
            compilerTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "<NetWasmManagedStackTrace Condition=\"'$(NetWasmManagedStackTrace)' == ''\">false</NetWasmManagedStackTrace>",
            compilerTargets,
            StringComparison.Ordinal);

        using var project = EvaluationProject.Create("""
            <TargetFramework>netwasm0.1</TargetFramework>
            <OutputType>Exe</OutputType>
            """);
        Assert.Equal("allowAll", project.Property("NetWasmNetworkPolicy"));
        Assert.Equal("true", project.Property("NetWasmRandomness"));
        Assert.Equal(project.ProjectDirectory, project.Property("RunWorkingDirectory"));
    }

    [Fact]
    public void ExplicitLocalHostPolicyOverridesRemainAvailable()
    {
        using var project = EvaluationProject.Create("""
            <TargetFramework>netwasm0.1</TargetFramework>
            <OutputType>Exe</OutputType>
            <NetWasmNetworkPolicy>denyAll</NetWasmNetworkPolicy>
            <NetWasmRandomness>false</NetWasmRandomness>
            <RunWorkingDirectory>/deployment/work</RunWorkingDirectory>
            """);

        Assert.Equal("denyAll", project.Property("NetWasmNetworkPolicy"));
        Assert.Equal("false", project.Property("NetWasmRandomness"));
        Assert.Equal("/deployment/work", project.Property("RunWorkingDirectory"));
    }

    [Fact]
    public void ManagedStackTraceDefaultsFollowConfigurationAndAllowExplicitOverrides()
    {
        using var debug = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework>",
            configuration: "Debug",
            includeCompilerTargets: true);
        using var release = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework>",
            includeCompilerTargets: true);
        using var debugOptOut = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework><NetWasmManagedStackTrace>false</NetWasmManagedStackTrace>",
            configuration: "Debug",
            includeCompilerTargets: true);
        using var releaseOptIn = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework><NetWasmManagedStackTrace>true</NetWasmManagedStackTrace>",
            includeCompilerTargets: true);

        Assert.Equal("true", debug.Property("NetWasmManagedStackTrace"));
        Assert.Equal("false", release.Property("NetWasmManagedStackTrace"));
        Assert.Equal("false", debugOptOut.Property("NetWasmManagedStackTrace"));
        Assert.Equal("true", releaseOptIn.Property("NetWasmManagedStackTrace"));
    }

    [Fact]
    public void FinalWasmOptimizationDefaultsToSizeAndAllowsExplicitNone()
    {
        using var defaultPolicy = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework>",
            includeCompilerTargets: true);
        using var noFinalOptimization = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework><NetWasmOptimization>None</NetWasmOptimization>",
            includeCompilerTargets: true);

        Assert.Equal("Size", defaultPolicy.Property("NetWasmOptimization"));
        Assert.Equal("None", noFinalOptimization.Property("NetWasmOptimization"));
    }

    [Fact]
    public void PackagingIncrementalBoundaryTracksEveryExternalToolAndOptionIdentity()
    {
        var targetNamespace = XNamespace.Get(
            "http://schemas.microsoft.com/developer/msbuild/2003");
        var targets = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src/NetWasm.Compiler.Tasks/buildTransitive/NetWasm.Compiler.Tasks.targets"));

        var identity = Assert.Single(targets.Descendants(
            targetNamespace + "Target"), target =>
            (string?)target.Attribute("Name") == "NetWasmWritePackagingIdentity");
        var identityContract = identity.ToString(SaveOptions.DisableFormatting);
        foreach (var value in new[]
                 {
                     "$(NetWasmOptimization)",
                     "packagingPolicyRevision=2",
                     "$(NetWasmTarget)",
                     "$(NetWasmWorld)",
                     "$(NetWasmWitPath)",
                     "$(NetWasmJcoVersion)",
                     "$(NetWasmPreview2ShimVersion)",
                     "$(NetWasmNodePath)",
                     "$(NetWasmWasmToolsCommandPath)",
                     "$(NetWasmWasmToolsModulePath)",
                     "$(NetWasmBinaryenWasmOptPath)",
                     "$(NetWasmBinaryenWasmMergePath)",
                     "%(NetWasmComponentRuntime.FullPath)",
                     "%(NetWasmComponentWitVariant.FullPath)",
                 })
        {
            Assert.Contains(value, identityContract, StringComparison.Ordinal);
        }

        foreach (var targetName in new[]
                 {
                     "NetWasmCompilerTasksComponentize",
                     "NetWasmCompilerTasksLinkRawModule",
                 })
        {
            var packaging = Assert.Single(targets.Descendants(
                targetNamespace + "Target"), target =>
                (string?)target.Attribute("Name") == targetName);
            var inputs = (string?)packaging.Attribute("Inputs");
            Assert.Contains("$(NetWasmNodePath)", inputs, StringComparison.Ordinal);
            Assert.Contains("$(NetWasmWasmToolsCommandPath)", inputs, StringComparison.Ordinal);
            Assert.Contains("$(NetWasmWasmToolsModulePath)", inputs, StringComparison.Ordinal);
            Assert.Contains("$(NetWasmBinaryenWasmOptPath)", inputs, StringComparison.Ordinal);
            Assert.Contains("$(NetWasmBinaryenWasmMergePath)", inputs, StringComparison.Ordinal);
            Assert.Contains("$(NetWasmPackagingIdentityPath)", inputs, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void InnerCollectionReturnsTheEvaluatedPackageReferenceContract()
    {
        using var project = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework>",
            "netwasm0.1",
            includePackageReference: true);

        Assert.Contains(
            "Example.Package|netwasm0.1|NetWasm,Version=v0.1|[1.0.0]|compile|runtime|all",
            project.Items("NetWasmSdkInnerPackageDependency"));
        Assert.Contains(
            $"NetWasm.Ref|netwasm0.1|NetWasm,Version=v0.1|{project.Property("NetWasmRefPackageVersion")}|compile|none|all",
            project.Items("NetWasmSdkInnerPackageDependency"));
    }

    [Fact]
    public void OuterPackEvidenceCollectsDirectReferencesFromEveryInnerTarget()
    {
        using var project = EvaluationProject.Create(
            "<TargetFrameworks>netwasm0.1;net10.0</TargetFrameworks>",
            includePackageReference: true,
            collectPackEvidence: true);

        Assert.Contains(
            "Example.Package|netwasm0.1|NetWasm,Version=v0.1|[1.0.0]|compile|runtime|all",
            project.Items("NetWasmSdkInnerPackageDependency"));
        Assert.Contains(
            $"NetWasm.Ref|netwasm0.1|NetWasm,Version=v0.1|{project.Property("NetWasmRefPackageVersion")}|compile|none|all",
            project.Items("NetWasmSdkInnerPackageDependency"));
        Assert.Contains(
            "Example.Package|net10.0|net10.0|[1.0.0]|compile|runtime|all",
            project.Items("NetWasmSdkInnerPackageDependency"));
    }

    [Fact]
    public void OuterPackEvidenceCollectsProjectReferencePackageIdentityAndPackMetadata()
    {
        using var project = EvaluationProject.Create(
            "<TargetFrameworks>netwasm0.1;net10.0</TargetFrameworks>",
            includeProjectReference: true,
            collectPackEvidence: true);

        Assert.Contains(
            "Example.Project|Referenced.csproj|netwasm0.1|NetWasm,Version=v0.1|2.0.0|compile|runtime|all",
            project.Items("NetWasmSdkInnerProjectDependency"));
        Assert.Contains(
            "Example.Project|Referenced.csproj|net10.0|net10.0|2.0.0|compile|runtime|all",
            project.Items("NetWasmSdkInnerProjectDependency"));
    }

    [Fact]
    public void StockSdkProjectReferencePreservesUnresolvedIdentityEvidence()
    {
        using var project = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework>",
            includeStockProjectReference: true,
            collectPackEvidence: true);

        Assert.Contains(
            "unresolved|Referenced.csproj|netwasm0.1|netwasm0.1|NetWasm,Version=v0.1|Release|AnyCPU|test-rid|all",
            project.Items("NetWasmSdkInnerProjectDependencyResolution"));
    }

    [Fact]
    public void DesktopOnlyProjectRetainsStockPackTargetAndConstructionBoundary()
    {
        using var project = EvaluationProject.Create("<TargetFramework>net10.0</TargetFramework>");

        Assert.Empty(project.Items("NetWasmSdkProfile"));
        Assert.Equal(string.Empty, project.Property("NetWasmSdkCustomProfileEnabled"));
        Assert.EndsWith("NuGet.Build.Tasks.Pack.targets", project.Property("NuGetBuildTasksPackTargets"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GenerateNuspec", project.Property("PackDependsOn"));
        Assert.Equal(string.Empty, project.Property("NetWasmSdkCustomGenerateNuspecOverride"));
        Assert.Equal("true", project.Property("IsPackable"));
    }

    [Theory]
    [InlineData("<TargetFramework>netwasm0.10</TargetFramework>")]
    [InlineData("<TargetFramework>netwasm0.1-preview</TargetFramework>")]
    [InlineData("<TargetFramework>xnetwasm0.1</TargetFramework>")]
    [InlineData("<TargetFramework>netwasm0.1x</TargetFramework>")]
    [InlineData("<TargetFramework> netwasm0.1 </TargetFramework>")]
    [InlineData("<TargetFramework>NETWASM0.1</TargetFramework>")]
    [InlineData("<TargetFrameworks>net10.0;netwasm0.10</TargetFrameworks>")]
    public void SimilarAliasesDoNotActivateTheCustomProfile(string targetFrameworkProperty)
    {
        using var project = EvaluationProject.Create(targetFrameworkProperty);

        Assert.Empty(project.Items("NetWasmSdkProfile"));
        Assert.Equal(string.Empty, project.Property("NetWasmSdkCustomProfileEnabled"));
        Assert.EndsWith("NuGet.Build.Tasks.Pack.targets", project.Property("NuGetBuildTasksPackTargets"), StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src/NetWasm.Sdk/Sdk/Sdk.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The NetWasm repository root was not found.");
    }

    private sealed class EvaluationProject : IDisposable
    {
        private readonly string root;
        private readonly Dictionary<string, string> properties;
        private readonly Dictionary<string, string[]> items;

        private EvaluationProject(string root, Dictionary<string, string> properties, Dictionary<string, string[]> items)
        {
            this.root = root;
            this.properties = properties;
            this.items = items;
        }

        public static EvaluationProject Create(
            string targetFrameworkProperty,
            string? innerTargetFramework = null,
            bool includePackageReference = false,
            bool includeProjectReference = false,
            bool collectPackEvidence = false,
            bool includeStockProjectReference = false,
            string configuration = "Release",
            bool includeCompilerTargets = false)
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "netwasm-sdk-evaluation", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var repositoryRoot = FindRepositoryRoot();
            var sdkRoot = System.IO.Path.Combine(repositoryRoot, "src/NetWasm.Sdk/Sdk");
            var compilerTargetsPath = System.IO.Path.Combine(
                repositoryRoot,
                "src/NetWasm.Compiler.Tasks/buildTransitive/NetWasm.Compiler.Tasks.targets");
            var packageVersionsPath = System.IO.Path.Combine(repositoryRoot, "eng/NetWasm.PackageVersions.props");
            var projectPath = System.IO.Path.Combine(root, "Routing.csproj");
            if (includeProjectReference || includeStockProjectReference)
            {
                var referencedProject = includeStockProjectReference
                    ? """
                      <Project Sdk="Microsoft.NET.Sdk">
                        <PropertyGroup>
                          <TargetFramework>net10.0</TargetFramework>
                          <PackageId>Example.StockProject</PackageId>
                          <PackageVersion>2.0.0</PackageVersion>
                        </PropertyGroup>
                      </Project>
                      """
                    : $"""
                      <Project>
                        <Import Project="{Escape(packageVersionsPath)}" />
                        <Import Project="{Escape(sdkRoot)}/Sdk.props" />
                        <PropertyGroup>
                          <TargetFrameworks>netwasm0.1;net10.0</TargetFrameworks>
                          <PackageId>Example.Project</PackageId>
                          <PackageVersion>2.0.0</PackageVersion>
                        </PropertyGroup>
                        <Import Project="{Escape(sdkRoot)}/Sdk.targets" />
                      </Project>
                      """;
                File.WriteAllText(System.IO.Path.Combine(root, "Referenced.csproj"), referencedProject);
            }

            File.WriteAllText(
                projectPath,
                $"""
                <Project>
                  <Import Project="{Escape(packageVersionsPath)}" />
                  <Import Project="{Escape(sdkRoot)}/Sdk.props" />
                  <PropertyGroup>
                    {targetFrameworkProperty}
                    <Configuration>{configuration}</Configuration>
                    <Platform>AnyCPU</Platform>
                    <RuntimeIdentifier>test-rid</RuntimeIdentifier>
                  </PropertyGroup>
                  {(includePackageReference ? "<ItemGroup><PackageReference Include=\"Example.Package\" Version=\"[1.0.0]\" PrivateAssets=\"all\" IncludeAssets=\"compile\" ExcludeAssets=\"runtime\" /></ItemGroup>" : string.Empty)}
                  {(includeProjectReference || includeStockProjectReference ? "<ItemGroup><ProjectReference Include=\"Referenced.csproj\" PrivateAssets=\"all\" IncludeAssets=\"compile\" ExcludeAssets=\"runtime\" /></ItemGroup>" : string.Empty)}
                  <Import Project="{Escape(sdkRoot)}/Sdk.targets" />
                  {(includeCompilerTargets ? $"<Import Project=\"{Escape(compilerTargetsPath)}\" />" : string.Empty)}
                  <Target Name="PrintNetWasmRouting" DependsOnTargets="$(NetWasmSdkPackRouteProbeDependsOn);NetWasmSdkCollectInnerPackageReferences{(collectPackEvidence ? ";NetWasmCollectPackEvidence" : string.Empty)}">
                    <Message Importance="High" Text="__NETWASM_PROP__TargetFrameworkIdentifier=$(TargetFrameworkIdentifier)" />
                    <Message Importance="High" Text="__NETWASM_PROP__TargetFrameworkVersion=$(TargetFrameworkVersion)" />
                    <Message Importance="High" Text="__NETWASM_PROP__TargetFrameworkMoniker=$(TargetFrameworkMoniker)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NuGetTargetMoniker=$(NuGetTargetMoniker)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmSdkCustomProfileEnabled=$(NetWasmSdkCustomProfileEnabled)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NuGetBuildTasksPackTargets=$(NuGetBuildTasksPackTargets)" />
                    <Message Importance="High" Text="__NETWASM_PROP__PackDependsOn=$(PackDependsOn)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmSdkCustomGenerateNuspecOverride=$(NetWasmSdkCustomGenerateNuspecOverride)" />
                    <Message Importance="High" Text="__NETWASM_PROP__IsPackable=$(IsPackable)" />
                    <Message Importance="High" Text="__NETWASM_PROP__DisableStandardFrameworkResolution=$(DisableStandardFrameworkResolution)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmSdkPackageVersion=$(NetWasmSdkPackageVersion)" />
                    <Message Importance="High" Text="__NETWASM_PROP__CopyBuildOutputToPublishDirectory=$(CopyBuildOutputToPublishDirectory)" />
                    <Message Importance="High" Text="__NETWASM_PROP__CopyOutputSymbolsToPublishDirectory=$(CopyOutputSymbolsToPublishDirectory)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmNetworkPolicy=$(NetWasmNetworkPolicy)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmRandomness=$(NetWasmRandomness)" />
                    <Message Importance="High" Text="__NETWASM_PROP__RunWorkingDirectory=$(RunWorkingDirectory)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedStackTrace=$(NetWasmManagedStackTrace)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmOptimization=$(NetWasmOptimization)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmRefPackageVersion=$(NetWasmRefPackageVersion)" />
                    <Message Importance="High" Text="__NETWASM_ITEM__NetWasmSdkProfile=@(NetWasmSdkProfile->'%(Identity)|%(CanonicalFolder)')" />
                    <Message Importance="High" Text="__NETWASM_ITEM__NetWasmSdkPackRoute=@(NetWasmSdkPackRoute)" />
                    <Message Importance="High" Text="__NETWASM_ITEM__NetWasmSdkInnerPackageDependency=@(_NetWasmSdkInnerPackageDependency->'%(Identity)|%(TargetFrameworkAlias)|%(TargetFramework)|%(Version)|%(IncludeAssets)|%(ExcludeAssets)|%(PrivateAssets)')" />
                    <Message Importance="High" Text="__NETWASM_ITEM__NetWasmSdkInnerProjectDependency=@(_NetWasmSdkInnerProjectDependency->'%(Identity)|%(ProjectPath)|%(TargetFrameworkAlias)|%(TargetFramework)|%(Version)|%(IncludeAssets)|%(ExcludeAssets)|%(PrivateAssets)')" />
                    <Message Importance="High" Text="__NETWASM_ITEM__NetWasmSdkInnerProjectDependencyResolution=@(_NetWasmSdkInnerProjectDependency->'%(ProjectReferenceResolution)|%(ProjectPath)|%(ProjectReferenceTargetFramework)|%(TargetFrameworkAlias)|%(TargetFramework)|%(Configuration)|%(Platform)|%(RuntimeIdentifier)|%(PrivateAssets)')" />
                  </Target>
                </Project>
                """);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = repositoryRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("msbuild");
            process.StartInfo.ArgumentList.Add(projectPath);
            process.StartInfo.ArgumentList.Add("/t:PrintNetWasmRouting");
            process.StartInfo.ArgumentList.Add("/nologo");
            process.StartInfo.ArgumentList.Add("/v:minimal");
            process.StartInfo.ArgumentList.Add("/m:1");
            process.StartInfo.ArgumentList.Add("/nr:false");
            if (innerTargetFramework is not null)
            {
                process.StartInfo.ArgumentList.Add($"/p:TargetFramework={innerTargetFramework}");
            }
            else if (targetFrameworkProperty.Contains("TargetFrameworks", StringComparison.Ordinal))
            {
                process.StartInfo.ArgumentList.Add("/p:TargetFramework=");
            }
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();
            Assert.True(
                process.ExitCode == 0,
                $"MSBuild routing evaluation failed.\n{output}\n{error}");

            var parsedProperties = new Dictionary<string, string>(StringComparer.Ordinal);
            var parsedItems = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(static line => line.TrimStart()))
            {
                if (line.StartsWith("__NETWASM_PROP__", StringComparison.Ordinal))
                {
                    var separator = line.IndexOf('=', "__NETWASM_PROP__".Length);
                    if (separator >= 0)
                    {
                        parsedProperties[line["__NETWASM_PROP__".Length..separator]] = line[(separator + 1)..];
                    }
                }
                else if (line.StartsWith("__NETWASM_ITEM__", StringComparison.Ordinal))
                {
                    var separator = line.IndexOf('=', "__NETWASM_ITEM__".Length);
                    if (separator >= 0)
                    {
                        var value = line[(separator + 1)..];
                        parsedItems[line["__NETWASM_ITEM__".Length..separator]] = string.IsNullOrEmpty(value)
                            ? []
                            : value.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    }
                }
            }

            return new EvaluationProject(root, parsedProperties, parsedItems);
        }

        public string Property(string name) => properties.GetValueOrDefault(name, string.Empty);

        public string ProjectDirectory => root;

        public string[] Items(string name) => items.GetValueOrDefault(name, []);

        public string[] Item(string name) => Items(name);

        public void Dispose()
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Keep all diagnostic state local if an SDK process still has a
                // temporary import open; no repository artifact is retained.
            }
        }

        private static string Escape(string value) => value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

    }
}
