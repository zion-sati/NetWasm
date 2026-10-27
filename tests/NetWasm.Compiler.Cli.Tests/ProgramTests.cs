using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using NetWasm.Compiler.Cli.Tests.TestSupport;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class ProgramTests
{
    private const string World = "netwasm:platform@1.0.0/platform";

    [Fact]
    public void ReportsAnInvalidCommandLineAsCscDoes()
    {
        var console = new RecordingConsole();

        var result = Program.Run(["-nologo", "App.dll"], console);

        Assert.Equal(1, result);
        Assert.Equal(["error NW1000: no output file was specified (-out)"], console.Lines);
    }

    [Fact]
    public void ReportsAnyOtherFailureAsAHostFailure()
    {
        var console = new RecordingConsole();
        var missing = Path.Combine(Path.GetTempPath(), $"netwasm-missing-{Guid.NewGuid():N}.dll");

        var result = Program.Run(["-nologo", missing, "-out:app.wasm"], console);

        Assert.Equal(1, result);
        var line = Assert.Single(console.Lines);
        Assert.StartsWith("error NW1014: ", line, StringComparison.Ordinal);
        Assert.Contains(missing, line, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWritesToTheProcessConsole()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        int result;
        try
        {
            result = Program.Main(["-nologo", "-help"]);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal(0, result);
        Assert.StartsWith("                             NetWasm Compiler Options", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void CompilesAFunctionModuleAndTheSidecarsItNames(string platform)
    {
        using var assets = TestAssets.Create();
        var output = Path.Combine(assets.Directory, "app.wasm");
        var layout = Path.Combine(assets.Directory, "layout.json");
        var interop = Path.Combine(assets.Directory, "interop.json");
        var console = new RecordingConsole();

        var result = Program.Run(
            [
                "-nologo", assets.Application, $"-r:{assets.Library};{assets.CoreLib}", "-target:function",
                "-entry:NetWasm.Fixtures.Application.EntryPoint::Run", $"-platform:{platform}", "-optimize:None",
                $"-out:{output}", $"-runtimelayout:{layout}", $"-interopmanifest:{interop}", $"-wit:{Wit(assets)}",
                $"-world:{World}",
            ],
            console);

        Assert.Equal(0, result);
        Assert.DoesNotContain(console.Lines, line => line.StartsWith("error", StringComparison.Ordinal));
        Assert.Equal([0, 97, 115, 109], File.ReadAllBytes(output)[..4]);
        using var document = JsonDocument.Parse(File.ReadAllText(layout));
        Assert.Equal(platform, document.RootElement.GetProperty("target").GetString());
        Assert.Contains($"\"target\": \"{platform}\"", File.ReadAllText(interop), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(assets.Directory, "app.callbacks.o")));
    }

    [Fact]
    public void CompilesAnExecutableFromItsEntryPoint()
    {
        using var assets = TestAssets.Create();
        var application = WithEntryPoint(assets.Application, "EntryPoint", "Main");
        var output = Path.Combine(assets.Directory, "app.wasm");
        var layout = Path.Combine(assets.Directory, "layout.json");
        var console = new RecordingConsole();

        var result = Program.Run(
            [
                "-nologo", application, $"-r:{assets.Library}", $"-r:{assets.CoreLib}", "-optimize:None",
                $"-out:{output}", $"-runtimelayout:{layout}", $"-wit:{Wit(assets)}", $"-world:{World}",
            ],
            console);

        Assert.True(result == 0, string.Join('\n', console.Lines));
        using var document = JsonDocument.Parse(File.ReadAllText(layout));
        var entryPoint = document.RootElement.GetProperty("managedExecutableEntryPoint");
        Assert.Equal("none", entryPoint.GetProperty("parameterShape").GetString());
        Assert.Equal("exitCode", entryPoint.GetProperty("returnShape").GetString());
    }

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void CompilesALibraryRepeatablyThroughTheFrontendCache(string platform)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource("WorkerLibrary", """
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices.JavaScript;
            public static class Worker
            {
                private static int counter;
                [ModuleInitializer]
                public static void Initialize() => counter = 40;
                [JSExport("add")]
                public static int Add(int left, int right) => counter + left + right;
            }
            """);
        byte[] Compile(string name)
        {
            var console = new RecordingConsole();
            var output = Path.Combine(assets.Directory, name + ".wasm");
            var result = Program.Run(
                [
                    "-nologo", library, $"-r:{assets.CoreLib}", "-target:library", $"-platform:{platform}",
                    "-optimize:None", "-jsexportboundary+", $"-out:{output}",
                    $"-interopmanifest:{Path.Combine(assets.Directory, name + ".json")}",
                    $"-intermediatedirectory:{Path.Combine(assets.Directory, "obj")}",
                    $"-wit:{Wit(assets)}", $"-world:{World}",
                ],
                console);
            Assert.True(result == 0, string.Join('\n', console.Lines));
            return File.ReadAllBytes(output);
        }

        var first = Compile("first");
        var repeated = Compile("repeated");

        Assert.Equal(first, repeated);
        using var interop = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets.Directory, "first.json")));
        Assert.Equal(platform, interop.RootElement.GetProperty("target").GetString());
        Assert.Contains(
            interop.RootElement.GetProperty("exports").EnumerateArray(),
            export => export.GetProperty("name").GetString() == "add");
    }

    [Fact]
    public void RunsTheWasmToolsTheOptionsName()
    {
        using var assets = TestAssets.Create();
        var node = Path.Combine(assets.Directory, "missing-node");
        var console = new RecordingConsole();

        var result = Program.Run(
            [
                "-nologo", assets.Application, $"-r:{assets.Library}", $"-r:{assets.CoreLib}", "-target:function",
                "-entry:NetWasm.Fixtures.Application.EntryPoint::Run", "-optimize:None",
                $"-out:{Path.Combine(assets.Directory, "app.wasm")}", $"-wit:{Wit(assets)}", $"-world:{World}",
                $"-wasmtoolsnode:{node}", "-wasmtoolscommand:run.mjs", "-wasmtoolsmodule:wasm-tools.wasm",
            ],
            console);

        Assert.Equal(1, result);
        Assert.Contains(console.Lines, line => line.StartsWith("error NW", StringComparison.Ordinal) &&
            line.Contains(node, StringComparison.Ordinal));
    }

    private static string Wit(TestAssets assets) => Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0");

    // The fixtures compile as libraries; this gives a copy the entry point a
    // compiled executable carries in its CLI header.
    private static string WithEntryPoint(string assembly, string typeName, string methodName)
    {
        var image = File.ReadAllBytes(assembly);
        using (var pe = new PEReader(new MemoryStream(image, writable: false)))
        {
            var metadata = pe.GetMetadataReader();
            var method = metadata.MethodDefinitions.Single(handle =>
            {
                var definition = metadata.GetMethodDefinition(handle);
                return metadata.GetString(definition.Name) == methodName &&
                    metadata.GetString(metadata.GetTypeDefinition(definition.GetDeclaringType()).Name) == typeName;
            });
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(pe.PEHeaders.CorHeaderStartOffset + 20, 4),
                MetadataTokens.GetToken(method));
        }
        var path = Path.Combine(Path.GetDirectoryName(assembly)!, "Executable", Path.GetFileName(assembly));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, image);
        return path;
    }
}
