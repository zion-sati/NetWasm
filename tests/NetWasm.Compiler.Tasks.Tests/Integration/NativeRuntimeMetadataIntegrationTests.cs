using System.Diagnostics;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Tasks.ComponentModel;

namespace NetWasm.Compiler.Tasks.Tests.Integration;

public sealed class NativeRuntimeMetadataIntegrationTests
{
    [Fact]
    [Trait("Category", "NativeMetadataIntegration")]
    public async Task PreservesLiteralExportNamesAndInvalidatesPackagingIdentityOnlyWhenMetadataChanges()
    {
        var retained = Environment.GetEnvironmentVariable("NETWASM_NATIVE_INTEROP_EVIDENCE_ROOT");
        var root = retained is null ? Directory.CreateTempSubdirectory("netwasm-native-metadata-").FullName :
            Path.Combine(retained, "metadata-roundtrip");
        if (retained is not null)
        {
            Assert.False(Directory.Exists(root));
            Directory.CreateDirectory(root);
        }
        try
        {
            var assets = Path.Combine(AppContext.BaseDirectory, "IntegrationAssets");
            var project = Path.Combine(root, "NativeRuntimeMetadata.proj");
            File.Copy(Path.Combine(assets, "NativeRuntimeMetadata.proj"), project);
            File.Copy(Path.Combine(assets, "global.json"), Path.Combine(root, "global.json"));
            var observation = Path.Combine(root, "exports.json");
            var identity = Path.Combine(root, "packaging.identity");
            var reader = new InternalRuntimeExportReader();
            var first = Expected("first");

            await RunAsync("first", 1);
            Assert.Equal(first, reader.Read(await File.ReadAllTextAsync(observation)).ToArray());
            var firstIdentity = await File.ReadAllTextAsync(identity);
            var firstTime = File.GetLastWriteTimeUtc(identity);
            Assert.Equal("internalRuntimeExports=" + JsonSerializer.Serialize(first),
                Assert.Single(await File.ReadAllLinesAsync(identity), line => line.StartsWith("internalRuntimeExports=", StringComparison.Ordinal)));

            await RunAsync("first", 2);
            Assert.Equal(firstIdentity, await File.ReadAllTextAsync(identity));
            Assert.Equal(firstTime, File.GetLastWriteTimeUtc(identity));

            await RunAsync("second", 3);
            var second = Expected("second");
            Assert.Equal(second, reader.Read(await File.ReadAllTextAsync(observation)).ToArray());
            Assert.NotEqual(firstIdentity, await File.ReadAllTextAsync(identity));
            Assert.Equal("internalRuntimeExports=" + JsonSerializer.Serialize(second),
                Assert.Single(await File.ReadAllLinesAsync(identity), line => line.StartsWith("internalRuntimeExports=", StringComparison.Ordinal)));

            async Task RunAsync(string variant, int run)
            {
                var start = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var argument in new[]
                {
                    "msbuild", project, "-nologo", "-v:q",
                    "-p:ProbeAssembly=" + typeof(NativeRuntimeMetadataProducerTask).Assembly.Location,
                    "-p:CompilerTargets=" + Path.Combine(assets, "NetWasm.Compiler.Tasks.targets"),
                    "-p:ObservationPath=" + observation, "-p:NetWasmPackagingIdentityPath=" + identity,
                    "-p:Variant=" + variant,
                }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                await using var stdout = File.Create(Path.Combine(root, $"msbuild-{run}.stdout.log"));
                await using var stderr = File.Create(Path.Combine(root, $"msbuild-{run}.stderr.log"));
                await Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(stdout),
                    process.StandardError.BaseStream.CopyToAsync(stderr), process.WaitForExitAsync());
                Assert.Equal(0, process.ExitCode);
            }
        }
        finally
        {
            if (retained is null) Directory.Delete(root, recursive: true);
        }
    }

    private static WasmInternalExport[] Expected(string variant) =>
        [new(variant + "%3Bentry", 0), new("global;" + variant + "%253B", 3)];
}
