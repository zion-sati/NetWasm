using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Runtime.Pack.Composition;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;
using NetWasm.TestInfrastructure;

namespace NetWasm.Runtime.Pack.Tests.Integration;

public sealed class NativeManagedExecutionIntegrationTests
{
    private static readonly int[] ExpectedInvocationValues = [42, 42];
    private static readonly int[] ExpectedNamedCallbackInvocationValues = [-100, 35];
    private static readonly int[] ExpectedCallbackOnlyInvocationValues = [1, 137];
    private static readonly string[] ToolNames = ["node", "wasm-ld", "wasm-opt", "wasm-merge"];
    private static readonly ImmutableDictionary<string, string> DesktopAliases =
        new[] { "System.Runtime", "System.Runtime.InteropServices" }
            .ToImmutableDictionary(name => name, _ => "NetWasm.CoreLib");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Theory]
    [InlineData("wasm32", false)]
    [InlineData("wasm32", true)]
    [InlineData("wasm64", false)]
    [InlineData("wasm64", true)]
    [Trait("Category", "NativeManagedIntegration")]
    public void ExecutesOrdinaryManagedNativeCallsAgainstRealArchivesBeforeAndAfterOptimization(string target, bool lto)
        => Execute(target, lto, generated: false);

    [Theory]
    [InlineData("wasm32", false)]
    [InlineData("wasm32", true)]
    [InlineData("wasm64", false)]
    [InlineData("wasm64", true)]
    [Trait("Category", "NativeGeneratedIntegration")]
    public void ExecutesStockGeneratedNativeCallsAfterDesktopOracle(string target, bool lto)
        => Execute(target, lto, generated: true);

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    [Trait("Category", "NativeAggregateIntegration")]
    public void ExecutesStockGeneratedAggregateCallsAfterDesktopOracle(string target)
        => Execute(target, lto: false, generated: true, aggregates: true);

    [Theory]
    [InlineData("wasm32", false)]
    [InlineData("wasm32", true)]
    [InlineData("wasm64", false)]
    [InlineData("wasm64", true)]
    [Trait("Category", "NativeAggregateProfileIntegration")]
    public void ExecutesQualifiedAggregateProfileAfterDesktopOracle(string target, bool lto)
        => Execute(target, lto, generated: true, aggregates: true, aggregateProfile: true);

    [Theory]
    [InlineData("wasm32", false)]
    [InlineData("wasm32", true)]
    [InlineData("wasm64", false)]
    [InlineData("wasm64", true)]
    [Trait("Category", "NativeArrayUtf8Integration")]
    public void ExecutesStockGeneratedCountedArraysAndUtf8AfterDesktopOracle(string target, bool lto)
        => Execute(target, lto, generated: true, arraysUtf8: true);

    [Theory]
    [InlineData("wasm32", false)]
    [InlineData("wasm32", true)]
    [InlineData("wasm64", false)]
    [InlineData("wasm64", true)]
    [Trait("Category", "NativeSafeHandleIntegration")]
    public void ExecutesStockGeneratedSafeHandleBorrowingAndOwnershipAfterDesktopOracle(string target, bool lto)
        => Execute(target, lto, generated: true, safeHandles: true);

    [Theory]
    [InlineData("wasm32", false)]
    [InlineData("wasm32", true)]
    [InlineData("wasm64", false)]
    [InlineData("wasm64", true)]
    [Trait("Category", "NativeCallbackIntegration")]
    public void ExecutesCompilerProducedStaticCallbacksThroughRealArchive(
        string target,
        bool lto)
        => Execute(target, lto, generated: true, callbacks: true);

    [Theory]
    [InlineData("wasm32", false)]
    [InlineData("wasm32", true)]
    [InlineData("wasm64", false)]
    [InlineData("wasm64", true)]
    [Trait("Category", "NativeNamedCallbackIntegration")]
    public void ExecutesNamedManagedEntryThroughRealArchive(string target, bool lto)
        => Execute(
            target,
            lto,
            generated: false,
            namedCallbacks: true);

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    [Trait("Category", "NativeCallbackOnlyIntegration")]
    public void ExecutesCallbackOnlyOutputWithoutNativeProviders(string target)
        => Execute(
            target,
            lto: false,
            generated: false,
            callbackOnly: true);

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    [Trait("Category", "NativeCallbackFailureIntegration")]
    public void PreservesFirstAndCachedInitializerFailureAcrossCallbackBoundaries(
        string target)
        => Execute(
            target,
            lto: false,
            generated: false,
            callbackFailure: true);

    private static void Execute(string target, bool lto, bool generated, bool aggregates = false,
        bool aggregateProfile = false, bool arraysUtf8 = false, bool safeHandles = false,
        bool callbacks = false,
        bool namedCallbacks = false,
        bool callbackOnly = false,
        bool callbackFailure = false)
    {
        var providerlessCallbacks = callbackOnly || callbackFailure;
        using var temporary = new TemporaryDirectory();
        using var assets = TestAssets.Create();
        var root = Environment.GetEnvironmentVariable("NETWASM_NATIVE_INTEROP_EVIDENCE_ROOT") ?? temporary.Path;
        var cell = Path.Combine(root, target + (lto ? "-lto" : "-object") + (generated ? "-generated" : "") +
            (aggregateProfile ? "-aggregate-profile" : aggregates ? "-aggregates" : ""));
        if (arraysUtf8)
        {
            cell += "-arrays-utf8";
        }
        if (safeHandles)
        {
            cell += "-safe-handles";
        }
        if (callbacks)
        {
            cell += "-callbacks";
        }
        if (namedCallbacks)
        {
            cell += "-named-callbacks";
        }
        if (callbackOnly)
        {
            cell += "-callback-only";
        }
        if (callbackFailure)
        {
            cell += "-callback-failure";
        }
        Assert.False(Directory.Exists(cell));
        Directory.CreateDirectory(cell);
        var hostTools = RequiredDirectory("NETWASM_NATIVE_INTEROP_HOST_TOOLS_ROOT");
        var toolchain = RequiredDirectory("NETWASM_NATIVE_INTEROP_TOOLCHAIN_ROOT");
        var emsdk = providerlessCallbacks ? null : RequiredDirectory("NETWASM_EMSDK_ROOT");
        var suffix = OperatingSystem.IsWindows() ? ".exe" : "";
        var tools = Path.Combine(hostTools, "tools", "bin");
        var producerTools = providerlessCallbacks
            ? null
            : Path.Combine(emsdk!, "upstream", "bin");
        var commands = new CommandInvoker();
        var nativeSource = providerlessCallbacks
            ? null
            : Path.Combine(
                AppContext.BaseDirectory,
                "IntegrationAssets",
                namedCallbacks ? "named-callback-mule.c" : "native-mule.c");
        var objectPath = Path.Combine(cell, "mule.o");
        var archive = Path.Combine(cell, "libmule.a");
        if (!providerlessCallbacks)
        {
            var compile = ImmutableArray.Create("--target=" + target + "-unknown-emscripten", "-O2", "-ffreestanding",
                "-ffunction-sections", "-fdata-sections", "-c", nativeSource!, "-o", objectPath);
            if (lto) compile = compile.Add("-flto");
            commands.Invoke(new(Path.Combine(producerTools!, "clang" + suffix), compile, Path.Combine(cell, "produce.log")));
            commands.Invoke(new(Path.Combine(producerTools!, "llvm-ar" + suffix), ["rcs", archive, objectPath], Path.Combine(cell, "archive.log")));
        }
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "IntegrationAssets",
            callbackFailure ? "CallbackFailureNativeMule.cs.txt" :
                callbackOnly ? "CallbackOnlyNativeMule.cs.txt" :
                namedCallbacks ? "NamedCallbackNativeMule.cs.txt" :
                callbacks ? "CallbackNativeMule.cs.txt" : safeHandles ? "SafeHandleNativeMule.cs.txt" :
                arraysUtf8 ? "ArrayUtf8NativeMule.cs.txt" :
                aggregateProfile ? "AggregateProfileNativeMule.cs.txt" : aggregates ? "AggregateNativeMule.cs.txt" :
                generated ? "GeneratedNativeMule.cs.txt" : "NativeMule.cs.txt");
        var source = File.ReadAllText(sourcePath);
        var wasmTarget = target == "wasm64" ? WasmTarget.Wasm64 : WasmTarget.Wasm32;
        var runtimeAssets = Path.Combine(assets.Root, "src", "NetWasm.Runtime.Pack", "runtime");
        var coreLib = Path.Combine(cell, "NetWasm.CoreLib.dll");
        File.Copy(assets.CoreLib, coreLib);
        if (nativeSource is not null)
        {
            File.Copy(nativeSource, Path.Combine(cell, "native-mule.c"));
        }
        var inputDirectory = Path.Combine(cell, "inputs");
        Directory.CreateDirectory(inputDirectory);
        foreach (var path in Directory.GetFiles(AppContext.BaseDirectory, "NetWasm.*.dll"))
            File.Copy(path, Path.Combine(inputDirectory, Path.GetFileName(path)));
        File.Copy(Path.Combine(runtimeAssets, "runtime-pack.json"), Path.Combine(inputDirectory, "runtime-pack.json"));
        File.Copy(Path.Combine(runtimeAssets, "runtime-policy.json"), Path.Combine(inputDirectory, "runtime-policy.json"));
        var runtimeWitSource = Path.Combine(assets.Root, "src", "NetWasm.Runtime", "wit");
        var runtimeWit = Path.Combine(inputDirectory, "runtime-wit");
        foreach (var path in Directory.GetFiles(runtimeWitSource, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(runtimeWit, Path.GetRelativePath(runtimeWitSource, path));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(path, copy);
        }
        foreach (var optimizedCil in new[] { false, true })
        {
            var run = Path.Combine(cell, optimizedCil ? "release-cil" : "debug-cil");
            Directory.CreateDirectory(run);
            string assembly;
            try
            {
                assembly = generated
                    ? CompileAndExecuteDesktopOracle(assets.Root, run, sourcePath, nativeSource!, optimizedCil, commands)
                    : optimizedCil
                        ? assets.CompileOptimizedUnsafeSource("NativeMuleOptimized", source)
                        : assets.CompileUnsafeSource("NativeMuleDebug", source);
            }
            catch (RoslynCompilationException error)
            {
                File.WriteAllText(Path.Combine(run, "roslyn-codes.json"), JsonSerializer.Serialize(error.DiagnosticCodes));
                throw;
            }
            var managedInput = Path.Combine(run, "managed-input.dll");
            File.Copy(assembly, managedInput);
            File.Copy(sourcePath, Path.Combine(run, "managed-input.cs"));
            assembly = managedInput;
            var compilation = NetWasmCompiler.Compile(new(assembly, [coreLib], "NativeMule.EntryPoint", "Run", [], wasmTarget,
                DiagnosticTracePath: Path.Combine(run, "compiler-trace.json"), DiagnosticLogPath: Path.Combine(run, "compiler.log"),
                ReferenceAssemblyAliases: generated ? DesktopAliases : null));
            Assert.Equal(providerlessCallbacks ? 0 : namedCallbacks ? 1 : callbacks ? 9 : safeHandles ? 7 : arraysUtf8 ? 7 :
                    aggregateProfile ? 19 : aggregates ? 4 : 14,
                compilation.NativeImports.Length);
            Assert.DoesNotContain(compilation.NativeImports, import => import.LibraryName == "unused");
            var application = Path.Combine(run, "application.wasm");
            File.WriteAllBytes(application, compilation.ApplicationModule);
            var callbackObject = Path.Combine(run, "application.callbacks.o");
            if (callbacks || namedCallbacks || providerlessCallbacks)
            {
                var support = Assert.IsType<WasmNativeCallbackSupportArtifact>(compilation.NativeCallbackSupport);
                Assert.Equal(namedCallbacks || providerlessCallbacks ? 1 : 15, support.Callbacks.Length);
                if (namedCallbacks || providerlessCallbacks)
                {
                    var callback = Assert.Single(support.Callbacks);
                    var exportName = callbackFailure
                        ? "native_mule_callback_failure"
                        : callbackOnly
                            ? "native_mule_callback_only"
                            : "native_mule_managed_entry";
                    Assert.Equal(exportName, callback.NativeSymbol);
                    Assert.Equal(exportName, callback.ApplicationExportName);
                    Assert.Null(callback.RuntimeGetterExportName);
                    Assert.Empty(support.TemporaryApplicationExports);
                    Assert.Empty(support.TemporaryRuntimeExports);
                    Assert.Contains(
                        exportName,
                        WasmModuleInspection.ReadExportNames(compilation.ApplicationModule));
                }
                File.WriteAllBytes(callbackObject, support.ObjectBytes);
            }
            else
            {
                Assert.Null(compilation.NativeCallbackSupport);
            }
            var layout = Path.Combine(run, "runtime-layout.json");
            object layoutEvidence = compilation.NativeCallbackSupport is { } callbackSupport
                ? new
                {
                    schemaVersion = 4,
                    target,
                    applicationStaticDataEnd = compilation.StaticDataEnd,
                    managedExecutableEntryPoint = (object?)null,
                    nativeImports = compilation.NativeImports,
                    nativeCallbackSupport = new
                    {
                        fileName = Path.GetFileName(callbackObject),
                        callbackSupport.Sha256,
                        callbackSupport.Callbacks,
                        callbackSupport.TemporaryApplicationExports,
                        callbackSupport.TemporaryRuntimeExports,
                    },
                }
                : new
                {
                    schemaVersion = 3,
                    target,
                    applicationStaticDataEnd = compilation.StaticDataEnd,
                    managedExecutableEntryPoint = (object?)null,
                    nativeImports = compilation.NativeImports,
                };
            File.WriteAllText(layout, JsonSerializer.Serialize(layoutEvidence, JsonOptions));
            var manifestPath = Path.Combine(run, "interop.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(compilation.InteropManifest, JsonOptions));
            var request = new RuntimeMaterializationRequest(Path.Combine(runtimeAssets, "runtime-pack.json"), layout, runtimeAssets,
                Path.Combine(tools, "wasm-ld" + suffix), Path.Combine(tools, "wasm-opt" + suffix), Path.Combine(tools, "node" + suffix),
                Path.Combine(toolchain, "tools", "wasm-tools", "run-wasm-tools.mjs"),
                Path.Combine(toolchain, "tools", "wasm-tools", "wasm-tools.wasm"),
                Path.Combine(run, "runtime.wasm"), Path.Combine(run, "logs"), Path.Combine(run, "cache"), target,
                RuntimeWasmOptimization.None, 1_048_576, null,
                new("source", "source", "source", "source", "qualified-host-tools", "0.5.0", "pinned", "pinned", "pinned", "pinned"))
            {
                NativeLibraries = providerlessCallbacks
                    ? []
                    : [new("mule", target, archive), new("unused", target, Path.Combine(cell, "missing-unused.a"))],
                NativeCallbackObjectPath = callbacks || namedCallbacks || providerlessCallbacks
                    ? callbackObject
                    : null,
            };
            var runtime = RuntimeMaterializationComposition.Create().Materialize(request);
            if (namedCallbacks || providerlessCallbacks)
            {
                Assert.Empty(runtime.InternalApplicationExports);
            }
            var services = new ServiceCollection().AddNetWasmCompiler();
            services.AddSingleton<IBinaryenToolRunner>(provider => new CapturedNativeBinaryenTools(
                tools, suffix, run, provider.GetRequiredService<IExternalToolRunner>()));
            services.AddSingleton<IWasmTools>(provider => new ProcessWasmTools(
                provider.GetRequiredService<IExternalToolRunner>(), new ExternalToolCommand(
                    Path.Combine(tools, "node" + suffix),
                    ["--disable-warning=ExperimentalWarning",
                        Path.Combine(toolchain, "tools", "wasm-tools", "run-wasm-tools.mjs"),
                        Path.Combine(toolchain, "tools", "wasm-tools", "wasm-tools.wasm")])));
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true, ValidateScopes = true,
            });
            var componentTarget = wasmTarget == WasmTarget.Wasm64
                ? ComponentTarget.Wasm64Wasi02 : ComponentTarget.Wasm32Wasi02;
            var linker = provider.GetRequiredService<IRawModuleLinker>();
            var internalExportsPath = Path.Combine(run, "internal-exports.json");
            var sanitized = Path.Combine(run, "sanitized.wasm");
            var final = Path.Combine(run, "final.wasm");
            var internalRuntimeExports = runtime.InternalRuntimeExports
                .Select(export => new WasmInternalExport(export.Name, export.Kind)).ToImmutableArray();
            var internalApplicationExports = runtime.InternalApplicationExports
                .Select(export => new WasmInternalExport(export.Name, export.Kind)).ToImmutableArray();
            File.WriteAllText(internalExportsPath, JsonSerializer.Serialize(
                internalRuntimeExports.AddRange(internalApplicationExports)));
            linker.Link(new(application, runtime.OutputPath, sanitized, componentTarget, FinalWasmOptimization.None)
            {
                InternalRuntimeExports = internalRuntimeExports,
                InternalApplicationExports = internalApplicationExports,
            });
            linker.Link(new(application, runtime.OutputPath, final, componentTarget, FinalWasmOptimization.Oz)
            {
                InternalRuntimeExports = internalRuntimeExports,
                InternalApplicationExports = internalApplicationExports,
            });
            var wit = Path.Combine(run, "application.wit");
            File.WriteAllText(wit, "package integration:native; world application {}\n");
            var inspector = Path.Combine(assets.Root, "src", "NetWasm.Compiler.ComponentModel", "Raw", "JavaScript",
                "run-raw-module-inspection.mjs");
            var decoder = Path.Combine(toolchain, "tools", "binaryen", "index.js");
            foreach (var module in new[] { sanitized, final })
            {
                var binding = provider.GetRequiredService<IRawBuildImportSourceValidator>().Validate(new(
                    new(compilation.FunctionImports, compilation.InteropManifest), wit, "application", wasmTarget,
                    new(Path.Combine(tools, "node" + suffix), inspector, runtime.OutputPath, decoder),
                    new(Path.Combine(tools, "node" + suffix), inspector, module, decoder))
                { RuntimeWitPath = runtimeWit, RuntimeWorld = "runtime-platform" });
                Assert.Equal(binding.ExpectedImports.Length, binding.ObservedImports.Length);
                File.WriteAllText(module + ".bindings.json", JsonSerializer.Serialize(new
                {
                    binding.ExpectedImports, binding.ObservedImports,
                }, JsonOptions));
                var observation = module + ".observations.json";
                commands.Invoke(new(Path.Combine(tools, "node" + suffix),
                    [Path.Combine(AppContext.BaseDirectory, "IntegrationAssets", "native-mule-runner.mjs"),
                        assets.Root, target, manifestPath, module, observation, internalExportsPath,
                        callbackFailure ? "callback-failure" :
                            callbackOnly ? "callback-only" :
                            namedCallbacks ? "named-callbacks" : callbacks ? "callbacks" : "ordinary"],
                    module + ".execution.log"));
                using var observed = JsonDocument.Parse(File.ReadAllText(observation));
                Assert.Equal(0, observed.RootElement.GetProperty("preview1Imports").GetInt32());
                Assert.Equal(0, observed.RootElement.GetProperty("nativeHostFallbacks").GetInt32());
                var observations = observed.RootElement
                    .GetProperty("observations")
                    .EnumerateArray()
                    .Select(instance => instance
                        .EnumerateArray()
                        .Select(value => value.GetInt32())
                        .ToArray())
                    .ToArray();
                if (callbackFailure)
                {
                    Assert.Equal([0], observations[0]);
                    Assert.Equal([102], observations[1]);
                    Assert.Equal(2, observed.RootElement.GetProperty("fatalReports").GetInt32());
                }
                else
                {
                    Assert.All(observations, instance =>
                        Assert.Equal(
                            callbackOnly
                                ? ExpectedCallbackOnlyInvocationValues
                                : namedCallbacks
                                    ? ExpectedNamedCallbackInvocationValues
                                    : ExpectedInvocationValues,
                            instance));
                }
                if (namedCallbacks || providerlessCallbacks)
                {
                    Assert.Contains(
                        callbackFailure
                            ? "native_mule_callback_failure"
                            : callbackOnly
                                ? "native_mule_callback_only"
                                : "native_mule_managed_entry",
                        WasmModuleInspection.ReadExportNames(File.ReadAllBytes(module)));
                }
            }
            Assert.True(new FileInfo(final).Length < 3 * 1024 * 1024);
            File.WriteAllText(Path.Combine(run, "receipt.json"), JsonSerializer.Serialize(new
            {
                target, lto, optimizedCil, generated, aggregates, aggregateProfile, arraysUtf8,
                safeHandles, callbacks, namedCallbacks, callbackOnly, callbackFailure,
                compilation.NativeImports,
                SourceSha256 = new Sha256ArtifactDigestCalculator().Calculate(sourcePath),
                AssemblySha256 = new Sha256ArtifactDigestCalculator().Calculate(assembly),
                CoreLibSha256 = new Sha256ArtifactDigestCalculator().Calculate(coreLib),
                NativeSourceSha256 = nativeSource is null
                    ? null
                    : new Sha256ArtifactDigestCalculator().Calculate(nativeSource),
                RuntimeManifestSha256 = new Sha256ArtifactDigestCalculator().Calculate(Path.Combine(runtimeAssets, "runtime-pack.json")),
                CompilerAssemblyHashes = Directory.GetFiles(inputDirectory, "NetWasm.*.dll")
                    .Order(StringComparer.Ordinal).ToDictionary(path => Path.GetFileName(path),
                        path => new Sha256ArtifactDigestCalculator().Calculate(path)),
                ToolHashes = ToolNames.ToDictionary(
                    name => name, name => new Sha256ArtifactDigestCalculator().Calculate(Path.Combine(tools, name + suffix))),
                ArchiveSha256 = providerlessCallbacks
                    ? null
                    : new RuntimeNativeArchiveReader().Read(archive),
                Runtime = runtime,
                SanitizedSha256 = new Sha256ArtifactDigestCalculator().Calculate(sanitized),
                FinalSha256 = new Sha256ArtifactDigestCalculator().Calculate(final),
                InputHashes = Directory.GetFiles(inputDirectory, "*", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal).ToDictionary(path => Path.GetRelativePath(inputDirectory, path),
                        path => new Sha256ArtifactDigestCalculator().Calculate(path)),
                BindingHashes = new[] { sanitized, final }.ToDictionary(path => Path.GetFileName(path),
                    path => new Sha256ArtifactDigestCalculator().Calculate(path + ".bindings.json")),
                ProductionRawLinker = true, IndependentRuntimeWitValidated = true,
            }, JsonOptions));
        }
    }

    private static string CompileAndExecuteDesktopOracle(string repository, string run, string source,
        string nativeSource, bool optimizedCil, CommandInvoker commands)
    {
        // Maintainer composition only. Ordinary SDK consumers never need a native compiler.
        var projectDirectory = Path.Combine(run, "desktop-generator");
        Directory.CreateDirectory(projectDirectory);
        var project = Path.Combine(projectDirectory, "NativeGenerator.csproj");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "IntegrationAssets", "NativeGenerator.csproj.txt"), project);
        File.Copy(Path.Combine(repository, "global.json"), Path.Combine(projectDirectory, "global.json"));
        File.Copy(source, Path.Combine(projectDirectory, "NativeMule.cs"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "IntegrationAssets", "NativeDesktopMain.cs.txt"),
            Path.Combine(projectDirectory, "Program.cs"));
        var configuration = optimizedCil ? "Release" : "Debug";
        commands.Invoke(new("dotnet", ["build", project, "-c", configuration,
            "-p:CompilerGeneratedFilesOutputPath=" + Path.Combine(projectDirectory, "obj", "generated")],
            Path.Combine(run, "desktop-build.log")));
        var output = Path.Combine(projectDirectory, "bin", configuration, "net10.0");
        var library = Path.Combine(output, "libmule" + (OperatingSystem.IsMacOS() ? ".dylib" : ".so"));
        commands.Invoke(new("clang", [OperatingSystem.IsMacOS() ? "-dynamiclib" : "-shared", "-fPIC", "-O2",
            nativeSource, "-o", library], Path.Combine(run, "desktop-native.log")));
        var assembly = Path.Combine(output, "NativeGenerator.dll");
        commands.Invoke(new("dotnet", [assembly], Path.Combine(run, "desktop-execution.log")));
        var generatedSources = Directory.GetFiles(Path.Combine(projectDirectory, "obj", "generated"), "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(generatedSources);
        File.WriteAllText(Path.Combine(run, "desktop-receipt.json"), JsonSerializer.Serialize(new
        {
            AssemblySha256 = new Sha256ArtifactDigestCalculator().Calculate(assembly),
            NativeSha256 = new Sha256ArtifactDigestCalculator().Calculate(library),
            GeneratedSourceHashes = generatedSources.Order(StringComparer.Ordinal).ToDictionary(
                path => Path.GetRelativePath(projectDirectory, path), path => new Sha256ArtifactDigestCalculator().Calculate(path)),
            DesktopOraclePassed = true,
        }, JsonOptions));
        return assembly;
    }

    private static string RequiredDirectory(string name)
    {
        var path = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(Directory.Exists(path));
        return path!;
    }

    private sealed class CapturedNativeBinaryenTools(
        string directory, string suffix, string logs, IExternalToolRunner processes) : IBinaryenToolRunner
    {
        private int _invocations;

        public ToolResult Run(string toolId, ImmutableArray<string> arguments)
        {
            Assert.Contains(toolId, BinaryenToolIds.Required);
            var result = processes.Run(Path.Combine(directory, toolId + suffix), arguments);
            var log = Path.Combine(logs, "production-" + toolId + "-" + ++_invocations);
            File.WriteAllText(log + ".stdout.log", result.StandardOutput);
            File.WriteAllText(log + ".stderr.log", result.StandardError);
            return result;
        }
    }
}
