using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Integration;

// A native archive that imports a host function keeps that import through the
// real runtime link; it validates only when the project declares the module.
public sealed class NativeImportModuleIntegrationTests
{
    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    [Trait("Category", "NativeInteropIntegration")]
    public void ArchiveImportsFromADeclaredModuleSurviveTheRuntimeLink(string targetName)
    {
        using var temporary = new TemporaryDirectory();
        var repository = RepositoryRoot();
        var assetRoot = Path.Combine(repository, "src", "NetWasm.Runtime.Pack", "runtime");
        var manifest = new RuntimePackManifestReader(new RuntimePackManifestParser()).Read(Path.Combine(assetRoot, "runtime-pack.json"));
        var target = new RuntimePackTargetSelector(new RuntimeOptimizationPolicyResolver()).Select(manifest, targetName, null);
        var emsdk = Environment.GetEnvironmentVariable("NETWASM_EMSDK_ROOT") ??
            Environment.GetEnvironmentVariable("EMSDK") ?? Environment.GetEnvironmentVariable("EMSDK_ROOT") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "emsdk");
        var executableSuffix = OperatingSystem.IsWindows() ? ".exe" : "";
        var tools = Path.Combine(emsdk, "upstream", "bin");
        var clang = Path.Combine(tools, "clang" + executableSuffix);
        var linker = Path.Combine(tools, "wasm-ld" + executableSuffix);
        var archiver = Path.Combine(tools, "llvm-ar" + executableSuffix);
        Assert.All(new[] { clang, linker, archiver }, path => Assert.True(File.Exists(path)));
        var commands = new CommandInvoker();
        var source = Path.Combine(AppContext.BaseDirectory, "IntegrationAssets", "native-host-import.c");
        var objectPath = Path.Combine(temporary.Path, "host-ui.o");
        var archive = Path.Combine(temporary.Path, "libhost_ui.a");
        commands.Invoke(new(clang, ["--target=" + targetName + "-unknown-emscripten", "-O2", "-ffreestanding",
            "-ffunction-sections", "-fdata-sections", "-c", source, "-o", objectPath], Path.Combine(temporary.Path, "compile.log")));
        commands.Invoke(new(archiver, ["rcs", archive, objectPath], Path.Combine(temporary.Path, "archive.log")));
        var provider = new RuntimeNativeLibrary("host_ui", targetName, archive, new Sha256ArtifactDigestCalculator().Calculate(archive));
        var bindings = new RuntimeNativeProviderSelector().Select(new(targetName,
            [new("host_ui", "app_create_node", [RuntimeNativeValueType.I32], RuntimeNativeValueType.I64)], [provider]));
        var plan = new RuntimeMemoryPlanBuilder().Build(new(target, manifest.WasmPageSize, 65_537, 65_537, null));
        var output = Path.Combine(temporary.Path, "native-runtime.wasm");
        var request = new RuntimeLinkRequest(manifest, target,
            new(plan.RuntimeGlobalBase, null, plan.MaximumMemorySizeBytes), assetRoot,
            [.. target.SystemLibraries.Assets.Select(asset => Path.Combine(assetRoot, asset.Path))], output)
        {
            NativeBindings = bindings,
        };
        commands.Invoke(new(linker, new RuntimeLinkArgumentBuilder(new RuntimeLinkExportPlanBuilder()).Build(request),
            Path.Combine(temporary.Path, "link.log")));
        var linked = new LinkedRuntimeModuleReader().Read(File.ReadAllBytes(output));
        var profile = target.NativeValidation!;
        var validator = new RuntimeLinkedImportValidator(new RuntimeNativeValidationProfileValidator());

        Assert.Contains(linked.Imports, import => import.Module == "host_ui" && import.Name == "create_node");
        validator.Validate(profile, linked, importModules: new HashSet<string> { "host_ui" });
        var undeclared = Assert.Throws<InvalidOperationException>(() => validator.Validate(profile, linked));
        Assert.Contains("host_ui.create_node", undeclared.Message, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "NetWasm.Runtime.Pack")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("The native interop integration gate requires the source checkout.");
    }
}
