using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Integration;

public sealed class NativeLinkedMemoryLayoutIntegrationTests
{
    private static readonly JsonSerializerOptions ReceiptOptions = new() { WriteIndented = true };

    [Theory]
    [InlineData("wasm32", false)]
    [InlineData("wasm32", true)]
    [InlineData("wasm64", false)]
    [InlineData("wasm64", true)]
    [Trait("Category", "NativeInteropIntegration")]
    public void ObservesRealLinkedBoundsAcrossProbeRelinkAndOptimization(string targetName, bool lto)
    {
        using var temporary = new TemporaryDirectory();
        var artifactRoot = Environment.GetEnvironmentVariable("NETWASM_NATIVE_INTEROP_EVIDENCE_ROOT") ?? temporary.Path;
        var cell = Path.Combine(artifactRoot, targetName + (lto ? "-lto" : "-object"));
        Assert.False(Directory.Exists(cell)); // Immutable run identity; never reuse or overwrite accepted evidence.
        Directory.CreateDirectory(cell);
        var repository = RepositoryRoot();
        var assetRoot = Path.Combine(repository, "src", "NetWasm.Runtime.Pack", "runtime");
        var manifest = new RuntimePackManifestReader().Read(Path.Combine(assetRoot, "runtime-pack.json"));
        var target = manifest.Targets.Single(item => item.Target == targetName);
        var emsdk = Environment.GetEnvironmentVariable("NETWASM_EMSDK_ROOT") ??
            Environment.GetEnvironmentVariable("EMSDK") ?? Environment.GetEnvironmentVariable("EMSDK_ROOT") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "emsdk");
        var executableSuffix = OperatingSystem.IsWindows() ? ".exe" : "";
        var tools = Path.Combine(emsdk, "upstream", "bin");
        var clang = Path.Combine(tools, "clang" + executableSuffix);
        var linker = Path.Combine(tools, "wasm-ld" + executableSuffix);
        var archiver = Path.Combine(tools, "llvm-ar" + executableSuffix);
        var optimizer = Path.Combine(tools, "wasm-opt" + executableSuffix);
        Assert.All(new[] { clang, linker, archiver, optimizer }, path => Assert.True(File.Exists(path)));
        var commands = new CommandInvoker();
        var source = Path.Combine(AppContext.BaseDirectory, "IntegrationAssets", "native-mule.c");
        var objectPath = Path.Combine(cell, "mule.o");
        var archive = Path.Combine(cell, "libmule.a");
        var compile = ImmutableArray.Create("--target=" + targetName + "-unknown-emscripten", "-O2",
            "-ffreestanding", "-ffunction-sections", "-fdata-sections", "-c", source, "-o", objectPath);
        if (lto)
            compile = compile.Add("-flto");
        commands.Invoke(new(clang, compile, Path.Combine(cell, "compile.log")));
        commands.Invoke(new(archiver, ["rcs", archive, objectPath], Path.Combine(cell, "archive.log")));
        var archiveValidator = new RuntimeNativeArchiveValidationArgumentBuilder();
        commands.Invoke(new(linker, archiveValidator.Build(new(targetName, archive,
            Path.Combine(cell, "archive-validation.o"))), Path.Combine(cell, "archive-validation.log")));
        var wrongTarget = targetName == "wasm32" ? "wasm64" : "wasm32";
        Assert.Throws<InvalidOperationException>(() => commands.Invoke(new(linker,
            archiveValidator.Build(new(wrongTarget, archive, Path.Combine(cell, "wrong-target-validation.o"))),
            Path.Combine(cell, "wrong-target-validation.log"))));
        var digest = new Sha256ArtifactDigestCalculator();
        var provider = new RuntimeNativeLibrary("mule", targetName, archive, digest.Calculate(archive));
        var pointerType = targetName == "wasm64" ? RuntimeNativeValueType.I64 : RuntimeNativeValueType.I32;
        var bindings = new RuntimeNativeProviderSelector().Select(new(targetName,
            [new("mule", "native_mule_probe", [], RuntimeNativeValueType.I32),
                new("mule", "native_mule_data_end", [], pointerType)], [provider]));
        var plan = new RuntimeMemoryPlanBuilder().Build(new(target, manifest.WasmPageSize, 65_537, 65_537, null));
        var output = Path.Combine(cell, "native-runtime.wasm");
        var request = new RuntimeLinkRequest(manifest, target,
            new(plan.RuntimeGlobalBase, null, plan.MaximumMemorySizeBytes), assetRoot,
            [.. target.SystemLibraries.Assets.Select(asset => Path.Combine(assetRoot, asset.Path))], output)
        {
            NativeBindings = bindings,
        };
        var arguments = new RuntimeLinkArgumentBuilder(new RuntimeLinkExportPlanBuilder());
        commands.Invoke(new(linker, arguments.Build(request), Path.Combine(cell, "probe-link.log")));
        var reader = new LinkedRuntimeModuleReader();
        var traceReader = new LinkerSymbolTraceReader();
        var bindingValidator = new RuntimeNativeBindingValidator();
        var generatedInputs = lto ? ImmutableArray.Create(output + ".lto.o") : ImmutableArray<string>.Empty;
        var probeModule = reader.Read(File.ReadAllBytes(output));
        var probe = probeModule.MemoryLayout;
        var probeBindings = bindingValidator.Validate(new RuntimeNativeBindingValidationRequest(bindings, generatedInputs,
            traceReader.Read(File.ReadAllText(Path.Combine(cell, "probe-link.log"))), probeModule));
        var validator = new LinkedMemoryLayoutValidator();
        var expected = new LinkedMemoryLayoutCalculator(validator).Calculate(plan, probe);
        Assert.True(probe.DataEnd > plan.RuntimeGlobalBase + 2 * 1024 * 1024);
        Assert.True(probe.HeapBase > plan.RuntimeGlobalBase + target.RuntimeFootprintBytes);
        Assert.True(expected.InitialMemorySizeBytes > probe.InitialMemorySizeBytes);
        var probeHash = digest.Calculate(output);
        request = request with { Layout = request.Layout with { InitialMemorySizeBytes = expected.InitialMemorySizeBytes } };
        commands.Invoke(new(linker, arguments.Build(request), Path.Combine(cell, "final-link.log")));
        var linkedModule = reader.Read(File.ReadAllBytes(output));
        var linked = linkedModule.MemoryLayout;
        validator.Validate(plan, linked, expected);
        var linkedBindings = bindingValidator.Validate(new RuntimeNativeBindingValidationRequest(bindings, generatedInputs,
            traceReader.Read(File.ReadAllText(Path.Combine(cell, "final-link.log"))), linkedModule));
        var linkedHash = digest.Calculate(output);
        commands.Invoke(new(optimizer,
            new RuntimeOptimizationArgumentBuilder().Build(new(target, output, RuntimeWasmOptimization.Oz)),
            Path.Combine(cell, "optimize.log")));
        var optimizedModule = reader.Read(File.ReadAllBytes(output));
        var optimized = optimizedModule.MemoryLayout;
        validator.Validate(plan, optimized, expected);
        var optimizedBindings = bindingValidator.Validate(new RuntimeNativeBindingValidationRequest(bindings, generatedInputs,
            traceReader.Read(File.ReadAllText(Path.Combine(cell, "final-link.log"))), optimizedModule));
        // Inspection exports do not imply that this gate has qualified final public export pruning or execution.
        File.WriteAllText(Path.Combine(cell, "receipt.json"), JsonSerializer.Serialize(new
        {
            Target = targetName,
            ArchiveKind = lto ? "lto" : "object",
            SourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant(),
            ArchiveSha256 = provider.Sha256,
            RuntimeArchiveSha256 = target.RuntimeArchive.Sha256,
            Plan = plan,
            Probe = probe,
            Expected = expected,
            Linked = linked,
            Optimized = optimized,
            ProbeBindings = probeBindings,
            LinkedBindings = linkedBindings,
            OptimizedBindings = optimizedBindings,
            ProbeSha256 = probeHash,
            LinkedSha256 = linkedHash,
            OptimizedSha256 = digest.Calculate(output),
        }, ReceiptOptions));
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
