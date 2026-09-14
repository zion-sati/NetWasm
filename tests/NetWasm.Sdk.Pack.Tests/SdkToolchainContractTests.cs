using System.Xml.Linq;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class SdkToolchainContractTests
{
    [Fact]
    public void ToolchainTargetExposesThePinnedComponentLifecycleSeams()
    {
        var document = LoadSdkTarget("NetWasm.Toolchain.targets");
        var targetNames = document
            .Descendants(XName.Get("Target", "http://schemas.microsoft.com/developer/msbuild/2003"))
            .Select(target => (string?)target.Attribute("Name"))
            .Where(name => name is not null)
            .ToArray();
        var source = document.ToString(SaveOptions.DisableFormatting);

        Assert.Contains("NetWasmSdkComponentize", targetNames);
        Assert.Contains("NetWasmSdkPublishPortable", targetNames);
        Assert.Contains("NetWasmSdkPublishBrowser", targetNames);
        Assert.Contains("NetWasmSdkRejectLibraryLifecycle", targetNames);
        Assert.Contains("NetWasmComponentInput", source, StringComparison.Ordinal);
        Assert.Contains("NetWasmComponentRuntime", source, StringComparison.Ordinal);
        Assert.Contains("NetWasmComponentizeDependsOn", source, StringComparison.Ordinal);
        Assert.Contains("NetWasmWitPath", source, StringComparison.Ordinal);
        Assert.Contains("NetWasmCompilerWitPath", source, StringComparison.Ordinal);
        Assert.Contains("NetWasmCompilerWitWorld", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NetWasmWasmtimePath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NetWasmWasmToolsPath", source, StringComparison.Ordinal);
        Assert.Contains("NetWasmWasmToolsCommandPath", source, StringComparison.Ordinal);
        Assert.Contains("NetWasmWasmToolsModulePath", source, StringComparison.Ordinal);
        Assert.Contains("NetWasmBinaryenWasmOptPath", source, StringComparison.Ordinal);
        Assert.Contains("NetWasmBinaryenWasmMergePath", source, StringComparison.Ordinal);
        Assert.Contains("NetWasmJcoPath", source, StringComparison.Ordinal);
        Assert.Contains("NetWasmNodePath", source, StringComparison.Ordinal);
        Assert.Contains("$(NETWASM_NODE_PATH)", source, StringComparison.Ordinal);
        Assert.Contains("$(EMSDK_NODE)", source, StringComparison.Ordinal);
        Assert.Contains("NetWasmTranspileComponentTask", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolchainTargetDoesNotIntroduceAmbientToolDiscoveryOrDownloads()
    {
        var document = LoadSdkTarget("NetWasm.Toolchain.targets");
        var commands = document
            .Descendants(XName.Get("Exec", "http://schemas.microsoft.com/developer/msbuild/2003"))
            .Select(element => (string?)element.Attribute("Command"))
            .Where(command => command is not null)
            .ToArray();

        Assert.DoesNotContain(commands, command => command!.Contains("npx", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(commands, command => command!.Contains("npm install", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(commands, command => command!.Contains("curl ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(commands, command => command!.Contains("wget ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(commands, command => command!.Contains("PATH", StringComparison.Ordinal));
    }

    [Fact]
    public void RunCommandUsesTheValidatedNodeSelectionOrderAtEvaluationTime()
    {
        var document = LoadSdkTarget("NetWasm.Toolchain.targets");
        var commands = document
            .Descendants(XName.Get(
                "NetWasmRunNodeCommand",
                "http://schemas.microsoft.com/developer/msbuild/2003"))
            .Select(element => (
                Value: element.Value,
                Condition: (string?)element.Attribute("Condition")))
            .ToArray();

        Assert.Collection(
            commands,
            command =>
            {
                Assert.Equal("$(NetWasmNodePath)", command.Value);
                Assert.Contains("$(NetWasmNodePath)", command.Condition, StringComparison.Ordinal);
            },
            command =>
            {
                Assert.Equal("$(NETWASM_NODE_PATH)", command.Value);
                Assert.Contains("$(NETWASM_NODE_PATH)", command.Condition, StringComparison.Ordinal);
            },
            command =>
            {
                Assert.Equal("$(EMSDK_NODE)", command.Value);
                Assert.Contains("$(EMSDK_NODE)", command.Condition, StringComparison.Ordinal);
            },
            command => Assert.Equal("node", command.Value));
    }

    [Fact]
    public void SdkDiagnosticsUseUniqueCodesAcrossComposedTargets()
    {
        var repositoryRoot = FindRepositoryRoot();
        var targetFiles = new[]
        {
            FindSdkFile("Sdk.targets"),
            FindSdkFile("NetWasm.Toolchain.targets"),
            Path.Combine(
                repositoryRoot,
                "src/NetWasm.Compiler.Tasks/buildTransitive/NetWasm.Compiler.Tasks.targets"),
            Path.Combine(
                repositoryRoot,
                "src/NetWasm.Runtime.Pack/buildTransitive/NetWasm.Runtime.Pack.targets"),
        };
        var errorName = XName.Get(
            "Error",
            "http://schemas.microsoft.com/developer/msbuild/2003");
        var codes = targetFiles
            .SelectMany(file => XDocument.Load(file).Descendants(errorName))
            .Select(error => (string?)error.Attribute("Code"))
            .Where(code => code is not null)
            .ToArray();

        Assert.Contains("NWSDK037", codes);
        Assert.Contains("NWSDK038", codes);
        Assert.Contains("NWSDK039", codes);
        Assert.Contains("NWSDK040", codes);
        Assert.DoesNotContain(codes
            .GroupBy(code => code, StringComparer.Ordinal)
, group => group.Count() > 1);
    }

    [Fact]
    public void PublishRehydratesVerifiedCompilerArtifactState()
    {
        var targetNamespace = XNamespace.Get(
            "http://schemas.microsoft.com/developer/msbuild/2003");
        var compilerTargets = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src/NetWasm.Compiler.Tasks/buildTransitive/NetWasm.Compiler.Tasks.targets"));
        var compilation = Assert.Single(compilerTargets.Descendants(
            targetNamespace + "Target"), target =>
            (string?)target.Attribute("Name") == "NetWasmCompileCoreModule");
        var compilationReplacement = Assert.Single(compilation.Descendants(
            targetNamespace + "ItemGroup"), group =>
            group.Elements(targetNamespace + "NetWasmArtifact")
                .Any(item => item.Attribute("Remove") is not null));
        Assert.Contains("_NetWasmResolvedArtifact",
            (string?)compilationReplacement.Attribute("Condition"),
            StringComparison.Ordinal);

        var validation = Assert.Single(compilerTargets.Descendants(
            targetNamespace + "Target"), target =>
            (string?)target.Attribute("Name") == "NetWasmValidateCompilerArtifacts");

        Assert.DoesNotContain("NoBuild", (string?)validation.Attribute("Condition"),
            StringComparison.Ordinal);
        Assert.Contains("'$(NetWasmSemanticBuildId)' == ''",
            (string?)validation.Attribute("Condition"),
            StringComparison.Ordinal);
        Assert.Single(validation.Descendants(
            targetNamespace + "NetWasmValidateArtifactTask"));
        Assert.Contains("SemanticBuildId",
            validation.ToString(SaveOptions.DisableFormatting),
            StringComparison.Ordinal);
        Assert.Contains("_NetWasmValidationManagedLinkReference",
            validation.ToString(SaveOptions.DisableFormatting),
            StringComparison.Ordinal);
        Assert.DoesNotContain("References=\"@(_NetWasmManagedLinkReference)\"",
            validation.ToString(SaveOptions.DisableFormatting),
            StringComparison.Ordinal);
        Assert.Contains("_NetWasmValidatedArtifact",
            validation.ToString(SaveOptions.DisableFormatting),
            StringComparison.Ordinal);
        var validationReplacement = Assert.Single(validation.Descendants(
            targetNamespace + "ItemGroup"), group =>
            group.Elements(targetNamespace + "NetWasmArtifact")
                .Any(item => item.Attribute("Remove") is not null));
        Assert.Contains("_NetWasmValidatedArtifact",
            (string?)validationReplacement.Attribute("Condition"),
            StringComparison.Ordinal);

        var sdkTargets = LoadSdkTarget("NetWasm.Toolchain.targets");
        var preparation = Assert.Single(sdkTargets.Descendants(
            targetNamespace + "Target"), target =>
            (string?)target.Attribute("Name") == "NetWasmSdkPrepareLocalDeployment");
        Assert.Contains("NetWasmValidateCompilerArtifacts",
            (string?)preparation.Attribute("DependsOnTargets"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ToolchainTargetProvidesAnExplicitAsyncCommandContract()
    {
        var document = LoadSdkTarget("NetWasm.Toolchain.targets");
        var source = document.ToString(SaveOptions.DisableFormatting);

        Assert.Contains("NetWasmComponentContract", source, StringComparison.Ordinal);
        Assert.Contains("async-command", source, StringComparison.Ordinal);
        Assert.Contains("wit-packages", source, StringComparison.Ordinal);
        Assert.Contains("command.wit.wasm", source, StringComparison.Ordinal);
        Assert.Contains("async-command.wit.wasm", source, StringComparison.Ordinal);
        Assert.Contains("compiler.wit.wasm", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NetWasmToolchainWitRoot", source,
            StringComparison.Ordinal);
        Assert.Contains("netwasm:component@1.0.0/async-command", source, StringComparison.Ordinal);
        Assert.Contains("netwasm:component@1.0.0/async-http-command", source,
            StringComparison.Ordinal);
        Assert.Contains("wasi:http@0.2.11/", source, StringComparison.Ordinal);
        var compilerWorlds = document.Descendants(XName.Get("NetWasmCompilerWitWorld",
            "http://schemas.microsoft.com/developer/msbuild/2003")).ToArray();
        Assert.Equal("netwasm:platform@1.0.0/async-platform", compilerWorlds[0].Value);
        Assert.Contains("'$(NetWasmComponentContract)' == 'async-command'",
            (string?)compilerWorlds[0].Attribute("Condition"), StringComparison.Ordinal);
        Assert.Equal("netwasm:platform@1.0.0/platform", compilerWorlds[1].Value);
        Assert.Contains("NWSDK025", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncCommandWorldExportsProcessObservationAndRetainsReactorSurface()
    {
        var repositoryRoot = FindRepositoryRoot();
        var world = File.ReadAllText(
            Path.Combine(repositoryRoot, "src/NetWasm.Toolchain/async-wit/world.wit"));

        Assert.Contains("include wasi:cli/imports@0.2.11", world,
            StringComparison.Ordinal);
        Assert.DoesNotContain("include wasi:cli/command", world, StringComparison.Ordinal);
        Assert.Contains("export netwasm:runtime/process@1.0.0", world, StringComparison.Ordinal);
        Assert.Contains("world async-http-command", world, StringComparison.Ordinal);
        Assert.Contains("import wasi:http/types@0.2.11", world,
            StringComparison.Ordinal);
        Assert.Contains("import wasi:http/outgoing-handler@0.2.11", world,
            StringComparison.Ordinal);
        Assert.Contains("import netwasm:runtime/reactor-host@1.0.0", world,
            StringComparison.Ordinal);
        Assert.Contains("export netwasm:runtime/reactor-guest@1.0.0", world,
            StringComparison.Ordinal);
        var process = File.ReadAllText(Path.Combine(repositoryRoot,
            "wit/netwasm-runtime-1.0.0/process.wit"));
        Assert.Contains("start: func() -> u32;", process, StringComparison.Ordinal);
        Assert.Contains("status: func(operation: u32) -> u32;", process, StringComparison.Ordinal);
        Assert.Contains("exit-code: func(operation: u32) -> s32;", process, StringComparison.Ordinal);
        Assert.Contains("complete: func(operation: u32);", process, StringComparison.Ordinal);
    }

    [Fact]
    public void CompilerPlatformWorldsDescribeRandomnessWithoutASeparateCapabilityMode()
    {
        var repositoryRoot = FindRepositoryRoot();
        var world = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "wit/netwasm-platform-1.0.0/world.wit"));

        Assert.Equal(2, world.Split(
            "import wasi:random/random@0.2.11;",
            StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("cryptographic-platform", world,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TargetFrameworkContractSuppressesOnlyTheIncompatibleCharacterLookupAnalyzer()
    {
        var document = LoadSdkTarget("NetWasm.Sdk.Tfm.props");
        var source = document.ToString(SaveOptions.DisableFormatting);
        var analyzerConfiguration = File.ReadAllText(FindSdkFile("NetWasm.Sdk.Analyzers.globalconfig"));

        Assert.Contains("NetWasm.Sdk.Analyzers.globalconfig", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<NoWarn>", source, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.CA1847.severity = none", analyzerConfiguration, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.CA1865.severity = none", analyzerConfiguration, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.CA1866.severity = none", analyzerConfiguration, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.CA1867.severity = none", analyzerConfiguration, StringComparison.Ordinal);
    }

    [Fact]
    public void InfrastructurePackageTargetsHonorNoBuildWithoutInvokingBuildTargets()
    {
        var repositoryRoot = FindRepositoryRoot();
        var guardedTargets = new (string Path, string Target)[]
        {
            ("eng/NetWasm.DeterministicPackageArchive.targets",
                "ResolveNetWasmDeterministicPackageArchiveTask"),
            ("src/NetWasm.Compiler.Tasks/NetWasm.Compiler.Tasks.csproj",
                "ResolveNetWasmCompilerTasksPackTask"),
            ("src/NetWasm.Hosting/NetWasm.Hosting.csproj",
                "ResolveNetWasmHostingPackTask"),
            ("src/NetWasm.Hosting.Build/NetWasm.Hosting.Build.csproj",
                "ResolveNetWasmHostingBuildPackTask"),
            ("src/NetWasm.Templates/NetWasm.Templates.csproj",
                "ResolveNetWasmTemplatesPackTask"),
            ("src/NetWasm.Toolchain/NetWasm.Toolchain.csproj",
                "ResolveNetWasmToolchainPackTask"),
        };

        foreach (var (path, targetName) in guardedTargets)
        {
            var document = XDocument.Load(Path.Combine(repositoryRoot, path));
            var target = Assert.Single(document.Descendants("Target"), candidate =>
                (string?)candidate.Attribute("Name") == targetName);
            Assert.Contains("'$(NoBuild)' != 'true'",
                (string?)target.Attribute("Condition"), StringComparison.Ordinal);
        }

        var referenceProject = XDocument.Load(Path.Combine(
            repositoryRoot, "src/NetWasm.Ref/NetWasm.Ref.csproj"));
        var referenceBuilds = referenceProject.Descendants("MSBuild")
            .Where(task => (string?)task.Attribute("Targets") == "Build")
            .ToArray();
        Assert.Equal(2, referenceBuilds.Length);
        Assert.All(referenceBuilds, task => Assert.Contains(
            "'$(NoBuild)' != 'true'", (string?)task.Attribute("Condition"),
            StringComparison.Ordinal));

        foreach (var path in new[]
                 {
                     "Directory.Build.props",
                     "src/NetWasm.Sdk/Sdk/NetWasm.Sdk.Tfm.props",
                 })
        {
            var document = XDocument.Load(Path.Combine(repositoryRoot, path));
            var property = Assert.Single(document.Descendants(), element =>
                element.Name.LocalName == "BuildProjectReferences");
            Assert.Equal("false", property.Value);
            Assert.Contains("'$(NoBuild)' == 'true'",
                (string?)property.Attribute("Condition"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RepositoryBuildDefaultsCanonicalizeSourcePathsForProjectReferences()
    {
        var repositoryRoot = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Build.props"));
        var sourceRoot = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "NetWasmRepositorySourceRoot");
        var sourcePathMap = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "PathMap" &&
            element.Value == "$(NetWasmRepositorySourceRoot)=/_/");
        var packageCachePathMap = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "PathMap" &&
            element.Value == "$(PathMap),$(NetWasmPackageCacheRoot)=/_/nuget");

        Assert.Contains("NormalizeDirectory", sourceRoot.Value, StringComparison.Ordinal);
        Assert.Contains("$(MSBuildThisFileDirectory)", sourceRoot.Value, StringComparison.Ordinal);
        Assert.Equal("'$(PathMap)' == ''", (string?)sourcePathMap.Attribute("Condition"));
        Assert.Equal(
            "'$(NetWasmPackageCacheRoot)' != ''",
            (string?)packageCachePathMap.Attribute("Condition"));
    }

    [Fact]
    public void PackageBuildCanonicalizesTemporaryRootBeforeDerivingSourceRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "eng/build-packages.sh"));
        var temporaryRootIndex = script.IndexOf(
            "build_root=\"$(mktemp -d ",
            StringComparison.Ordinal);
        var canonicalRootIndex = script.IndexOf(
            "build_root=\"$(cd \"${build_root}\" && pwd -P)\"",
            StringComparison.Ordinal);
        var sourceRootIndex = script.IndexOf(
            "source_root=\"${build_root}/source\"",
            StringComparison.Ordinal);

        Assert.True(temporaryRootIndex >= 0);
        Assert.True(canonicalRootIndex > temporaryRootIndex);
        Assert.True(sourceRootIndex > canonicalRootIndex);
    }

    [Fact]
    public void PackageBuildRejectsRuntimePackDriftFromNativeSources()
    {
        var repositoryRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "eng/build-packages.sh"));
        var regenerationIndex = script.IndexOf(
            "tools/regenerate-runtime-pack.sh",
            StringComparison.Ordinal);
        var driftCheckIndex = script.IndexOf(
            "git -C \"${source_root}\" diff --quiet",
            StringComparison.Ordinal);
        var buildIndex = script.IndexOf(
            "dotnet build \"${source_root}/NetWasm.slnx\"",
            StringComparison.Ordinal);

        Assert.True(regenerationIndex >= 0);
        Assert.True(driftCheckIndex > regenerationIndex);
        Assert.True(buildIndex > driftCheckIndex);
    }

    private static XDocument LoadSdkTarget(string fileName)
        => XDocument.Load(FindSdkFile(fileName));

    private static string FindSdkFile(string fileName)
        => Path.Combine(
            FindRepositoryRoot(),
            "src",
            "NetWasm.Sdk",
            "Sdk",
            fileName);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "src/NetWasm.Toolchain/async-wit/world.wit")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
