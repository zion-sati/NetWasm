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
