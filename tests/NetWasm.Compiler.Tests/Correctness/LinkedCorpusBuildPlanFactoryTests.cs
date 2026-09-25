using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class LinkedCorpusBuildPlanFactoryTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, 0, "wasm32", "debug", 3)]
    [InlineData(WasmTarget.Wasm64, 0, "wasm64", "debug", 3)]
    [InlineData(WasmTarget.Wasm32, 1, "wasm32", "release", 5)]
    [InlineData(WasmTarget.Wasm64, 1, "wasm64", "release", 5)]
    public void PlansNativeBuildAndAssertedValidationForEveryCell(
        WasmTarget target, int formValue, string targetName, string configuration, int stages)
    {
        var form = (CorpusWasmForm)formValue;
        var request = Request with { Target = target, Form = form };
        var factory = new LinkedCorpusBuildPlanFactory();

        var plan = ((ILinkedCorpusBuildPlanFactory)factory).Create(request);

        Assert.Equal(stages, plan.Steps.Length);
        Assert.Equal(
            new[] { LinkedCorpusBuildStage.NativeRuntime, LinkedCorpusBuildStage.Merge, LinkedCorpusBuildStage.ValidateMerged },
            plan.Steps.Take(3).Select(step => step.Stage));
        var native = plan.Steps[0].Process;
        Assert.Equal("bash", native.FileName);
        Assert.Equal(
            new[] { Path.Combine(request.RepositoryRoot, "eng", "build-netwasm-runtime.sh"),
                "--runtime-layout", request.RuntimeLayoutPath, "--target", targetName,
                "--configuration", configuration, "--output", plan.RuntimeModulePath },
            native.Arguments);
        Assert.Equal(request.Tools.EmsdkRoot, native.EnvironmentVariables["NETWASM_EMSDK_ROOT"]);
        Assert.Equal(request.RunDirectory, native.EnvironmentVariables["TMPDIR"]);
        Assert.Equal(2, native.EnvironmentVariables.Count);
        var merge = plan.Steps[1].Process;
        Assert.Equal(request.Tools.Merge, merge.FileName);
        Assert.Equal(
            new[] { request.ApplicationModulePath, "netwasm.application.v1", plan.RuntimeModulePath, "netwasm.runtime.v1" },
            merge.Arguments.Take(4));
        Assert.Equal(target == WasmTarget.Wasm64, merge.Arguments.Contains("--enable-memory64"));
        Assert.Contains("--enable-nontrapping-float-to-int", merge.Arguments);
        Assert.Contains("--enable-exception-handling", merge.Arguments);
        Assert.Contains("--enable-bulk-memory", merge.Arguments);
        Assert.Contains("--enable-multimemory", merge.Arguments);
        Assert.Equal(new[] { "-g", "-o", plan.MergedModulePath }, merge.Arguments.TakeLast(3));
        Assert.Equal(request.Tools.Validate, plan.Steps[2].Process.FileName);
        Assert.Equal(new[] { "validate", plan.MergedModulePath, "--features", "all" }, plan.Steps[2].Process.Arguments);
        if (form == CorpusWasmForm.Optimized)
        {
            Assert.Equal(LinkedCorpusBuildStage.Optimize, plan.Steps[3].Stage);
            Assert.Equal(LinkedCorpusBuildStage.ValidateFinal, plan.Steps[4].Stage);
            var optimize = plan.Steps[3].Process;
            Assert.Equal(request.Tools.Optimize, optimize.FileName);
            Assert.Equal(plan.MergedModulePath, optimize.Arguments[0]);
            Assert.Equal("-Oz", optimize.Arguments[1]);
            Assert.Contains("--disable-compact-imports", optimize.Arguments);
            Assert.Equal(target == WasmTarget.Wasm64, optimize.Arguments.Contains("--enable-memory64"));
            Assert.Equal(new[] { "-o", plan.OutputModulePath }, optimize.Arguments.TakeLast(2));
            Assert.Equal(request.Tools.Validate, plan.Steps[4].Process.FileName);
            Assert.Equal(new[] { "validate", plan.OutputModulePath, "--features", "all" }, plan.Steps[4].Process.Arguments);
            Assert.NotEqual(plan.MergedModulePath, plan.OutputModulePath);
        }
        else
        {
            Assert.Equal(plan.MergedModulePath, plan.OutputModulePath);
        }
        Assert.All(plan.Steps, step =>
        {
            Assert.Equal(request.RepositoryRoot, step.Process.WorkingDirectory);
            Assert.Equal(request.Timeout, step.Process.Timeout);
        });
        Assert.All(plan.Steps.Skip(1), step => Assert.Empty(step.Process.EnvironmentVariables));
        Assert.StartsWith(request.RunDirectory, plan.RuntimeModulePath, StringComparison.Ordinal);
        Assert.StartsWith(request.RunDirectory, plan.OutputModulePath, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMissingRequestsToolsAndPaths()
    {
        var factory = new LinkedCorpusBuildPlanFactory();
        Assert.Throws<ArgumentNullException>(() => factory.Create(null!));
        Assert.Throws<ArgumentNullException>(() => factory.Create(Request with { Tools = null! }));
        Assert.Throws<ArgumentNullException>(() => factory.Create(Request with { ApplicationModulePath = null! }));
        Assert.Throws<ArgumentException>(() => factory.Create(Request with { RunDirectory = " " }));
        Assert.Throws<ArgumentException>(() => factory.Create(Request with { RuntimeLayoutPath = "relative.json" }));
    }

    [Fact]
    public void RejectsUnsupportedDimensionsTimeoutsAndInputOutputCollisions()
    {
        var factory = new LinkedCorpusBuildPlanFactory();
        Assert.Throws<ArgumentOutOfRangeException>(() => factory.Create(Request with { Target = (WasmTarget)99 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => factory.Create(Request with { Form = (CorpusWasmForm)99 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => factory.Create(Request with { Timeout = TimeSpan.Zero }));
        var plan = factory.Create(Request);
        Assert.Throws<ArgumentException>(() => factory.Create(Request with { ApplicationModulePath = plan.RuntimeModulePath }));
        Assert.Throws<ArgumentException>(() => factory.Create(Request with { RuntimeLayoutPath = Request.ApplicationModulePath }));
        Assert.Throws<ArgumentException>(() => factory.Create(Request with
        {
            ApplicationModulePath = Path.Combine(Request.RunDirectory, "subdir", "..", Path.GetFileName(plan.MergedModulePath)),
        }));
    }

    internal static LinkedCorpusBuildRequest Request
    {
        get
        {
            var root = Path.GetFullPath("linked-corpus-fixture");
            return new(root, Path.Combine(root, "run"), Path.Combine(root, "application.wasm"),
                Path.Combine(root, "layout.json"), WasmTarget.Wasm32, CorpusWasmForm.Direct,
                new(Path.Combine(root, "emsdk"), Path.Combine(root, "wasm-merge"),
                    Path.Combine(root, "wasm-opt"), Path.Combine(root, "wasm-tools"),
                    Path.Combine(root, "node")), TimeSpan.FromMinutes(2));
        }
    }
}
