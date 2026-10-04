using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.ManagedExecutables;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Runtime.Pack.Composition;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;
using NetWasm.TestInfrastructure;

namespace NetWasm.Runtime.Pack.Tests.Integration;

public sealed class NativeCallbackDceIntegrationTests
{
    private static readonly string[] ToolNames = ["node", "wasm-opt", "wasm-merge"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    [Trait("Category", "NativeCallbackDceIntegration")]
    public void UnreachableCallbackDoesNotChangeHello42Outputs(string target)
    {
        using var temporary = new TemporaryDirectory();
        using var assets = TestAssets.Create();
        var evidenceRoot = Environment.GetEnvironmentVariable(
            "NETWASM_NATIVE_INTEROP_EVIDENCE_ROOT") ?? temporary.Path;
        var cell = Path.Combine(evidenceRoot, $"{target}-unused-callback-dce");
        Assert.False(Directory.Exists(cell));
        Directory.CreateDirectory(cell);

        var hostTools = RequiredDirectory("NETWASM_NATIVE_INTEROP_HOST_TOOLS_ROOT");
        var toolchain = RequiredDirectory("NETWASM_NATIVE_INTEROP_TOOLCHAIN_ROOT");
        var wasmTarget = target == "wasm64" ? WasmTarget.Wasm64 : WasmTarget.Wasm32;
        var baseline = BuildVariant(
            assets, cell, "baseline", target, wasmTarget, hostTools, toolchain,
            includeUnusedCallback: false);
        var unused = BuildVariant(
            assets, cell, "unused-callback", target, wasmTarget, hostTools, toolchain,
            includeUnusedCallback: true);

        Assert.NotEqual(baseline.AssemblySha256, unused.AssemblySha256);
        Assert.Equal(baseline.EntryPointToken, unused.EntryPointToken);
        Assert.Equal(baseline.StaticDataEnd, unused.StaticDataEnd);
        Assert.Equal(baseline.FunctionImports, unused.FunctionImports);
        Assert.Equal(baseline.InteropManifest, unused.InteropManifest);
        Assert.Equal(baseline.Application, unused.Application);
        Assert.Equal(baseline.Runtime, unused.Runtime);
        Assert.Equal(baseline.RawLinked, unused.RawLinked);
        Assert.Equal(baseline.OptimizedLinked, unused.OptimizedLinked);

        var digest = new Sha256ArtifactDigestCalculator();
        var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        var tools = Path.Combine(hostTools, "tools", "bin");
        File.WriteAllText(Path.Combine(cell, "receipt.json"), JsonSerializer.Serialize(new
        {
            target,
            SameEntryPointToken = true,
            SameStaticDataEnd = true,
            SameFunctionImports = true,
            SameInteropManifest = true,
            SameApplicationBytes = true,
            SameRuntimeBytes = true,
            SameRawLinkedBytes = true,
            SameOptimizedLinkedBytes = true,
            baseline.ApplicationSha256,
            baseline.RuntimeSha256,
            baseline.RawLinkedSha256,
            baseline.OptimizedLinkedSha256,
            baseline.ApplicationBytes,
            baseline.RuntimeBytes,
            baseline.RawLinkedBytes,
            baseline.OptimizedLinkedBytes,
            CoreLibSha256 = digest.Calculate(assets.CoreLib),
            CompilerAssemblyHashes = Directory.GetFiles(AppContext.BaseDirectory, "NetWasm.*.dll")
                .Order(StringComparer.Ordinal)
                .ToDictionary(path => Path.GetFileName(path), digest.Calculate),
            ToolHashes = ToolNames
                .ToDictionary(name => name,
                    name => digest.Calculate(Path.Combine(tools, name + suffix))),
            WasmToolsSha256 = digest.Calculate(Path.Combine(
                toolchain, "tools", "wasm-tools", "wasm-tools.wasm")),
            RuntimeManifestSha256 = digest.Calculate(Path.Combine(
                assets.Root, "src", "NetWasm.Runtime.Pack", "runtime", "runtime-pack.json")),
            RuntimePolicySha256 = digest.Calculate(Path.Combine(
                assets.Root, "src", "NetWasm.Runtime.Pack", "runtime", "runtime-policy.json")),
            RunnerSha256 = digest.Calculate(Path.Combine(
                AppContext.BaseDirectory, "IntegrationAssets", "hello42-dce-runner.mjs")),
        }, JsonOptions));
    }

    private static VariantResult BuildVariant(
        TestAssets assets,
        string cell,
        string name,
        string target,
        WasmTarget wasmTarget,
        string hostTools,
        string toolchain,
        bool includeUnusedCallback)
    {
        var root = Path.Combine(cell, name);
        Directory.CreateDirectory(root);
        var commands = new CommandInvoker();
        var hello42 = Path.Combine(root, "Hello42.cs");
        File.Copy(Path.Combine(assets.Root, "eng", "size-canary", "Hello42.cs"), hello42);
        var sources = new List<string> { hello42 };
        if (includeUnusedCallback)
        {
            var unused = Path.Combine(root, "UnusedCallback.cs");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "IntegrationAssets",
                "UnusedCallbackHello42.cs.txt"), unused);
            sources.Add(unused);
        }

        var assembly = Path.Combine(root, "NetWasmApp.dll");
        CompileExecutable(assets, commands, assembly, sources, Path.Combine(root, "csc.log"));
        var assemblyBytes = File.ReadAllBytes(assembly);
        AssertUnusedCallbackMetadata(assemblyBytes, includeUnusedCallback);
        var entryPoint = new ManagedExecutableEntryPointSelector().SelectEntryPoint(
            assembly, assemblyBytes);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            entryPoint.TypeName,
            entryPoint.MethodName,
            [],
            wasmTarget,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform@1.0.0/platform",
            EntryMethodToken: entryPoint.MetadataToken,
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable));
        Assert.Null(compilation.NativeCallbackSupport);
        Assert.Empty(compilation.NativeImports);
        Assert.Empty(compilation.Program.NativeCallbacks);
        Assert.DoesNotContain(compilation.Program.Methods.Values,
            method => method.Method.Definition.Name == "NeverCalled");

        var applicationPath = Path.Combine(root, "application.wasm");
        File.WriteAllBytes(applicationPath, compilation.ApplicationModule);
        var layout = Path.Combine(root, "runtime-layout.json");
        File.WriteAllText(layout, JsonSerializer.Serialize(new
        {
            schemaVersion = 3,
            target,
            applicationStaticDataEnd = compilation.StaticDataEnd,
            managedExecutableEntryPoint = (object?)null,
            nativeImports = compilation.NativeImports,
        }, JsonOptions));
        var manifest = Path.Combine(root, "interop.json");
        var interopManifest = JsonSerializer.Serialize(compilation.InteropManifest, JsonOptions);
        File.WriteAllText(manifest, interopManifest);

        var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        var tools = Path.Combine(hostTools, "tools", "bin");
        var runtimeAssets = Path.Combine(assets.Root, "src", "NetWasm.Runtime.Pack", "runtime");
        var runtime = RuntimeMaterializationComposition.Create().Materialize(
            new RuntimeMaterializationRequest(
                Path.Combine(runtimeAssets, "runtime-pack.json"),
                layout,
                runtimeAssets,
                Path.Combine(tools, "wasm-ld" + suffix),
                Path.Combine(tools, "wasm-opt" + suffix),
                Path.Combine(tools, "node" + suffix),
                Path.Combine(toolchain, "tools", "wasm-tools", "run-wasm-tools.mjs"),
                Path.Combine(toolchain, "tools", "wasm-tools", "wasm-tools.wasm"),
                Path.Combine(root, "runtime.wasm"),
                Path.Combine(root, "runtime-logs"),
                Path.Combine(root, "runtime-cache"),
                target,
                RuntimeWasmOptimization.None,
                1_048_576,
                null,
                new("source", "source", "source", "source", "qualified-host-tools",
                    "0.5.0", "pinned", "pinned", "pinned", "pinned")));

        var services = new ServiceCollection().AddNetWasmCompiler();
        services.AddSingleton<IBinaryenToolRunner>(provider => new CapturedBinaryenTools(
            tools, suffix, root, provider.GetRequiredService<IExternalToolRunner>()));
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
        var linker = provider.GetRequiredService<IRawModuleLinker>();
        var componentTarget = wasmTarget == WasmTarget.Wasm64
            ? ComponentTarget.Wasm64Wasi02
            : ComponentTarget.Wasm32Wasi02;
        var internalRuntimeExports = runtime.InternalRuntimeExports
            .Select(export => new WasmInternalExport(export.Name, export.Kind))
            .ToImmutableArray();
        var internalApplicationExports = runtime.InternalApplicationExports
            .Select(export => new WasmInternalExport(export.Name, export.Kind))
            .ToImmutableArray();
        var rawPath = Path.Combine(root, "linked.wasm");
        var optimizedPath = Path.Combine(root, "linked-Oz.wasm");
        Link(linker, applicationPath, runtime.OutputPath, rawPath, componentTarget,
            FinalWasmOptimization.None, internalRuntimeExports, internalApplicationExports);
        Link(linker, applicationPath, runtime.OutputPath, optimizedPath, componentTarget,
            FinalWasmOptimization.Oz, internalRuntimeExports, internalApplicationExports);

        var internalNames = internalRuntimeExports.AddRange(internalApplicationExports)
            .Select(export => export.Name)
            .ToArray();
        foreach (var output in new[] { rawPath, optimizedPath })
        {
            var exports = WasmModuleInspection.ReadExportNames(File.ReadAllBytes(output));
            Assert.DoesNotContain(internalNames, exports.Contains);
            commands.Invoke(new RuntimeCommand(
                Path.Combine(tools, "node" + suffix),
                [Path.Combine(AppContext.BaseDirectory, "IntegrationAssets", "hello42-dce-runner.mjs"),
                    assets.Root, target, manifest, output, output + ".observations.json"],
                output + ".execution.log"));
        }

        var digest = new Sha256ArtifactDigestCalculator();
        var application = File.ReadAllBytes(applicationPath);
        var runtimeBytes = File.ReadAllBytes(runtime.OutputPath);
        var raw = File.ReadAllBytes(rawPath);
        var optimized = File.ReadAllBytes(optimizedPath);
        var functionImports = JsonSerializer.Serialize(compilation.FunctionImports, JsonOptions);
        var result = new VariantResult(
            entryPoint.MetadataToken,
            compilation.StaticDataEnd,
            digest.Calculate(assembly),
            functionImports,
            interopManifest,
            application,
            runtimeBytes,
            raw,
            optimized,
            digest.Calculate(applicationPath),
            digest.Calculate(runtime.OutputPath),
            digest.Calculate(rawPath),
            digest.Calculate(optimizedPath));
        File.WriteAllText(Path.Combine(root, "receipt.json"), JsonSerializer.Serialize(new
        {
            target,
            includeUnusedCallback,
            result.EntryPointToken,
            result.StaticDataEnd,
            result.AssemblySha256,
            result.ApplicationSha256,
            result.RuntimeSha256,
            result.RawLinkedSha256,
            result.OptimizedLinkedSha256,
            result.ApplicationBytes,
            result.RuntimeBytes,
            result.RawLinkedBytes,
            result.OptimizedLinkedBytes,
            NativeImports = 0,
            NativeCallbacks = 0,
            NativeCallbackSupport = false,
            SourceSha256 = digest.Calculate(hello42),
            UnusedCallbackSourceSha256 = includeUnusedCallback
                ? digest.Calculate(sources[1])
                : null,
        }, JsonOptions));
        return result;
    }

    private static void Link(
        IRawModuleLinker linker,
        string application,
        string runtime,
        string output,
        ComponentTarget target,
        FinalWasmOptimization optimization,
        ImmutableArray<WasmInternalExport> internalRuntimeExports,
        ImmutableArray<WasmInternalExport> internalApplicationExports) =>
        linker.Link(new(application, runtime, output, target, optimization)
        {
            InternalRuntimeExports = internalRuntimeExports,
            InternalApplicationExports = internalApplicationExports,
        });

    private static void CompileExecutable(
        TestAssets assets,
        CommandInvoker commands,
        string output,
        IReadOnlyList<string> sources,
        string log)
    {
        using var global = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(assets.Root, "global.json")));
        var sdkVersion = global.RootElement.GetProperty("sdk").GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sdkVersion));
        var dotnet = ResolveDotNetHost();
        var csc = Path.Combine(Path.GetDirectoryName(dotnet)!, "sdk", sdkVersion!,
            "Roslyn", "bincore", "csc.dll");
        Assert.True(File.Exists(csc));
        var arguments = ImmutableArray.CreateBuilder<string>();
        arguments.Add(csc);
        arguments.AddRange(new[]
        {
            "-nologo",
            "-noconfig",
            "-nostdlib",
            "-langversion:latest",
            "-define:NETWASM_REF_STRUCT_GENERICS;NETWASM_REGEX_STRING_CREATE;SYSTEM_TEXT_REGULAREXPRESSIONS",
            "-deterministic+",
            "-optimize+",
            "-target:exe",
            "-runtimemetadataversion:v4.0.30319",
            "-reference:" + assets.CoreLib,
            "-out:" + output,
        });
        arguments.AddRange(sources);
        commands.Invoke(new(dotnet, arguments.ToImmutable(), log));
    }

    private static void AssertUnusedCallbackMetadata(
        byte[] assembly,
        bool expected)
    {
        using var stream = new MemoryStream(assembly, writable: false);
        using var portableExecutable = new PEReader(stream);
        var metadata = portableExecutable.GetMetadataReader();
        var methods = metadata.TypeDefinitions
            .SelectMany(handle => metadata.GetTypeDefinition(handle).GetMethods())
            .Where(handle => metadata.GetString(
                metadata.GetMethodDefinition(handle).Name) == "NeverCalled")
            .ToArray();
        if (!expected)
        {
            Assert.Empty(methods);
            return;
        }

        var method = metadata.GetMethodDefinition(Assert.Single(methods));
        Assert.Contains(method.GetCustomAttributes(), handle =>
            IsUnmanagedCallersOnly(metadata, metadata.GetCustomAttribute(handle)));
    }

    private static bool IsUnmanagedCallersOnly(
        MetadataReader metadata,
        CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
        {
            return false;
        }
        var constructor = metadata.GetMemberReference(
            (MemberReferenceHandle)attribute.Constructor);
        return constructor.Parent.Kind switch
        {
            HandleKind.TypeReference => metadata.GetString(metadata.GetTypeReference(
                (TypeReferenceHandle)constructor.Parent).Name) ==
                "UnmanagedCallersOnlyAttribute",
            HandleKind.TypeDefinition => metadata.GetString(metadata.GetTypeDefinition(
                (TypeDefinitionHandle)constructor.Parent).Name) ==
                "UnmanagedCallersOnlyAttribute",
            _ => false,
        };
    }

    private static string ResolveDotNetHost()
    {
        var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var configured = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return ResolveLink(configured);
        }
        if (Environment.ProcessPath is { } processPath &&
            string.Equals(Path.GetFileName(processPath), executable,
                StringComparison.OrdinalIgnoreCase))
        {
            return ResolveLink(processPath);
        }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return ResolveLink(candidate);
            }
        }
        throw new InvalidOperationException("dotnet host path is unavailable");
    }

    private static string ResolveLink(string path) =>
        File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? Path.GetFullPath(path);

    private static string RequiredDirectory(string name)
    {
        var path = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(Directory.Exists(path));
        return path!;
    }

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

    private sealed record VariantResult(
        int EntryPointToken,
        int StaticDataEnd,
        string AssemblySha256,
        string FunctionImports,
        string InteropManifest,
        byte[] Application,
        byte[] Runtime,
        byte[] RawLinked,
        byte[] OptimizedLinked,
        string ApplicationSha256,
        string RuntimeSha256,
        string RawLinkedSha256,
        string OptimizedLinkedSha256)
    {
        public int ApplicationBytes => Application.Length;
        public int RuntimeBytes => Runtime.Length;
        public int RawLinkedBytes => RawLinked.Length;
        public int OptimizedLinkedBytes => OptimizedLinked.Length;
    }
}
