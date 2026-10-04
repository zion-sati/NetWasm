using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Runtime.Pack.Composition;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;
using NetWasm.TestInfrastructure;

namespace NetWasm.Runtime.Pack.Tests.Integration;

public sealed class NativeCallbackCacheIntegrationTests
{
    private const string FirstCallback = "native_mule_callback_first";
    private const string OtherCallback = "native_mule_callback_other";
    private const string CallbackObjectFileName = "application.callbacks.o";
    private static readonly string[] ToolNames = ["node", "wasm-ld", "wasm-opt", "wasm-merge"];
    private static readonly DateTime PreservedTimestamp =
        new(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    [Trait("Category", "NativeCallbackCacheIntegration")]
    public void CompilerCallbackReplacementInvalidatesAndWarmsRuntimeCache(
        string target)
    {
        Assert.Equal(FirstCallback.Length, OtherCallback.Length);
        using var temporary = new TemporaryDirectory();
        using var assets = TestAssets.Create();
        var evidenceRoot = Environment.GetEnvironmentVariable(
            "NETWASM_NATIVE_INTEROP_EVIDENCE_ROOT") ?? temporary.Path;
        var cell = Path.Combine(evidenceRoot, $"{target}-callback-cache");
        Assert.False(Directory.Exists(cell));
        Directory.CreateDirectory(cell);
        var wasmTarget = target == "wasm64" ? WasmTarget.Wasm64 : WasmTarget.Wasm32;
        var first = CompileVariant(assets, cell, "first", target, wasmTarget, FirstCallback);
        var other = CompileVariant(assets, cell, "other", target, wasmTarget, OtherCallback);
        AssertVariantChange(first, other);

        var active = Path.Combine(cell, "active");
        Directory.CreateDirectory(active);
        var activeObject = Path.Combine(active, "application.callbacks.o");
        var activeLayout = Path.Combine(active, "runtime-layout.json");
        Install(first, activeObject, activeLayout);
        File.SetLastWriteTimeUtc(activeObject, PreservedTimestamp);
        File.SetLastWriteTimeUtc(activeLayout, PreservedTimestamp);
        var objectTimestamp = File.GetLastWriteTimeUtc(activeObject);
        var layoutTimestamp = File.GetLastWriteTimeUtc(activeLayout);

        var hostTools = RequiredDirectory("NETWASM_NATIVE_INTEROP_HOST_TOOLS_ROOT");
        var toolchain = RequiredDirectory("NETWASM_NATIVE_INTEROP_TOOLCHAIN_ROOT");
        var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        var tools = Path.Combine(hostTools, "tools", "bin");
        var runtimeAssets = Path.Combine(assets.Root, "src", "NetWasm.Runtime.Pack", "runtime");
        var output = Path.Combine(active, "runtime.wasm");
        var logs = Path.Combine(active, "logs");
        var cache = Path.Combine(active, "cache");
        var request = new RuntimeMaterializationRequest(
            Path.Combine(runtimeAssets, "runtime-pack.json"),
            activeLayout,
            runtimeAssets,
            Path.Combine(tools, "wasm-ld" + suffix),
            Path.Combine(tools, "wasm-opt" + suffix),
            Path.Combine(tools, "node" + suffix),
            Path.Combine(toolchain, "tools", "wasm-tools", "run-wasm-tools.mjs"),
            Path.Combine(toolchain, "tools", "wasm-tools", "wasm-tools.wasm"),
            output,
            logs,
            cache,
            target,
            RuntimeWasmOptimization.Oz,
            1_048_576,
            null,
            new("source", "source", "source", "source", "qualified-host-tools",
                "0.5.0", "pinned", "pinned", "pinned", "pinned"))
        {
            NativeLibraries = [],
            NativeCallbackObjectPath = activeObject,
        };
        var materializer = RuntimeMaterializationComposition.Create();

        var firstCold = materializer.Materialize(request);
        AssertCache(firstCold, RuntimeMaterializationCacheOutcome.Miss, recomputed: true);
        AssertLinkCounts(logs, links: 1, validations: 1);
        var firstRuntime = File.ReadAllBytes(output);
        File.Delete(output);
        var firstWarm = materializer.Materialize(request);
        AssertCache(firstWarm, RuntimeMaterializationCacheOutcome.Hit, recomputed: false);
        Assert.Equal(firstCold.CacheMetrics.KeyPrefix, firstWarm.CacheMetrics.KeyPrefix);
        Assert.Equal(firstRuntime, File.ReadAllBytes(output));
        AssertLinkCounts(logs, links: 1, validations: 2);
        var firstExecution = LinkAndExecute(
            assets, first, firstWarm, output, target, wasmTarget, tools, toolchain, suffix);

        Install(other, activeObject, activeLayout);
        File.SetLastWriteTimeUtc(activeObject, objectTimestamp);
        File.SetLastWriteTimeUtc(activeLayout, layoutTimestamp);
        Assert.Equal(objectTimestamp, File.GetLastWriteTimeUtc(activeObject));
        Assert.Equal(layoutTimestamp, File.GetLastWriteTimeUtc(activeLayout));

        var otherCold = materializer.Materialize(request);
        AssertCache(otherCold, RuntimeMaterializationCacheOutcome.Miss, recomputed: true);
        Assert.NotEqual(firstCold.CacheMetrics.KeyPrefix, otherCold.CacheMetrics.KeyPrefix);
        AssertLinkCounts(logs, links: 2, validations: 3);
        var otherRuntime = File.ReadAllBytes(output);
        File.Delete(output);
        var otherWarm = materializer.Materialize(request);
        AssertCache(otherWarm, RuntimeMaterializationCacheOutcome.Hit, recomputed: false);
        Assert.Equal(otherCold.CacheMetrics.KeyPrefix, otherWarm.CacheMetrics.KeyPrefix);
        Assert.Equal(otherRuntime, File.ReadAllBytes(output));
        AssertLinkCounts(logs, links: 2, validations: 4);
        var otherExecution = LinkAndExecute(
            assets, other, otherWarm, output, target, wasmTarget, tools, toolchain, suffix);

        var digest = new Sha256ArtifactDigestCalculator();
        File.WriteAllText(Path.Combine(cell, "receipt.json"), JsonSerializer.Serialize(new
        {
            target,
            ObjectTimestamp = objectTimestamp,
            LayoutTimestamp = layoutTimestamp,
            ObjectTimestampPreserved = true,
            LayoutTimestampPreserved = true,
            FirstCold = firstCold.CacheMetrics,
            FirstWarm = firstWarm.CacheMetrics,
            OtherCold = otherCold.CacheMetrics,
            OtherWarm = otherWarm.CacheMetrics,
            first.SupportSha256,
            OtherSupportSha256 = other.SupportSha256,
            first.DescriptorSha256,
            OtherDescriptorSha256 = other.DescriptorSha256,
            FirstRuntimeSha256 = Digest(firstRuntime),
            OtherRuntimeSha256 = Digest(otherRuntime),
            FirstFinalSha256 = firstExecution.FinalSha256,
            OtherFinalSha256 = otherExecution.FinalSha256,
            firstExecution.ObservationSha256,
            OtherObservationSha256 = otherExecution.ObservationSha256,
            CompilerAssemblyHashes = Directory.GetFiles(AppContext.BaseDirectory, "NetWasm.*.dll")
                .Order(StringComparer.Ordinal)
                .ToDictionary(path => Path.GetFileName(path), digest.Calculate),
            CoreLibSha256 = digest.Calculate(assets.CoreLib),
            RuntimeManifestSha256 = digest.Calculate(Path.Combine(runtimeAssets, "runtime-pack.json")),
            RuntimePolicySha256 = digest.Calculate(Path.Combine(runtimeAssets, "runtime-policy.json")),
            RunnerSha256 = digest.Calculate(Path.Combine(AppContext.BaseDirectory,
                "IntegrationAssets", "callback-cache-runner.mjs")),
            ToolHashes = ToolNames.ToDictionary(name => name,
                name => digest.Calculate(Path.Combine(tools, name + suffix))),
            WasmToolsSha256 = digest.Calculate(Path.Combine(
                toolchain, "tools", "wasm-tools", "wasm-tools.wasm")),
        }, JsonOptions));
    }

    private static CallbackVariant CompileVariant(
        TestAssets assets,
        string cell,
        string name,
        string target,
        WasmTarget wasmTarget,
        string callbackName)
    {
        var root = Path.Combine(cell, name);
        Directory.CreateDirectory(root);
        var templatePath = Path.Combine(AppContext.BaseDirectory, "IntegrationAssets",
            "CallbackCacheNativeMule.cs.txt");
        var source = File.ReadAllText(templatePath).Replace(
            FirstCallback, callbackName, StringComparison.Ordinal);
        var sourcePath = Path.Combine(root, "managed-input.cs");
        File.WriteAllText(sourcePath, source);
        var compiled = assets.CompileOptimizedUnsafeSource(
            "NativeCallbackCacheFixture", source);
        var assembly = Path.Combine(root, "managed-input.dll");
        File.Copy(compiled, assembly);
        var compilation = NetWasmCompiler.Compile(new(
            assembly,
            [assets.CoreLib],
            "NativeMule.EntryPoint",
            "Run",
            [],
            wasmTarget));
        Assert.Empty(compilation.NativeImports);
        var support = Assert.IsType<WasmNativeCallbackSupportArtifact>(
            compilation.NativeCallbackSupport);
        var callback = Assert.Single(support.Callbacks);
        Assert.Equal(callbackName, callback.NativeSymbol);
        Assert.Equal(callbackName, callback.ApplicationExportName);
        Assert.Null(callback.RuntimeGetterExportName);
        Assert.Empty(support.TemporaryApplicationExports);
        Assert.Empty(support.TemporaryRuntimeExports);
        Assert.Contains(callbackName,
            WasmModuleInspection.ReadExportNames(compilation.ApplicationModule));
        var applicationPath = Path.Combine(root, "application.wasm");
        File.WriteAllBytes(applicationPath, compilation.ApplicationModule);
        var objectPath = Path.Combine(root, CallbackObjectFileName);
        File.WriteAllBytes(objectPath, support.ObjectBytes);
        var layout = JsonSerializer.Serialize(new
        {
            schemaVersion = 4,
            target,
            applicationStaticDataEnd = compilation.StaticDataEnd,
            managedExecutableEntryPoint = (object?)null,
            nativeImports = compilation.NativeImports,
            nativeCallbackSupport = new
            {
                fileName = CallbackObjectFileName,
                support.Sha256,
                support.Callbacks,
                support.TemporaryApplicationExports,
                support.TemporaryRuntimeExports,
            },
        }, JsonOptions);
        var layoutPath = Path.Combine(root, "runtime-layout.json");
        File.WriteAllText(layoutPath, layout);
        var manifest = JsonSerializer.Serialize(compilation.InteropManifest, JsonOptions);
        var manifestPath = Path.Combine(root, "interop.json");
        File.WriteAllText(manifestPath, manifest);
        var digest = new Sha256ArtifactDigestCalculator();
        var descriptor = JsonSerializer.Serialize(support.Callbacks, JsonOptions);
        var result = new CallbackVariant(
            root,
            callbackName,
            applicationPath,
            compilation.ApplicationModule,
            CallbackObjectFileName,
            support.ObjectBytes,
            support.Sha256,
            callback,
            Digest(System.Text.Encoding.UTF8.GetBytes(descriptor)),
            compilation.StaticDataEnd,
            JsonSerializer.Serialize(compilation.FunctionImports, JsonOptions),
            layout,
            manifest,
            digest.Calculate(assembly),
            digest.Calculate(sourcePath));
        File.WriteAllText(Path.Combine(root, "receipt.json"), JsonSerializer.Serialize(new
        {
            target,
            callbackName,
            result.AssemblySha256,
            result.SourceSha256,
            result.SupportSha256,
            result.DescriptorSha256,
            result.StaticDataEnd,
            ApplicationSha256 = digest.Calculate(applicationPath),
            CallbackObjectBytes = result.CallbackObject.Length,
            LayoutBytes = System.Text.Encoding.UTF8.GetByteCount(result.Layout),
        }, JsonOptions));
        return result;
    }

    private static void AssertVariantChange(
        CallbackVariant first,
        CallbackVariant other)
    {
        Assert.Equal(first.CallbackObjectFileName, other.CallbackObjectFileName);
        Assert.Equal(first.StaticDataEnd, other.StaticDataEnd);
        Assert.Equal(first.FunctionImports, other.FunctionImports);
        Assert.Equal(first.Callback.Parameters.ToArray(), other.Callback.Parameters.ToArray());
        Assert.Equal(first.Callback.ReturnType, other.Callback.ReturnType);
        Assert.Equal(first.Callback.RuntimeImportSymbol, other.Callback.RuntimeImportSymbol);
        Assert.Equal(first.Callback.RuntimeGetterExportName, other.Callback.RuntimeGetterExportName);
        Assert.Equal(first.CallbackObject.Length, other.CallbackObject.Length);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(first.Layout),
            System.Text.Encoding.UTF8.GetByteCount(other.Layout));
        Assert.NotEqual(first.Callback.NativeSymbol, other.Callback.NativeSymbol);
        Assert.NotEqual(first.Callback.ApplicationExportName,
            other.Callback.ApplicationExportName);
        Assert.NotEqual(first.SupportSha256, other.SupportSha256);
        Assert.NotEqual(first.DescriptorSha256, other.DescriptorSha256);
        Assert.False(first.Application.SequenceEqual(other.Application));
        Assert.NotEqual(first.AssemblySha256, other.AssemblySha256);
    }

    private static void Install(
        CallbackVariant variant,
        string objectPath,
        string layoutPath)
    {
        File.WriteAllBytes(objectPath, variant.CallbackObject);
        File.WriteAllText(layoutPath, variant.Layout);
    }

    private static void AssertCache(
        RuntimeMaterialization result,
        RuntimeMaterializationCacheOutcome outcome,
        bool recomputed)
    {
        Assert.Equal(outcome, result.CacheMetrics.Outcome);
        Assert.Equal(recomputed, result.CacheMetrics.Recomputed);
    }

    private static void AssertLinkCounts(
        string logs,
        int links,
        int validations)
    {
        Assert.Equal(links, Directory.EnumerateFiles(
            logs, "native-probe-link.log", SearchOption.AllDirectories).Count());
        Assert.Equal(links, Directory.EnumerateFiles(
            logs, "native-final-link.log", SearchOption.AllDirectories).Count());
        Assert.Equal(validations, Directory.EnumerateFiles(
            logs, "*validate.log", SearchOption.AllDirectories).Count());
    }

    private static ExecutionResult LinkAndExecute(
        TestAssets assets,
        CallbackVariant variant,
        RuntimeMaterialization runtime,
        string runtimePath,
        string target,
        WasmTarget wasmTarget,
        string tools,
        string toolchain,
        string suffix)
    {
        var services = new ServiceCollection().AddNetWasmCompiler();
        services.AddSingleton<IBinaryenToolRunner>(provider => new CapturedBinaryenTools(
            tools, suffix, variant.Root, provider.GetRequiredService<IExternalToolRunner>()));
        services.AddSingleton<IWasmTools>(provider => new ProcessWasmTools(
            provider.GetRequiredService<IExternalToolRunner>(), new ExternalToolCommand(
                Path.Combine(tools, "node" + suffix),
                ["--disable-warning=ExperimentalWarning",
                    Path.Combine(toolchain, "tools", "wasm-tools", "run-wasm-tools.mjs"),
                    Path.Combine(toolchain, "tools", "wasm-tools", "wasm-tools.wasm")])));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        var componentTarget = wasmTarget == WasmTarget.Wasm64
            ? ComponentTarget.Wasm64Wasi02
            : ComponentTarget.Wasm32Wasi02;
        var internalRuntimeExports = runtime.InternalRuntimeExports
            .Select(export => new WasmInternalExport(export.Name, export.Kind))
            .ToImmutableArray();
        var internalApplicationExports = runtime.InternalApplicationExports
            .Select(export => new WasmInternalExport(export.Name, export.Kind))
            .ToImmutableArray();
        var output = Path.Combine(variant.Root, "final.wasm");
        provider.GetRequiredService<IRawModuleLinker>().Link(new(
            variant.ApplicationPath,
            runtimePath,
            output,
            componentTarget,
            FinalWasmOptimization.Oz)
        {
            InternalRuntimeExports = internalRuntimeExports,
            InternalApplicationExports = internalApplicationExports,
        });
        var exports = WasmModuleInspection.ReadExportNames(File.ReadAllBytes(output));
        Assert.Contains(variant.CallbackName, exports);
        Assert.DoesNotContain(
            internalRuntimeExports.AddRange(internalApplicationExports)
                .Select(export => export.Name),
            exports.Contains);
        var observation = output + ".observations.json";
        new CommandInvoker().Invoke(new(
            Path.Combine(tools, "node" + suffix),
            [Path.Combine(AppContext.BaseDirectory, "IntegrationAssets", "callback-cache-runner.mjs"),
                assets.Root, target, Path.Combine(variant.Root, "interop.json"), output,
                observation, variant.CallbackName],
            output + ".execution.log"));
        var digest = new Sha256ArtifactDigestCalculator();
        return new(digest.Calculate(output), digest.Calculate(observation));
    }

    private static string RequiredDirectory(string name)
    {
        var path = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(Directory.Exists(path));
        return path!;
    }

    private static string Digest(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class CapturedBinaryenTools(
        string directory,
        string suffix,
        string logs,
        IExternalToolRunner processes) : IBinaryenToolRunner
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

    private sealed record CallbackVariant(
        string Root,
        string CallbackName,
        string ApplicationPath,
        byte[] Application,
        string CallbackObjectFileName,
        byte[] CallbackObject,
        string SupportSha256,
        WasmNativeCallbackDescriptor Callback,
        string DescriptorSha256,
        int StaticDataEnd,
        string FunctionImports,
        string Layout,
        string Manifest,
        string AssemblySha256,
        string SourceSha256);

    private sealed record ExecutionResult(
        string FinalSha256,
        string ObservationSha256);
}
