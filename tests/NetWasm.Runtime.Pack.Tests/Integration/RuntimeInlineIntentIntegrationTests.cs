using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Runtime.Pack.Composition;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Integration;

public sealed class RuntimeInlineIntentIntegrationTests
{
    private static readonly string[] ExpectedExports =
        ["root_frame_enter", "root_frame_leave", "value_frame_enter", "value_frame_enter_mapped", "value_frame_leave",
         "value_frame_leave_mapped"];

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    [Trait("Category", "RuntimeInlineIntentIntegration")]
    public void MaterializedRuntimeCarriesOnlyDeclaredAggressiveFunctionHints(string target)
    {
        using var temporary = new TemporaryDirectory();
        var evidenceRoot = Environment.GetEnvironmentVariable(
            "NETWASM_INLINE_INTENT_EVIDENCE_ROOT") ?? temporary.Path;
        var cell = Path.Combine(evidenceRoot, target);
        Assert.False(Directory.Exists(cell));
        Directory.CreateDirectory(cell);
        var outputPath = Path.Combine(cell, "runtime.wasm");
        var request = CreateRequest(target, outputPath, cell, temporary);
        var manifestPath = request.ManifestPath;
        var manifest = new RuntimePackManifestReader(new RuntimePackManifestParser()).Read(manifestPath);

        var materializer = RuntimeMaterializationComposition.Create();
        var cold = materializer.Materialize(request);
        var coldBytes = File.ReadAllBytes(outputPath);
        var evidence = ReadEvidence(coldBytes);
        var warm = materializer.Materialize(request);

        Assert.True(cold.CacheMetrics.Recomputed);
        Assert.Equal(RuntimeMaterializationCacheOutcome.Miss, cold.CacheMetrics.Outcome);
        Assert.False(warm.CacheMetrics.Recomputed);
        Assert.Equal(RuntimeMaterializationCacheOutcome.Hit, warm.CacheMetrics.Outcome);
        Assert.Equal(cold.Sha256, warm.Sha256);
        Assert.Equal(coldBytes, File.ReadAllBytes(outputPath));
        Assert.Equal(
            ExpectedExports,
            manifest.InlineIntents.Select(intent => intent.Export));
        Assert.All(manifest.InlineIntents, intent =>
            Assert.Equal(RuntimeFunctionInlinePreference.Aggressive, intent.Preference));
        var expectedIndices = ExpectedExports
            .Select(name => evidence.Exports[name])
            .Order()
            .ToArray();
        Assert.Equal(expectedIndices, evidence.AggressiveFunctions.Order());
    }

    [Theory]
    [InlineData("wasm32", FinalWasmOptimization.O3, ManagedInliningHints.IgnoreAggressive)]
    [InlineData("wasm64", FinalWasmOptimization.O3, ManagedInliningHints.IgnoreAggressive)]
    [InlineData("wasm32", FinalWasmOptimization.Oz, ManagedInliningHints.Preserve)]
    [InlineData("wasm64", FinalWasmOptimization.Oz, ManagedInliningHints.Preserve)]
    [Trait("Category", "RuntimeInlineIntentFinalLinkIntegration")]
    public void FinalLinkSelectsRuntimeAndManagedHintsIndependently(
        string target,
        FinalWasmOptimization optimization,
        ManagedInliningHints managedHints)
    {
        using var temporary = new TemporaryDirectory();
        var evidenceRoot = Environment.GetEnvironmentVariable(
            "NETWASM_INLINE_INTENT_EVIDENCE_ROOT") ?? temporary.Path;
        var cell = Path.Combine(evidenceRoot,
            $"final-link-{target}-{optimization}-{managedHints}");
        Assert.False(Directory.Exists(cell));
        Directory.CreateDirectory(cell);
        var runtimePath = Path.Combine(cell, "runtime.wasm");
        RuntimeMaterializationComposition.Create().Materialize(
            CreateRequest(target, runtimePath, cell, temporary));
        var applicationPath = Path.Combine(cell, "application.wasm");
        var environmentPath = Path.Combine(cell, "environment.wasm");
        File.WriteAllBytes(applicationPath, Module(
            "application_inline_candidate", InlinePreference.Aggressive));
        File.WriteAllBytes(environmentPath, EnvironmentModule(target));

        var settings = new WasmOptimizationSettingsResolver().Resolve(
            optimization,
            new(managedHints));
        var editor = new WasmCoreModuleInlineAnnotationEditor(
            new SystemByteFileReader(),
            new SystemByteFileWriter(),
            new WasmInlineAnnotationFilter());
        var request = new ComponentCoreModuleMergeRequest(
            applicationPath,
            runtimePath,
            environmentPath,
            Path.Combine(cell, "merged.wasm"),
            target == "wasm64" ? ComponentTarget.Wasm64Wasi02 : ComponentTarget.Wasm32Wasi02)
        {
            ApplicationInlineAnnotations = settings.ManagedInlineAnnotations,
            RuntimeInlineAnnotations = settings.RuntimeInlineAnnotations,
            Intermediates = new(
                Path.Combine(cell, "application-sanitized.wasm"),
                Path.Combine(cell, "runtime-sanitized.wasm"),
                Path.Combine(cell, "merged-unfiltered.wasm")),
        };

        // The synthetic application exports only its own candidate, which wasm-merge leaves the
        // runtime's other imports beside; the SDK's merger requires every import an input names.
        // The stepwise merge is the browser link plan's, which runs the toolchain's portable
        // Binaryen.
        var hostTools = RequiredDirectory("NETWASM_NATIVE_INTEROP_HOST_TOOLS_ROOT");
        var toolchain = RequiredDirectory("NETWASM_NATIVE_INTEROP_TOOLCHAIN_ROOT");
        var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        var tools = new ProcessBinaryenToolRunner(new ProcessExternalToolRunner(new BoundProcessExecution(
            Path.Combine(hostTools, "tools", "bin", "node" + suffix),
            Path.Combine(toolchain, "tools", "binaryen", "bin", "wasm-merge"))));
        new ComponentCoreModuleMergeRunner(tools, editor,
            new WasmCoreModuleExportEditor(new SystemFileExistence(), new SystemByteFileReader(),
                new SystemByteFileWriter())).Run(request);
        var mergedPath = request.OutputPath;

        var evidence = ReadEvidence(File.ReadAllBytes(mergedPath));
        var expectedExports = optimization == FinalWasmOptimization.O3
            ? ExpectedExports
            : ["application_inline_candidate"];
        var expectedIndices = expectedExports
            .Select(name => evidence.Exports[name])
            .Order()
            .ToArray();
        Assert.Equal(expectedIndices, evidence.AggressiveFunctions.Order());
    }

    private static RuntimeMaterializationRequest CreateRequest(
        string target,
        string outputPath,
        string cell,
        TemporaryDirectory temporary)
    {
        var repository = RepositoryRoot();
        var assetRoot = Path.Combine(repository, "src", "NetWasm.Runtime.Pack", "runtime");
        var hostTools = RequiredDirectory("NETWASM_NATIVE_INTEROP_HOST_TOOLS_ROOT");
        var toolchain = RequiredDirectory("NETWASM_NATIVE_INTEROP_TOOLCHAIN_ROOT");
        var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        var layoutPath = temporary.Write("runtime-layout.json", JsonSerializer.Serialize(new
        {
            schemaVersion = 3,
            target,
            applicationStaticDataEnd = 65_537,
            runtimeFeatures = Array.Empty<string>(),
            nativeImports = Array.Empty<object>(),
        }));
        return new(
            Path.Combine(assetRoot, "runtime-pack.json"),
            layoutPath,
            assetRoot,
            Path.Combine(hostTools, "tools", "bin", "wasm-ld" + suffix),
            Path.Combine(hostTools, "tools", "bin", "wasm-opt" + suffix),
            Path.Combine(hostTools, "tools", "bin", "node" + suffix),
            Path.Combine(toolchain, "tools", "wasm-tools", "run-wasm-tools.mjs"),
            Path.Combine(toolchain, "tools", "wasm-tools", "wasm-tools.wasm"),
            outputPath,
            Path.Combine(cell, "logs"),
            Path.Combine(cell, "cache"),
            target,
            RuntimeWasmOptimization.O3,
            null,
            null,
            new(
                "source-test",
                "source-test",
                "source-test",
                "source-test",
                "qualified-host-tools",
                "0.7.1",
                "emscripten-6.0.7",
                "binaryen-132",
                "qualified-wasm-tools",
                "qualified-node"))
        {
            GarbageCollector = "Compact",
        };
    }

    private static byte[] Module(string exportName, InlinePreference preference)
    {
        using var module = new MemoryStream();
        module.Write([0, 97, 115, 109, 1, 0, 0, 0]);
        Section(module, 1, section =>
        {
            Unsigned(section, 1);
            section.Write([0x60, 0, 0]);
        });
        Section(module, 3, section => section.Write([1, 0]));
        Section(module, 7, section =>
        {
            Unsigned(section, 1);
            Name(section, exportName);
            section.Write([0, 0]);
        });
        Section(module, 10, section => section.Write([1, 2, 0, 0x0b]));
        Section(module, 0, section =>
        {
            Name(section, "binaryen.inline");
            Unsigned(section, 1);
            Unsigned(section, 0);
            Unsigned(section, 1);
            Unsigned(section, 0);
            Unsigned(section, 1);
            section.WriteByte(preference == InlinePreference.Aggressive ? (byte)127 : (byte)0);
        });
        return module.ToArray();
    }

    private static byte[] EnvironmentModule(string target)
    {
        using var module = new MemoryStream();
        module.Write([0, 97, 115, 109, 1, 0, 0, 0]);
        Section(module, 1, section =>
        {
            Unsigned(section, 1);
            section.WriteByte(0x60);
            Unsigned(section, 1);
            section.WriteByte(target == "wasm64" ? (byte)0x7e : (byte)0x7f);
            Unsigned(section, 0);
        });
        Section(module, 3, section => section.Write([1, 0]));
        Section(module, 7, section =>
        {
            Unsigned(section, 1);
            Name(section, "emscripten_notify_memory_growth");
            section.Write([0, 0]);
        });
        Section(module, 10, section => section.Write([1, 2, 0, 0x0b]));
        return module.ToArray();
    }

    private static void Section(Stream module, byte id, Action<MemoryStream> write)
    {
        using var section = new MemoryStream();
        write(section);
        module.WriteByte(id);
        Unsigned(module, checked((uint)section.Length));
        section.Position = 0;
        section.CopyTo(module);
    }

    private static void Name(Stream output, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Unsigned(output, checked((uint)bytes.Length));
        output.Write(bytes);
    }

    private static void Unsigned(Stream output, uint value)
    {
        do
        {
            var next = (byte)(value & 0x7f);
            value >>= 7;
            if (value != 0)
                next |= 0x80;
            output.WriteByte(next);
        }
        while (value != 0);
    }

    private static InlineEvidence ReadEvidence(byte[] module)
    {
        var reader = new Reader(module);
        Assert.Equal("0061736D01000000", Convert.ToHexString(reader.Bytes(8)));
        var exports = new Dictionary<string, uint>(StringComparer.Ordinal);
        var aggressive = new List<uint>();
        while (reader.Remaining != 0)
        {
            var id = reader.Byte();
            var section = new Reader(reader.Bytes(checked((int)reader.Unsigned())));
            if (id == 7)
            {
                var count = section.Unsigned();
                for (var index = 0u; index < count; index++)
                {
                    var name = section.Name();
                    var kind = section.Byte();
                    var item = section.Unsigned();
                    if (kind == 0)
                        exports.Add(name, item);
                }
                Assert.Equal(0, section.Remaining);
                continue;
            }
            if (id != 0 || section.Name() != "binaryen.inline")
                continue;
            var functions = section.Unsigned();
            for (var function = 0u; function < functions; function++)
            {
                var functionIndex = section.Unsigned();
                var annotations = section.Unsigned();
                for (var annotation = 0u; annotation < annotations; annotation++)
                {
                    var offset = section.Unsigned();
                    var hint = section.Bytes(checked((int)section.Unsigned()));
                    if (offset == 0 && hint is [127])
                        aggressive.Add(functionIndex);
                }
            }
            Assert.Equal(0, section.Remaining);
        }
        Assert.Equal(aggressive.Count, aggressive.Distinct().Count());
        return new(exports, aggressive);
    }

    private static string RequiredDirectory(string name)
    {
        var path = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(Directory.Exists(path));
        return path!;
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
        throw new InvalidOperationException("The integration test repository root is unavailable.");
    }

    private sealed record InlineEvidence(
        Dictionary<string, uint> Exports,
        List<uint> AggressiveFunctions);

    private enum InlinePreference
    {
        Aggressive,
    }

    private sealed class BoundProcessExecution(string nodePath, string mergeScript) : IProcessExecution
    {
        public ToolResult Execute(ExternalToolInvocation invocation)
        {
            Assert.Equal(BinaryenToolIds.WasmMerge, invocation.Executable);
            Assert.Empty(invocation.EnvironmentVariablePrefixesToRemove);
            var start = new ProcessStartInfo
            {
                FileName = nodePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add(mergeScript);
            foreach (var argument in invocation.Arguments)
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ??
                throw new InvalidOperationException("The integration wasm-merge process did not start.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(standardOutput, standardError);
            return new(process.ExitCode, standardOutput.Result, standardError.Result);
        }
    }

    private ref struct Reader
    {
        private ReadOnlySpan<byte> _remaining;

        public Reader(ReadOnlySpan<byte> source) => _remaining = source;
        public readonly int Remaining => _remaining.Length;

        public byte Byte()
        {
            var value = _remaining[0];
            _remaining = _remaining[1..];
            return value;
        }

        public ReadOnlySpan<byte> Bytes(int length)
        {
            var value = _remaining[..length];
            _remaining = _remaining[length..];
            return value;
        }

        public string Name() => Encoding.UTF8.GetString(Bytes(checked((int)Unsigned())));

        public uint Unsigned()
        {
            uint value = 0;
            for (var shift = 0; shift < 35; shift += 7)
            {
                var item = Byte();
                value |= (uint)(item & 0x7f) << shift;
                if ((item & 0x80) == 0)
                    return value;
            }
            throw new InvalidOperationException("The integration Wasm contains an invalid unsigned integer.");
        }
    }
}
