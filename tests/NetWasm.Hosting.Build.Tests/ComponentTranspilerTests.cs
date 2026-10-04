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
            var temporary = request.Arguments[2];
            File.WriteAllText(Path.Combine(temporary, "program-component.js"), "export {};");
            File.WriteAllText(Path.Combine(temporary, "program-component.d.ts"), "export {};");
            var interfaces = Path.Combine(temporary, "interfaces");
            Directory.CreateDirectory(interfaces);
            File.WriteAllText(Path.Combine(interfaces, "api.d.ts"), "export {};");
            File.WriteAllBytes(Path.Combine(temporary, "program-component.core.wasm"), [0]);
            File.WriteAllBytes(Path.Combine(temporary, "program-component.core2.wasm"), [1]);
            File.WriteAllText(request.Arguments[4], """
                {"schemaVersion":1,"exports":[
                  {"name":"rootValue","kind":"function"},
                  {"name":"mathAlias","kind":"instance"}
                ]}
                """);
        });
        var runner = Path.Combine(directory.Path, "run-jco-transpile.mjs");
        var subject = new ComponentTranspiler(process, runner);

        var result = subject.Transpile(new(
            Path.Combine(directory.Path, "node"),
            Path.Combine(directory.Path, "jco.js"),
            Path.Combine(directory.Path, "program.wasm"),
            output,
            "program-component"));

        var invocation = Assert.IsType<JavaScriptProcessRequest>(process.Request);
        Assert.Equal("component transpiler", invocation.Operation);
        Assert.Equal(runner, invocation.EntryPointPath);
        Assert.Equal(Path.Combine(directory.Path, "api.js"), invocation.Arguments[0]);
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
        Assert.Equal(
            [
                new JcoComponentRootExport("rootValue", "function"),
                new JcoComponentRootExport("mathAlias", "instance"),
            ],
            result.RootExports.ToArray());
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
            var temporary = request.Arguments[2];
            var unexpected = Path.Combine(temporary, "unexpected");
            Directory.CreateDirectory(unexpected);
            File.WriteAllText(Path.Combine(unexpected, "payload.txt"), "bad");
            File.WriteAllText(request.Arguments[4], "{\"schemaVersion\":1,\"exports\":[]}");
        }), Path.Combine(directory.Path, "run-jco-transpile.mjs"));

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

    [Fact]
    public void RejectsMissingMalformedAndInvalidRootExportMetadata()
    {
        using var directory = TemporaryDirectory.Create();
        string?[] values =
        [
            null,
            "{",
            "null",
            "{\"schemaVersion\":2,\"exports\":[]}",
            "{\"schemaVersion\":1}",
            "{\"schemaVersion\":1,\"exports\":[null]}",
            "{\"schemaVersion\":1,\"exports\":[{\"name\":\"\",\"kind\":\"function\"}]}",
            "{\"schemaVersion\":1,\"exports\":[{\"name\":\"value\",\"kind\":\"other\"}]}",
            "{\"schemaVersion\":1,\"exports\":[{\"name\":\"value\",\"kind\":\"function\"},{\"name\":\"value\",\"kind\":\"instance\"}]}",
        ];
        foreach (var value in values)
        {
            var output = Path.Combine(directory.Path, Guid.NewGuid().ToString("N"));
            var subject = new ComponentTranspiler(new RecordingProcess(request =>
            {
                var temporary = request.Arguments[2];
                File.WriteAllText(Path.Combine(temporary, "program.js"), "export {};");
                File.WriteAllBytes(Path.Combine(temporary, "program.core.wasm"), [0]);
                if (value is not null) File.WriteAllText(request.Arguments[4], value);
            }), Path.Combine(directory.Path, "run-jco-transpile.mjs"));

            Assert.Throws<InvalidOperationException>(() => subject.Transpile(new(
                Path.Combine(directory.Path, "node"),
                Path.Combine(directory.Path, "jco.js"),
                Path.Combine(directory.Path, "program.wasm"),
                output,
                "program")));
        }
    }

    [Fact]
    public void ValidatesConstructionAndRequests()
    {
        using var directory = TemporaryDirectory.Create();
        var process = new RecordingProcess(_ => { });
        Assert.NotNull(new ComponentTranspiler());
        Assert.NotNull(new ComponentTranspiler(process));
        Assert.Throws<ArgumentNullException>(() => new ComponentTranspiler(null!));

        var subject = new ComponentTranspiler(
            process,
            Path.Combine(directory.Path, "run-jco-transpile.mjs"));
        Assert.Throws<ArgumentNullException>(() => subject.Transpile(null!));
        var valid = new ComponentTranspileRequest(
            Path.Combine(directory.Path, "node"),
            Path.Combine(directory.Path, "jco.js"),
            Path.Combine(directory.Path, "program.wasm"),
            Path.Combine(directory.Path, "output"),
            "program");
        foreach (var mutation in new ComponentTranspileRequest[]
        {
            valid with { NodePath = "" },
            valid with { JcoPath = "relative" },
            valid with { ComponentPath = "relative" },
            valid with { OutputDirectory = "relative" },
            valid with { OutputDirectory = Path.GetPathRoot(directory.Path)! },
            valid with { BaseName = "" },
            valid with { BaseName = "nested/program" },
            valid with { BaseName = "bad\0name" },
        })
        {
            Assert.Throws<ArgumentException>(() => subject.Transpile(mutation));
        }
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
