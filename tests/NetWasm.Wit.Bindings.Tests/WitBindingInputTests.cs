using System.Diagnostics;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Wit.Bindings;

namespace NetWasm.Wit.Bindings.Tests;

// The tool's WIT can come from a command instead of a file: from standard
// input, or from the pipe a shell passes for --wit <(command), which can be
// read only once and which wasm-tools, a separate process, cannot open. Both
// are run through real wasm-tools.
public sealed class WitBindingInputTests
{
    // The world of the report: it declares its own alias and enum.
    private const string Wit = """
        package my:ns;

        world my-world {
            type my-type = u64;

            enum my-enum {
                a, b, c, d,
            }

            import my-fn-b: func() -> my-type;
            import my-fn-a: func(a: my-type) -> my-enum;

            export my-export: func();
        }
        """;

    [Fact]
    public void GeneratesFromStandardInputToStandardOutput()
    {
        using var output = new StringWriter();

        _ = Command(new StringReader(Wit), output).Run(["--wit", "-", "--output", "-"]);

        var source = output.ToString();
        Assert.Contains("namespace NetWasm.Wit.My.Ns;", source, StringComparison.Ordinal);
        Assert.Single(source.Split('\n'), line => line == "public static partial class MyWorldImports");
        Assert.Contains("[WitImport(\"my-fn-b\")]", source, StringComparison.Ordinal);
        Assert.Contains("[WitImport(\"my-fn-a\")]", source, StringComparison.Ordinal);
        Assert.Contains("public enum MyEnum", source, StringComparison.Ordinal);
        Assert.Contains("public static MyEnum MyFnA(ulong a)", source, StringComparison.Ordinal);
        Assert.Contains("public static ulong MyFnB()", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratesFromAPipeThatCanBeReadOnlyOnce()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        var directory = Directory.CreateTempSubdirectory("netwasm-wit-pipe-");
        try
        {
            var pipe = Path.Combine(directory.FullName, "input");
            using (var mkfifo = Process.Start("mkfifo", [pipe]))
            {
                mkfifo.WaitForExit();
                Assert.Equal(0, mkfifo.ExitCode);
            }
            var cancellation = TestContext.Current.CancellationToken;
            var writer = Task.Run(() => File.WriteAllTextAsync(pipe, Wit, cancellation), cancellation);
            using var output = new StringWriter();

            _ = Command(TextReader.Null, output).Run(["--wit", pipe, "--output", "-", "--namespace", "My.Bindings"]);

            await writer;
            Assert.Contains("namespace My.Bindings;", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("public static partial class MyWorldExports", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static WitBindingCommand Command(TextReader input, TextWriter output) => new(
        new WitBindingOptionsReader(),
        new WitInputStager(input),
        new WitDocumentReader(new ProcessWasmTools(
            new SystemExternalCommandRunner(),
            Environment.GetEnvironmentVariable("NETWASM_WASM_TOOLS_PATH") ?? "wasm-tools")),
        WitBindingCompositionRoot.Create(),
        new WitBindingSourceAccessibilityRewriter(),
        new SystemTextFileWriter(),
        output);
}
