using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.Tests;

public sealed class ComponentTranspilerTests
{
    [Fact]
    public void EmitsOnlyThePinnedGeneratedRuntimeClosure()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "generated");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "stale.js"), "stale");
        var process = new RecordingProcess(request =>
        {
            var outputIndex = request.Arguments.IndexOf("--out-dir");
            var temporary = request.Arguments[outputIndex + 1];
            File.WriteAllText(Path.Combine(temporary, "program-component.js"), "export {};");
            File.WriteAllText(Path.Combine(temporary, "program-component.d.ts"), "export {};");
            var interfaces = Path.Combine(temporary, "interfaces");
            Directory.CreateDirectory(interfaces);
            File.WriteAllText(Path.Combine(interfaces, "api.d.ts"), "export {};");
            File.WriteAllBytes(Path.Combine(temporary, "program-component.core.wasm"), [0]);
            File.WriteAllBytes(Path.Combine(temporary, "program-component.core2.wasm"), [1]);
        });
        var subject = new ComponentTranspiler(process);

        var result = subject.Transpile(new(
            Path.Combine(directory.Path, "node"),
            Path.Combine(directory.Path, "jco.js"),
            Path.Combine(directory.Path, "program.wasm"),
            output,
            "program-component"));

        var invocation = Assert.IsType<JavaScriptProcessRequest>(process.Request);
        Assert.Equal("component transpiler", invocation.Operation);
        Assert.Contains("--instantiation", invocation.Arguments);
        Assert.Contains("--strict", invocation.Arguments);
        Assert.Contains("--no-wasi-shim", invocation.Arguments);
        Assert.Contains("--no-typescript", invocation.Arguments);
        Assert.False(File.Exists(Path.Combine(output, "stale.js")));
        Assert.Empty(Directory.GetDirectories(output));
        Assert.DoesNotContain(
            Directory.GetFiles(output),
            path => path.EndsWith(".d.ts", StringComparison.Ordinal));
        Assert.Equal(Path.Combine(output, "program-component.js"), result.JavaScriptPath);
        Assert.Equal<string>(
            [
                Path.Combine(output, "program-component.core.wasm"),
                Path.Combine(output, "program-component.core2.wasm"),
            ],
            result.CoreModulePaths);
    }

    [Fact]
    public void RejectsUnexpectedOutputWithoutReplacingPreviousClosure()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "generated");
        Directory.CreateDirectory(output);
        var stale = Path.Combine(output, "accepted.js");
        File.WriteAllText(stale, "accepted");
        var subject = new ComponentTranspiler(new RecordingProcess(request =>
        {
            var outputIndex = request.Arguments.IndexOf("--out-dir");
            var temporary = request.Arguments[outputIndex + 1];
            var unexpected = Path.Combine(temporary, "unexpected");
            Directory.CreateDirectory(unexpected);
            File.WriteAllText(Path.Combine(unexpected, "payload.txt"), "bad");
        }));

        Assert.Throws<InvalidOperationException>(() => subject.Transpile(new(
            Path.Combine(directory.Path, "node"),
            Path.Combine(directory.Path, "jco.js"),
            Path.Combine(directory.Path, "program.wasm"),
            output,
            "program-component")));

        Assert.Equal("accepted", File.ReadAllText(stale));
        Assert.DoesNotContain(
            Directory.GetDirectories(directory.Path),
            path => Path.GetFileName(path).EndsWith(".tmp", StringComparison.Ordinal));
    }

    private sealed class RecordingProcess(Action<JavaScriptProcessRequest> run) :
        IJavaScriptProcessRunner
    {
        public JavaScriptProcessRequest? Request { get; private set; }

        public void Run(JavaScriptProcessRequest request)
        {
            Request = request;
            run(request);
        }
    }
}
