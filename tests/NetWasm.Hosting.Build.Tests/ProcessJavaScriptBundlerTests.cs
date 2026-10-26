using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.Tests;

public sealed class ProcessJavaScriptBundlerTests
{
    [Fact]
    public void BuildsExactArgumentVectorAndRequiresOutput()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "bundle.mjs");
        var process = new RecordingProcess(request => File.WriteAllText(output, "bundle"));
        var subject = new ProcessJavaScriptBundler(process);

        subject.Bundle(new(
            Path.Combine(directory.Path, "node"),
            Path.Combine(directory.Path, "bundle-command.mjs"),
            Path.Combine(directory.Path, "entry.mjs"),
            output,
            "browser",
            true));

        Assert.Equal<string>(
            [
                "bundle",
                Path.Combine(directory.Path, "entry.mjs"),
                "--output",
                output,
                "--platform",
                "browser",
                "--minify",
                "true",
            ],
            process.Request!.Arguments);
    }

    // Issue #128: an entry reaches the shared runtime by the URLs its map gives.
    [Fact]
    public void PassesTheRuntimeImportsMapAndBundlesTheRuntime()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "bundle.mjs");
        var runtime = Path.Combine(directory.Path, "runtime");
        var process = new RecordingProcess(request =>
        {
            if (request.Arguments[0] == "bundle")
            {
                File.WriteAllText(output, "bundle");
            }
            else
            {
                Directory.CreateDirectory(runtime);
                File.WriteAllText(Path.Combine(runtime, "runtime.json"), "{}");
            }
        });
        var subject = new ProcessJavaScriptBundler(process);
        var imports = Path.Combine(directory.Path, "imports.json");

        subject.Bundle(new(
            Path.Combine(directory.Path, "node"),
            Path.Combine(directory.Path, "bundle-command.mjs"),
            Path.Combine(directory.Path, "entry.mjs"),
            output,
            "browser",
            false)
        { RuntimeImportsPath = imports });
        Assert.Equal<string>(["--runtime", imports], process.Request!.Arguments[^2..]);

        subject.BundleRuntime(new(
            Path.Combine(directory.Path, "node"),
            Path.Combine(directory.Path, "bundle-command.mjs"),
            Path.Combine(directory.Path, "facades"),
            runtime,
            true));
        Assert.Equal<string>(
            ["bundle-runtime", Path.Combine(directory.Path, "facades"), "--output", runtime, "--minify", "true"],
            process.Request!.Arguments);

        File.Delete(Path.Combine(runtime, "runtime.json"));
        var silent = new ProcessJavaScriptBundler(new RecordingProcess(_ => { }));
        Assert.Throws<InvalidOperationException>(() => silent.BundleRuntime(new(
            Path.Combine(directory.Path, "node"), Path.Combine(directory.Path, "bundle-command.mjs"),
            Path.Combine(directory.Path, "facades"), runtime, true)));
        Assert.Throws<ArgumentException>(() => subject.BundleRuntime(new(
            Path.Combine(directory.Path, "node"), Path.Combine(directory.Path, "bundle-command.mjs"),
            "facades", runtime, true)));
        Assert.Throws<ArgumentException>(() => subject.Bundle(new(
            Path.Combine(directory.Path, "node"), Path.Combine(directory.Path, "bundle-command.mjs"),
            Path.Combine(directory.Path, "entry.mjs"), output, "browser", false)
        { RuntimeImportsPath = "imports.json" }));
    }

    [Fact]
    public void RejectsInvalidRequestsBeforeProcessExecution()
    {
        var process = new RecordingProcess(_ => Assert.Fail("Must not execute."));
        var subject = new ProcessJavaScriptBundler(process);
        var absolute = Path.GetFullPath("tool");

        Assert.Throws<ArgumentException>(() => subject.Bundle(new(
            absolute, absolute, absolute, absolute, "universal", false)));
        Assert.Throws<ArgumentException>(() => subject.Bundle(new(
            "node", absolute, absolute, absolute, "node", false)));
        Assert.Null(process.Request);
    }

    [Fact]
    public void RejectsMissingProcessCapability()
    {
        Assert.Throws<ArgumentNullException>(() => new ProcessJavaScriptBundler(null!));
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
