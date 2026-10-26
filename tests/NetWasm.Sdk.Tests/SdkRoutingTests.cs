using System.Diagnostics;
using System.Xml.Linq;

namespace NetWasm.Sdk.Tests;

public sealed class SdkRoutingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void BrowserPublishUsesAnAbsoluteProjectPathAndPreservesAnExplicitDestination(
        bool absolutePublishDirectory, bool explicitBrowserDirectory)
    {
        var publishDirectory = absolutePublishDirectory
            ? Path.Combine(Path.GetTempPath(), "netwasm explicit publish")
            : "bin/Debug/netwasm0.1/publish/";
        var browserDirectory = Path.Combine(Path.GetTempPath(), "netwasm browser override");
        using var project = EvaluationProject.Create($"""
            <TargetFramework>netwasm0.1</TargetFramework>
            <OutputType>Exe</OutputType>
            <PublishDir>{publishDirectory}</PublishDir>
            {(explicitBrowserDirectory ? $"<NetWasmBrowserPublishDir>{browserDirectory}</NetWasmBrowserPublishDir>" : string.Empty)}
            """);

        var actual = project.Property("NetWasmBrowserPublishDir");
        var expected = explicitBrowserDirectory ? browserDirectory
            : Path.GetFullPath(Path.Combine(project.ProjectDirectory, publishDirectory, "browser"));
        Assert.True(Path.IsPathFullyQualified(actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("WitWebWorker", "wit")]
    [InlineData("JsModule", "jsexport")]
    public void WorkerOutputCompilesAsALibraryAndRestoresHostTools(
        string outputType,
        string workerKind)
    {
        using var project = EvaluationProject.Create($"""
            <TargetFramework>netwasm0.1</TargetFramework>
            <OutputType>{outputType}</OutputType>
            """);

        Assert.Equal("Library", project.Property("OutputType"));
        Assert.Equal(workerKind, project.Property("_NetWasmWorkerKind"));
        Assert.Equal("true", project.Property("_NetWasmCompileAsLibrary"));
        Assert.Equal(workerKind == "jsexport" ? "true" : "false", project.Property("NetWasmRawWasm"));
        Assert.Empty(project.Property("NetWasmComponentContract"));
        Assert.Empty(project.Property("NetWasmExecutionContract"));
        Assert.NotEqual("true", project.Property("_IsExecutable"));
        Assert.NotEqual("true", project.Property("HasRuntimeOutput"));
        Assert.Equal("true", project.Property("NetWasmRequiresHostTools"));
        Assert.Contains(project.Items("PackageReference"),
            package => package.StartsWith("NetWasm.HostTools.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "page", "True", "False")]
    [InlineData("<NetWasmJavaScriptHosts>worker</NetWasmJavaScriptHosts>", "worker", "False", "True")]
    [InlineData("<NetWasmJavaScriptHosts> Page; Worker </NetWasmJavaScriptHosts>", " Page; Worker ", "True", "True")]
    public void JsModuleSelectsItsJavaScriptHosts(string hosts, string expected, string page, string worker)
    {
        using var project = EvaluationProject.Create($"""
            <TargetFramework>netwasm0.1</TargetFramework>
            <OutputType>JsModule</OutputType>
            {hosts}
            """);

        Assert.Equal(expected, project.Property("NetWasmJavaScriptHosts"));
        Assert.Equal(page, project.Property("_NetWasmJavaScriptPageHost"));
        Assert.Equal(worker, project.Property("_NetWasmJavaScriptWorkerHost"));
        Assert.Equal(0, project.RunTarget("NetWasmSdkValidateJavaScriptHosts").ExitCode);
    }

    [Theory]
    [InlineData(";")]
    [InlineData("tab")]
    [InlineData("page;tab")]
    [InlineData("page;page")]
    public void JsModuleRejectsUnknownOrRepeatedHosts(string hosts)
    {
        using var project = EvaluationProject.Create($"""
            <TargetFramework>netwasm0.1</TargetFramework>
            <OutputType>JsModule</OutputType>
            <NetWasmJavaScriptHosts>{hosts}</NetWasmJavaScriptHosts>
            """);

        var (exitCode, output) = project.RunTarget("NetWasmSdkValidateJavaScriptHosts");
        Assert.NotEqual(0, exitCode);
        Assert.Contains("NWSDK076", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovedJsWebWorkerOutputTypeNamesItsReplacement()
    {
        using var project = EvaluationProject.Create("""
            <TargetFramework>netwasm0.1</TargetFramework>
            <OutputType>JsWebWorker</OutputType>
            """);

        Assert.Equal("Library", project.Property("OutputType"));
        Assert.Equal("JsWebWorker", project.Property("_NetWasmRemovedOutputType"));
        Assert.Empty(project.Property("_NetWasmWorkerKind"));
        var (exitCode, output) = project.RunTarget("NetWasmSdkRejectRemovedOutputType");
        Assert.NotEqual(0, exitCode);
        Assert.Contains("NWSDK074", output, StringComparison.Ordinal);
        Assert.Contains("<OutputType>JsModule</OutputType>", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RenamedJavaScriptModuleItemNamesItsReplacement()
    {
        using var project = EvaluationProject.Create("""
            <TargetFramework>netwasm0.1</TargetFramework>
            <OutputType>JsModule</OutputType>
            """, projectItems: """<NetWasmWorkerJavaScriptModule Include="host.mjs" Module="app.host" />""");

        var (exitCode, output) = project.RunTarget("NetWasmSdkRejectRenamedJavaScriptModuleItem");
        Assert.NotEqual(0, exitCode);
        Assert.Contains("NWSDK075", output, StringComparison.Ordinal);
        Assert.Contains("NetWasmJavaScriptModule", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WitWorkerKeepsApplicationAndWorkerWorldSelectionsSeparate()
    {
        using var project = EvaluationProject.Create("""
            <TargetFramework>netwasm0.1</TargetFramework>
            <OutputType>WitWebWorker</OutputType>
            <NetWasmWitPath>/application/worker.wit.wasm</NetWasmWitPath>
            <NetWasmWorld>example:worker@1.0.0/worker</NetWasmWorld>
            <NetWasmApplicationWorld>example:worker@1.0.0/application</NetWasmApplicationWorld>
            """);

        Assert.Equal("/application/worker.wit.wasm", project.Property("NetWasmCompilerWitPath"));
        Assert.Equal("example:worker@1.0.0/worker", project.Property("NetWasmCompilerWitWorld"));
        Assert.Equal(
            "example:worker@1.0.0/application",
            project.Property("NetWasmApplicationWorld"));
        Assert.Equal(project.Property("NetWasmWitPath"), project.Property("NetWasmCompilerWitPath"));
        Assert.Empty(project.Property("NetWasmExecutionContract"));
    }

    [Fact]
    public void WitWorkerPackageIsPreparedBeforeIncrementalIdentities()
    {
        var targets = XDocument.Load(Path.Combine(
            RepositoryRoot(),
            "src/NetWasm.Sdk/Sdk/NetWasm.Toolchain.targets"));
        var target = Assert.Single(targets.Descendants(), element =>
            element.Name.LocalName == "Target"
            && (string?)element.Attribute("Name") == "NetWasmSdkPrepareWitWorkerPackage");
        var beforeTargets = ((string?)target.Attribute("BeforeTargets") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("NetWasmWriteCompilationIdentity", beforeTargets);
        Assert.Contains("NetWasmWritePackagingIdentity", beforeTargets);
        Assert.Contains("CoreCompile", beforeTargets);
        Assert.Contains("Publish", beforeTargets);
    }

    [Theory]
    [InlineData("Exe", false)]
    [InlineData("Exe", true)]
    [InlineData("JsModule", true)]
    [InlineData("WitWebWorker", false)]
    public void EffectiveWitPathsFollowSdkDefaultsAfterCompilerTargetsImport(
        string outputType,
        bool raw)
    {
        var authoredWit = outputType == "WitWebWorker"
            ? "<NetWasmWitPath>/application/worker.wit.wasm</NetWasmWitPath>"
            : string.Empty;
        using var project = EvaluationProject.Create($"""
            <TargetFramework>netwasm0.1</TargetFramework>
            <OutputType>{outputType}</OutputType>
            <NetWasmRawWasm>{raw.ToString().ToLowerInvariant()}</NetWasmRawWasm>
            <NetWasmToolchainCompilerWitPackagePath>/toolchain/compiler.wit.wasm</NetWasmToolchainCompilerWitPackagePath>
            <NetWasmToolchainWitPackagePath>/toolchain/command.wit.wasm</NetWasmToolchainWitPackagePath>
            {authoredWit}
            """, compilerTargetsBeforeSdk: true);

        Assert.NotEmpty(project.Property("_NetWasmCompilerWitPath"));
        Assert.Equal(
            project.Property("NetWasmCompilerWitPath"),
            project.Property("_NetWasmCompilerWitPath"));
        if (outputType != "JsModule")
        {
            Assert.NotEmpty(project.Property("_NetWasmComponentWitPath"));
            Assert.Equal(
                project.Property("NetWasmWitPath"),
                project.Property("_NetWasmComponentWitPath"));
        }
    }

    [Fact]
    public void OrdinaryLibraryDoesNotRestoreWorkerHostTools()
    {
        using var project = EvaluationProject.Create("""
            <TargetFramework>netwasm0.1</TargetFramework>
            <OutputType>Library</OutputType>
            """);

        Assert.Equal("Library", project.Property("OutputType"));
        Assert.Empty(project.Property("_NetWasmWorkerKind"));
        Assert.NotEqual("true", project.Property("NetWasmRequiresHostTools"));
        Assert.DoesNotContain(project.Items("PackageReference"),
            package => package.StartsWith("NetWasm.HostTools.", StringComparison.Ordinal));
    }

    // NetWasmWit items generate bindings during the build, a class library's
    // too, so they restore the bindings tool and the host tools. A package
    // directory reads every WIT file under it and keeps no trailing separator,
    // which would escape the closing quote of its argument on Windows.
    [Fact]
    public void WitItemsGenerateBindingsFromFilesAndPackageDirectories()
    {
        using var project = EvaluationProject.Create("""
            <TargetFramework>netwasm0.1</TargetFramework>
            <OutputType>Library</OutputType>
            """,
            projectItems: """
            <NetWasmWit Include="host api/world.wit" World="my-world" Namespace="App.Host" Accessibility="Internal" />
            <NetWasmWit Include="wit packages/" />
            """,
            files: new Dictionary<string, string>
            {
                ["host api/world.wit"] = "package my:ns;\nworld my-world {}\n",
                ["wit packages/main.wit"] = "package my:pkg;\nworld main {}\n",
                ["wit packages/deps/dep.wit"] = "package my:dep;\ninterface api {}\n",
            });

        Assert.Contains(project.Items("PackageReference"),
            package => package.StartsWith("NetWasm.HostTools.", StringComparison.Ordinal));
        Assert.Contains("NetWasm.Wit.Bindings.Tool", project.Items("PackageDownload"));
        var bindings = project.Items("NetWasmWitBinding").Select(binding => binding.Split('|')).ToArray();
        Assert.Equal(2, bindings.Length);
        var file = Assert.Single(bindings, binding => binding[0].EndsWith("world.wit", StringComparison.Ordinal));
        Assert.EndsWith("world.wit.g.cs", file[1], StringComparison.Ordinal);
        Assert.Equal("--world \"my-world\" --namespace \"App.Host\" --accessibility internal", file[2]);
        var directory = Assert.Single(bindings, binding => binding[0].EndsWith("wit packages", StringComparison.Ordinal));
        Assert.EndsWith("wit packages.g.cs", directory[1], StringComparison.Ordinal);
        Assert.Empty(directory[2]);
        var inputs = project.Items("NetWasmWitBindingInput").Select(input => input.Replace('\\', '/')).ToArray();
        Assert.Contains(inputs, input => input.EndsWith("wit packages/main.wit", StringComparison.Ordinal));
        Assert.Contains(inputs, input => input.EndsWith("wit packages/deps/dep.wit", StringComparison.Ordinal));
        Assert.Contains(inputs, input => input.EndsWith("host api/world.wit", StringComparison.Ordinal));
    }

    // The compilation identity lists the declared import modules, so changing
    // them recompiles; a generated worker client's reserved module is not one.
    [Fact]
    public void CompilationIdentityListsTheDeclaredImportModules()
    {
        using var project = EvaluationProject.Create("""
            <TargetFramework>netwasm0.1</TargetFramework>
            <OutputType>Exe</OutputType>
            """,
            includeCompilerTargets: true,
            projectItems: """
            <NetWasmImportModule Include="host_gl" />
            <NetWasmJavaScriptModule Include="host-ui.mjs" Module="host_ui" />
            <NetWasmJavaScriptModule Include="worker.mjs" Module="netwasm:worker/Worker" NetWasmGeneratedWorkerClient="true" />
            """,
            probeTargets: "NetWasmWriteCompilationIdentity");

        Assert.Contains("importModules=host_gl,host_ui", project.Items("NetWasmCompilationIdentityLine"));
    }

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
        Assert.Contains("IncludeAssets=\"compile;analyzers\"", tfmProps, StringComparison.Ordinal);
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
        Assert.Contains(
            "<NetWasmStructuredDiagnostics Condition=\"'$(NetWasmStructuredDiagnostics)' == '' AND '$(Configuration)' == 'Debug'\">true</NetWasmStructuredDiagnostics>",
            compilerTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "<NetWasmStructuredDiagnostics Condition=\"'$(NetWasmStructuredDiagnostics)' == ''\">false</NetWasmStructuredDiagnostics>",
            compilerTargets,
            StringComparison.Ordinal);
        Assert.Contains(
            "NoLogo=\"$(NoLogo)\"",
            compilerTargets,
            StringComparison.Ordinal);

        using var project = EvaluationProject.Create("""
            <TargetFramework>netwasm0.1</TargetFramework>
            <OutputType>Exe</OutputType>
            """, includeCompilerTargets: true);
        Assert.Equal("allowAll", project.Property("NetWasmNetworkPolicy"));
        Assert.Equal("true", project.Property("NetWasmRandomness"));
        Assert.Equal(project.ProjectDirectory, project.Property("RunWorkingDirectory"));
    }

    [Fact]
    public void ManagedExceptionDiagnosticsArePublishedForEveryDeploymentMode()
    {
        var repositoryRoot = FindRepositoryRoot();
        var toolchainTargets = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src/NetWasm.Sdk/Sdk/NetWasm.Toolchain.targets"));
        var compilerTargets = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src/NetWasm.Compiler.Tasks/buildTransitive/NetWasm.Compiler.Tasks.targets"));
        const string mediaType =
            "application/vnd.netwasm.stack-trace-symbols+json;version=1";

        Assert.Equal(4,
            toolchainTargets.Split("<Role>stack-trace-symbols</Role>",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(4,
            toolchainTargets.Split($"<MediaType>{mediaType}</MediaType>",
                StringSplitOptions.None).Length - 1);
        Assert.Contains($"<MediaType>{mediaType}</MediaType>",
            compilerTargets,
            StringComparison.Ordinal);
        Assert.Equal(4,
            toolchainTargets.Split("<Role>exception-type-map</Role>",
                StringSplitOptions.None).Length - 1);
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
    public void ReleaseHostsTrustTheirDigestPinnedDeploymentManifestUnlessAsked()
    {
        using var debug = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework>",
            configuration: "Debug",
            includeCompilerTargets: true);
        using var release = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework>",
            includeCompilerTargets: true);
        using var releaseOptIn = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework><NetWasmValidateDeploymentManifest>true</NetWasmValidateDeploymentManifest>",
            includeCompilerTargets: true);

        Assert.Equal("true", debug.Property("NetWasmValidateDeploymentManifest"));
        Assert.Equal("false", release.Property("NetWasmValidateDeploymentManifest"));
        Assert.Equal("true", releaseOptIn.Property("NetWasmValidateDeploymentManifest"));
    }

    [Fact]
    public void StructuredDiagnosticsDefaultToDebugAndAllowExplicitOverrides()
    {
        using var debug = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework>",
            configuration: "Debug",
            includeCompilerTargets: true);
        using var release = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework>",
            includeCompilerTargets: true);
        using var debugOptOut = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework><NetWasmStructuredDiagnostics>false</NetWasmStructuredDiagnostics>",
            configuration: "Debug",
            includeCompilerTargets: true);
        using var releaseOptIn = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework><NetWasmStructuredDiagnostics>true</NetWasmStructuredDiagnostics>",
            includeCompilerTargets: true);

        Assert.Equal("true", debug.Property("NetWasmStructuredDiagnostics"));
        Assert.Equal("false", release.Property("NetWasmStructuredDiagnostics"));
        Assert.Equal("false", debugOptOut.Property("NetWasmStructuredDiagnostics"));
        Assert.Equal("true", releaseOptIn.Property("NetWasmStructuredDiagnostics"));
        Assert.Equal(
            "netwasm:component/diagnostic-command@1.0.0",
            debug.Property("NetWasmWorld"));
        Assert.Equal("command", release.Property("NetWasmWorld"));
    }

    [Fact]
    public void FinalWasmOptimizationDefaultsToOzAndAllowsExplicitNone()
    {
        using var defaultPolicy = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework>",
            includeCompilerTargets: true);
        using var noFinalOptimization = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework><NetWasmOptimization>None</NetWasmOptimization>",
            includeCompilerTargets: true);
        using var explicitOz = EvaluationProject.Create(
            "<TargetFramework>netwasm0.1</TargetFramework><NetWasmOptimization>Oz</NetWasmOptimization>",
            includeCompilerTargets: true);

        Assert.Equal("Oz", defaultPolicy.Property("NetWasmOptimization"));
        Assert.Equal("None", noFinalOptimization.Property("NetWasmOptimization"));
        Assert.Equal("Oz", explicitOz.Property("_NetWasmCanonicalOptimization"));
        Assert.Equal("Auto", defaultPolicy.Property("NetWasmManagedInliningHints"));
        Assert.Equal("Auto", defaultPolicy.Property(
            "NetWasmManagedLoopInvariantFieldHoisting"));
        Assert.Equal("Auto", defaultPolicy.Property(
            "NetWasmManagedStaticDataViewElimination"));
        Assert.Equal("Auto", defaultPolicy.Property(
            "NetWasmManagedBranchPrefixDuplication"));
        Assert.Equal("Auto", defaultPolicy.Property(
            "NetWasmManagedScalarCallPreservation"));
        Assert.Equal("Auto", defaultPolicy.Property("NetWasmManagedArrayBoundsCheckElimination"));
        Assert.Equal("Auto", defaultPolicy.Property("NetWasmManagedLoopUnrolling"));
        Assert.Equal("Auto", defaultPolicy.Property("NetWasmManagedConstantArgumentPropagation"));
        Assert.Equal("Auto", defaultPolicy.Property("NetWasmManagedStaticArrayPopulationElimination"));
        Assert.Equal("Auto", defaultPolicy.Property("NetWasmManagedConstantCallFolding"));
        Assert.Equal("Auto", defaultPolicy.Property("NetWasmManagedSwitchBitTests"));
        Assert.Equal("Auto", defaultPolicy.Property("NetWasmManagedDevirtualization"));
        Assert.Equal("Auto", defaultPolicy.Property("NetWasmManagedAllocationElimination"));
        Assert.Equal("Auto", defaultPolicy.Property("NetWasmManagedLoopRotation"));
        Assert.Equal("Auto", defaultPolicy.Property("NetWasmManagedArrayStoreCheckElimination"));
    }

    [Theory]
    [InlineData("IgnoreAggressive")]
    [InlineData("Preserve")]
    public void RetainsExplicitManagedInliningHints(string hints)
    {
        using var project = EvaluationProject.Create(
            $"<TargetFramework>netwasm0.1</TargetFramework><NetWasmManagedInliningHints>{hints}</NetWasmManagedInliningHints>",
            includeCompilerTargets: true);
        Assert.Equal(hints, project.Property("NetWasmManagedInliningHints"));
    }

    [Theory]
    [InlineData("NetWasmManagedArrayBoundsCheckElimination")]
    [InlineData("NetWasmManagedLoopUnrolling")]
    [InlineData("NetWasmManagedConstantArgumentPropagation")]
    [InlineData("NetWasmManagedStaticArrayPopulationElimination")]
    [InlineData("NetWasmManagedConstantCallFolding")]
    [InlineData("NetWasmManagedSwitchBitTests")]
    [InlineData("NetWasmManagedDevirtualization")]
    [InlineData("NetWasmManagedAllocationElimination")]
    [InlineData("NetWasmManagedLoopRotation")]
    [InlineData("NetWasmManagedArrayStoreCheckElimination")]
    public void PreservesExplicitCompilerReductionSelections(string property)
    {
        using var project = EvaluationProject.Create(
            $"<TargetFramework>netwasm0.1</TargetFramework><{property}>Disabled</{property}>",
            includeCompilerTargets: true);
        Assert.Equal("Disabled", project.Property(property));
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
        var frontendIdentity = Assert.Single(targets.Descendants(targetNamespace + "Target"), target =>
            (string?)target.Attribute("Name") == "NetWasmWriteCompilationIdentity");
        var frontendIdentityContract = frontendIdentity.ToString(
            SaveOptions.DisableFormatting);
        Assert.DoesNotContain("NetWasmManagedInliningHints", frontendIdentityContract,
            StringComparison.Ordinal);
        // Structured diagnostics adds a runtime feature to the core module, so
        // switching it must recompile rather than reuse the previous module.
        foreach (var value in new[]
                 {
                     "$(_NetWasmCanonicalOptimization)",
                     "$(NetWasmStructuredDiagnostics)",
                     "$(NetWasmManagedLoopInvariantFieldHoisting)",
                     "$(NetWasmManagedStaticDataViewElimination)",
                     "$(NetWasmManagedBranchPrefixDuplication)",
                     "$(NetWasmManagedScalarCallPreservation)",
                     "$(NetWasmManagedArrayBoundsCheckElimination)",
                     "$(NetWasmManagedLoopUnrolling)",
                     "$(NetWasmManagedConstantArgumentPropagation)",
                     "$(NetWasmManagedStaticArrayPopulationElimination)",
                     "$(NetWasmManagedConstantCallFolding)",
                     "$(NetWasmManagedSwitchBitTests)",
                     "$(NetWasmManagedDevirtualization)",
                     "$(NetWasmManagedAllocationElimination)",
                     "$(NetWasmManagedLoopRotation)",
                     "$(NetWasmManagedArrayStoreCheckElimination)",
                 })
        {
            Assert.Contains(value, frontendIdentityContract, StringComparison.Ordinal);
        }
        var compilation = Assert.Single(targets.Descendants(
            targetNamespace + "NetWasmCompileTask"));
        Assert.Equal(
            "$(NetWasmManagedLoopInvariantFieldHoisting)",
            (string?)compilation.Attribute("ManagedLoopInvariantFieldHoisting"));
        Assert.Equal(
            "$(NetWasmManagedStaticDataViewElimination)",
            (string?)compilation.Attribute("ManagedStaticDataViewElimination"));
        Assert.Equal(
            "$(NetWasmManagedBranchPrefixDuplication)",
            (string?)compilation.Attribute("ManagedBranchPrefixDuplication"));
        Assert.Equal(
            "$(NetWasmManagedScalarCallPreservation)",
            (string?)compilation.Attribute("ManagedScalarCallPreservation"));
        Assert.Equal("$(NetWasmManagedArrayBoundsCheckElimination)", (string?)compilation.Attribute("ManagedArrayBoundsCheckElimination"));
        Assert.Equal("$(NetWasmManagedLoopUnrolling)", (string?)compilation.Attribute("ManagedLoopUnrolling"));
        Assert.Equal("$(NetWasmManagedConstantArgumentPropagation)", (string?)compilation.Attribute("ManagedConstantArgumentPropagation"));
        Assert.Equal("$(NetWasmManagedStaticArrayPopulationElimination)", (string?)compilation.Attribute("ManagedStaticArrayPopulationElimination"));
        Assert.Equal("$(NetWasmManagedConstantCallFolding)", (string?)compilation.Attribute("ManagedConstantCallFolding"));
        Assert.Equal("$(NetWasmManagedSwitchBitTests)", (string?)compilation.Attribute("ManagedSwitchBitTests"));
        Assert.Equal("$(NetWasmManagedDevirtualization)", (string?)compilation.Attribute("ManagedDevirtualization"));
        Assert.Equal("$(NetWasmManagedAllocationElimination)", (string?)compilation.Attribute("ManagedAllocationElimination"));
        Assert.Equal("$(NetWasmManagedLoopRotation)", (string?)compilation.Attribute("ManagedLoopRotation"));
        Assert.Equal("$(NetWasmManagedArrayStoreCheckElimination)", (string?)compilation.Attribute("ManagedArrayStoreCheckElimination"));
        foreach (var value in new[]
                 {
                     "$(_NetWasmCanonicalOptimization)",
                     "packagingPolicyRevision=4",
                     "$(NetWasmManagedInliningHints)",
                     "$(NetWasmTarget)",
                     "$(NetWasmWorld)",
                     "$(_NetWasmComponentWitPath)",
                     "$(NetWasmApplicationWorld)",
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
            var task = Assert.Single(packaging.Elements(), element => element.Name.LocalName is
                "NetWasmComponentizeTask" or "NetWasmLinkRawModuleTask");
            Assert.Equal("$(NetWasmManagedInliningHints)", (string?)task.Attribute("ManagedInliningHints"));
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
            $"NetWasm.Ref|netwasm0.1|NetWasm,Version=v0.1|{project.Property("NetWasmRefPackageVersion")}|compile;analyzers|none|all",
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
            $"NetWasm.Ref|netwasm0.1|NetWasm,Version=v0.1|{project.Property("NetWasmRefPackageVersion")}|compile;analyzers|none|all",
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
        private const string ItemSeparator = "||#||";
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
            bool includeCompilerTargets = false,
            bool compilerTargetsBeforeSdk = false,
            string projectItems = "",
            IReadOnlyDictionary<string, string>? files = null,
            string probeTargets = "")
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "netwasm-sdk-evaluation", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            foreach (var (relativePath, contents) in files ?? new Dictionary<string, string>())
            {
                var path = System.IO.Path.Combine(root, relativePath);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                File.WriteAllText(path, contents);
            }
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
                  {(projectItems.Length == 0 ? string.Empty : $"<ItemGroup>{projectItems}</ItemGroup>")}
                  {(includePackageReference ? "<ItemGroup><PackageReference Include=\"Example.Package\" Version=\"[1.0.0]\" PrivateAssets=\"all\" IncludeAssets=\"compile\" ExcludeAssets=\"runtime\" /></ItemGroup>" : string.Empty)}
                  {(includeProjectReference || includeStockProjectReference ? "<ItemGroup><ProjectReference Include=\"Referenced.csproj\" PrivateAssets=\"all\" IncludeAssets=\"compile\" ExcludeAssets=\"runtime\" /></ItemGroup>" : string.Empty)}
                  {(compilerTargetsBeforeSdk ? $"<Import Project=\"{Escape(compilerTargetsPath)}\" />" : string.Empty)}
                  <Import Project="{Escape(sdkRoot)}/Sdk.targets" />
                  {(includeCompilerTargets ? $"<Import Project=\"{Escape(compilerTargetsPath)}\" />" : string.Empty)}
                  <Target Name="PrintNetWasmRouting" DependsOnTargets="$(NetWasmSdkPackRouteProbeDependsOn);NetWasmSdkCollectInnerPackageReferences;_NetWasmResolveWitBindings{(probeTargets.Length == 0 ? string.Empty : ";" + probeTargets)}{(collectPackEvidence ? ";NetWasmCollectPackEvidence" : string.Empty)}">
                    <Message Importance="High" Text="__NETWASM_PROP__OutputType=$(OutputType)" />
                    <Message Importance="High" Text="__NETWASM_PROP___NetWasmRemovedOutputType=$(_NetWasmRemovedOutputType)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmJavaScriptHosts=$(NetWasmJavaScriptHosts)" />
                    <Message Importance="High" Text="__NETWASM_PROP___NetWasmJavaScriptPageHost=$(_NetWasmJavaScriptPageHost)" />
                    <Message Importance="High" Text="__NETWASM_PROP___NetWasmJavaScriptWorkerHost=$(_NetWasmJavaScriptWorkerHost)" />
                    <Message Importance="High" Text="__NETWASM_PROP___NetWasmWorkerKind=$(_NetWasmWorkerKind)" />
                    <Message Importance="High" Text="__NETWASM_PROP___NetWasmCompileAsLibrary=$(_NetWasmCompileAsLibrary)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmRawWasm=$(NetWasmRawWasm)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmComponentContract=$(NetWasmComponentContract)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmExecutionContract=$(NetWasmExecutionContract)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmCompilerWitPath=$(NetWasmCompilerWitPath)" />
                    <Message Importance="High" Text="__NETWASM_PROP___NetWasmCompilerWitPath=$(_NetWasmCompilerWitPath)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmCompilerWitWorld=$(NetWasmCompilerWitWorld)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmWitPath=$(NetWasmWitPath)" />
                    <Message Importance="High" Text="__NETWASM_PROP___NetWasmComponentWitPath=$(_NetWasmComponentWitPath)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmWorld=$(NetWasmWorld)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmApplicationWorld=$(NetWasmApplicationWorld)" />
                    <Message Importance="High" Text="__NETWASM_PROP___IsExecutable=$(_IsExecutable)" />
                    <Message Importance="High" Text="__NETWASM_PROP__HasRuntimeOutput=$(HasRuntimeOutput)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmRequiresHostTools=$(NetWasmRequiresHostTools)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmBrowserPublishDir=$(NetWasmBrowserPublishDir)" />
                    <Message Importance="High" Text="__NETWASM_ITEM__PackageReference=@(PackageReference, '{ItemSeparator}')" />
                    <Message Importance="High" Text="__NETWASM_ITEM__PackageDownload=@(PackageDownload, '{ItemSeparator}')" />
                    <Message Importance="High" Text="__NETWASM_ITEM__NetWasmWitBinding=@(_NetWasmWitBinding->'%(Identity)|%(BindingPath)|%(Arguments)', '{ItemSeparator}')" />
                    <Message Importance="High" Text="__NETWASM_ITEM__NetWasmWitBindingInput=@(_NetWasmWitBindingInput, '{ItemSeparator}')" />
                    <Message Importance="High" Text="__NETWASM_ITEM__NetWasmCompilationIdentityLine=@(_NetWasmCompilationIdentityLine, '{ItemSeparator}')" />
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
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmValidateDeploymentManifest=$(NetWasmValidateDeploymentManifest)" />
                    <Message Importance="High" Text="__NETWASM_PROP__RunWorkingDirectory=$(RunWorkingDirectory)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedStackTrace=$(NetWasmManagedStackTrace)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmStructuredDiagnostics=$(NetWasmStructuredDiagnostics)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmOptimization=$(NetWasmOptimization)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedInliningHints=$(NetWasmManagedInliningHints)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedLoopInvariantFieldHoisting=$(NetWasmManagedLoopInvariantFieldHoisting)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedStaticDataViewElimination=$(NetWasmManagedStaticDataViewElimination)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedBranchPrefixDuplication=$(NetWasmManagedBranchPrefixDuplication)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedScalarCallPreservation=$(NetWasmManagedScalarCallPreservation)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedArrayBoundsCheckElimination=$(NetWasmManagedArrayBoundsCheckElimination)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedLoopUnrolling=$(NetWasmManagedLoopUnrolling)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedConstantArgumentPropagation=$(NetWasmManagedConstantArgumentPropagation)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedStaticArrayPopulationElimination=$(NetWasmManagedStaticArrayPopulationElimination)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedConstantCallFolding=$(NetWasmManagedConstantCallFolding)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedSwitchBitTests=$(NetWasmManagedSwitchBitTests)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedDevirtualization=$(NetWasmManagedDevirtualization)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedAllocationElimination=$(NetWasmManagedAllocationElimination)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedLoopRotation=$(NetWasmManagedLoopRotation)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmManagedArrayStoreCheckElimination=$(NetWasmManagedArrayStoreCheckElimination)" />
                    <Message Importance="High" Text="__NETWASM_PROP___NetWasmCanonicalOptimization=$(_NetWasmCanonicalOptimization)" />
                    <Message Importance="High" Text="__NETWASM_PROP__NetWasmRefPackageVersion=$(NetWasmRefPackageVersion)" />
                    <Message Importance="High" Text="__NETWASM_ITEM__NetWasmSdkProfile=@(NetWasmSdkProfile->'%(Identity)|%(CanonicalFolder)', '{ItemSeparator}')" />
                    <Message Importance="High" Text="__NETWASM_ITEM__NetWasmSdkPackRoute=@(NetWasmSdkPackRoute, '{ItemSeparator}')" />
                    <Message Importance="High" Text="__NETWASM_ITEM__NetWasmSdkInnerPackageDependency=@(_NetWasmSdkInnerPackageDependency->'%(Identity)|%(TargetFrameworkAlias)|%(TargetFramework)|%(Version)|%(IncludeAssets)|%(ExcludeAssets)|%(PrivateAssets)', '{ItemSeparator}')" />
                    <Message Importance="High" Text="__NETWASM_ITEM__NetWasmSdkInnerProjectDependency=@(_NetWasmSdkInnerProjectDependency->'%(Identity)|%(ProjectPath)|%(TargetFrameworkAlias)|%(TargetFramework)|%(Version)|%(IncludeAssets)|%(ExcludeAssets)|%(PrivateAssets)', '{ItemSeparator}')" />
                    <Message Importance="High" Text="__NETWASM_ITEM__NetWasmSdkInnerProjectDependencyResolution=@(_NetWasmSdkInnerProjectDependency->'%(ProjectReferenceResolution)|%(ProjectPath)|%(ProjectReferenceTargetFramework)|%(TargetFrameworkAlias)|%(TargetFramework)|%(Configuration)|%(Platform)|%(RuntimeIdentifier)|%(PrivateAssets)', '{ItemSeparator}')" />
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
                            : value.Split(ItemSeparator, StringSplitOptions.RemoveEmptyEntries);
                    }
                }
            }

            return new EvaluationProject(root, parsedProperties, parsedItems);
        }

        public string Property(string name) => properties.GetValueOrDefault(name, string.Empty);

        // Runs one SDK target against the same project and returns MSBuild's
        // exit code with its combined output, for diagnostics the probe cannot print.
        public (int ExitCode, string Output) RunTarget(string target)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = FindRepositoryRoot(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            foreach (var argument in new[]
                     {
                         "msbuild", System.IO.Path.Combine(root, "Routing.csproj"), $"/t:{target}",
                         "/nologo", "/v:minimal", "/m:1", "/nr:false"
                     })
            {
                process.StartInfo.ArgumentList.Add(argument);
            }
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return (process.ExitCode, outputTask.GetAwaiter().GetResult() + errorTask.GetAwaiter().GetResult());
        }

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
