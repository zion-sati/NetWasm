using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Browser;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser;

public sealed class BrowserComponentLinkPlanCaptureTests
{
    [Fact]
    public void ExportCaptureAcceptsOnlyAnInitializedComponentRetentionPolicy()
    {
        var capture = CreateCapture();
        capture.AddText("(module)", "environment");
        capture.AddInvocation(BinaryenToolIds.WasmMerge, []);
        var editor = Assert.IsAssignableFrom<IWasmCoreModuleExportEditor>(new BrowserComponentExportPruningCapture(capture));

        Assert.Throws<ArgumentNullException>(() => editor.Rewrite("merged", "sanitized", null!));
        foreach (var selection in new WasmExportSelection[]
        {
            new("cm32p2", default), new(null, []), new("cm32p2", [new("native", 0)]),
        }) Assert.Throws<InvalidOperationException>(() => editor.Rewrite("merged", "sanitized", selection));
        Assert.Throws<ArgumentException>(() => editor.Rewrite("merged", "sanitized", new(" ", [])));
        Assert.False(capture.PlannedFileExists("sanitized"));

        editor.Rewrite("merged", "sanitized", new("cm32p2", []));

        Assert.True(capture.PlannedFileExists("sanitized"));
    }

    [Fact]
    public void ExportPrunerVirtualFilesEnforceTheirSingleInputAndOutputContract()
    {
        var existence = new BrowserComponentExportPruner.VirtualModuleExistence();
        Assert.True(existence.Exists("merged.wasm"));
        Assert.False(existence.Exists("other.wasm"));

        var source = new byte[] { 1, 2, 3 };
        var reader = new BrowserComponentExportPruner.VirtualModuleReader(source);
        Assert.Same(source, reader.Read("merged.wasm"));
        Assert.Throws<InvalidOperationException>(() => reader.Read("other.wasm"));

        var writer = new BrowserComponentExportPruner.VirtualModuleWriter();
        Assert.Throws<InvalidOperationException>(() => _ = writer.Module);
        Assert.Throws<InvalidOperationException>(() =>
            writer.Write("other.wasm", [4, 5, 6]));
        writer.Write("sanitized.wasm", [4, 5, 6]);
        Assert.Equal([4, 5, 6], writer.Module);
        Assert.Throws<InvalidOperationException>(() =>
            writer.Write("sanitized.wasm", [7]));
    }

    [Fact]
    public void RejectsUnexpectedStagesToolsAndRepeatedCapture()
    {
        var capture = CreateCapture();
        Assert.Throws<InvalidOperationException>(() =>
            capture.AddInvocation(BinaryenToolIds.WasmMerge, default));
        Assert.Throws<InvalidOperationException>(() =>
            capture.AddInvocation(BinaryenToolIds.WasmMerge, []));
        Assert.False(capture.PlannedFileExists("sanitized"));
        Assert.Throws<InvalidOperationException>(() => capture.AddInvocation(BinaryenToolIds.WasmOpt, []));
        Assert.Throws<InvalidOperationException>(() => capture.AddExportPruning("merged", "sanitized", "cm32p2"));
        Assert.Throws<InvalidOperationException>(() => capture.AddInvocation("arbitrary-tool", []));
        Assert.Throws<InvalidOperationException>(() => capture.AddText("(module)", "unowned"));
        capture.AddText("(module)", "environment");
        Assert.Throws<InvalidOperationException>(() => capture.AddText("(module)", "environment"));
        capture.AddInvocation(BinaryenToolIds.WasmMerge, []);
        Assert.Throws<InvalidOperationException>(() => capture.AddInvocation(BinaryenToolIds.WasmMerge, []));
        Assert.Throws<InvalidOperationException>(() => capture.AddText("(module)", "host"));
        capture.AddExportPruning("merged", "sanitized", "cm32p2");
        Assert.True(capture.PlannedFileExists("sanitized"));
        Assert.False(capture.PlannedFileExists("Sanitized"));
        Assert.False(capture.PlannedFileExists("unknown"));
        Assert.Throws<InvalidOperationException>(() => capture.AddExportPruning("merged", "sanitized", "cm32p2"));
        capture.AddInvocation(BinaryenToolIds.WasmOpt, []);
        Assert.Throws<InvalidOperationException>(() => capture.AddInvocation(BinaryenToolIds.WasmOpt, []));
        Assert.Throws<InvalidOperationException>(() => capture.Snapshot());
    }

    [Fact]
    public void SnapshotRejectsIncompleteCleanup()
    {
        var capture = CreateCapture();
        capture.AddText("(module)", "environment");
        capture.AddInvocation(BinaryenToolIds.WasmMerge, []);
        capture.AddExportPruning("merged", "sanitized", "cm32p2");
        capture.AddInvocation(BinaryenToolIds.WasmOpt, []);
        capture.AddCleanup("environment");

        Assert.Throws<InvalidOperationException>(() => capture.Snapshot());
    }

    [Fact]
    public void CompleteSnapshotOwnsValuesAndCleanupCannotEraseCapturedModules()
    {
        var capture = CreateCapture();
        capture.AddText("(module)", "environment");
        capture.AddInvocation(BinaryenToolIds.WasmMerge, ["original"]);
        capture.AddExportPruning("merged", "sanitized", "cm32p2");
        capture.AddInvocation(BinaryenToolIds.WasmOpt, ["optimized"]);
        capture.AddValidation(Validation("optimized"));
        foreach (var path in new[] { "environment", "host", "adapter", "merged", "sanitized" }) capture.AddCleanup(path);
        var plan = capture.Snapshot();
        Assert.Equal("(module)", Assert.Single(plan.TextModules).Text);
        Assert.Equal("original", Assert.Single(plan.Merge.Arguments));
        Assert.Throws<InvalidOperationException>(() => capture.AddCleanup("environment"));
        Assert.Throws<InvalidOperationException>(() => capture.AddCleanup("unowned"));
        Assert.Throws<InvalidOperationException>(() => capture.AddText("changed", "environment"));
        Assert.Equal("(module)", Assert.Single(plan.TextModules).Text);
        Assert.Equal(5, plan.CleanupPaths.Length);
    }

    [Fact]
    public void CompleteCopySnapshotHasNoOptimizerInvocation()
    {
        var capture = CreateCapture();
        capture.AddText("(module)", "environment");
        capture.AddInvocation(BinaryenToolIds.WasmMerge, []);
        capture.AddExportPruning("merged", "sanitized", "cm32p2");
        capture.AddValidation(Validation("sanitized"));
        capture.AddCopy("sanitized", "output");
        foreach (var path in new[] { "environment", "host", "adapter", "merged", "sanitized" }) capture.AddCleanup(path);

        var plan = capture.Snapshot();

        Assert.Null(plan.Optimization);
        Assert.Equal("sanitized", plan.Validation!.Path);
        Assert.Equal(Validation("sanitized").Arguments, plan.Validation.Arguments);
        Assert.Equal(new BrowserFileCopy("sanitized", "output"), plan.Copy);
    }

    [Fact]
    public void CopyFinalizationRequiresValidationOfTheExportProcessedModule()
    {
        var capture = CreateCapture();
        capture.AddText("(module)", "environment");
        capture.AddInvocation(BinaryenToolIds.WasmMerge, []);
        capture.AddExportPruning("merged", "sanitized", "cm32p2");

        Assert.Throws<InvalidOperationException>(() =>
            capture.AddCopy("sanitized", "output"));
        Assert.Throws<InvalidOperationException>(() =>
            capture.AddValidation(Validation("merged")));

        capture.AddValidation(Validation("sanitized"));
        Assert.Throws<InvalidOperationException>(() =>
            capture.AddValidation(Validation("sanitized")));
        capture.AddCopy("sanitized", "output");
    }

    [Fact]
    public void OptimizedOutputMustValidateBeforeACompleteSnapshotCanEscape()
    {
        var capture = CreateCapture();
        capture.AddText("(module)", "environment");
        capture.AddInvocation(BinaryenToolIds.WasmMerge, []);
        capture.AddExportPruning("merged", "sanitized", "cm32p2");
        capture.AddInvocation(BinaryenToolIds.WasmOpt, ["--output", "output"]);

        Assert.Throws<ArgumentNullException>(() => capture.AddValidation(null!));
        Assert.Throws<InvalidOperationException>(() => capture.AddValidation(Validation("sanitized")));
        Assert.Throws<InvalidOperationException>(() => capture.AddCopy("sanitized", "output"));
        capture.AddValidation(Validation("output"));
        Assert.Throws<InvalidOperationException>(() => capture.AddValidation(Validation("output")));
        foreach (var path in new[] { "environment", "host", "adapter", "merged", "sanitized" }) capture.AddCleanup(path);

        var plan = capture.Snapshot();

        Assert.Equal("output", plan.Validation!.Path);
        Assert.Equal(Validation("output").Arguments, plan.Validation.Arguments);
        Assert.NotNull(plan.Optimization);
        Assert.Null(plan.Copy);
    }

    [Fact]
    public void CompleteCleanupCannotQualifyAnUnvalidatedOptimization()
    {
        var capture = CreateCapture();
        capture.AddText("(module)", "environment");
        capture.AddInvocation(BinaryenToolIds.WasmMerge, []);
        capture.AddExportPruning("merged", "sanitized", "cm32p2");
        capture.AddInvocation(BinaryenToolIds.WasmOpt, ["--output", "output"]);
        foreach (var path in new[] { "environment", "host", "adapter", "merged", "sanitized" }) capture.AddCleanup(path);

        Assert.Throws<InvalidOperationException>(() => capture.Snapshot());
    }

    [Fact]
    public void OperationAdapterCapturesExactCommandAndRejectsMalformedContracts()
    {
        var capture = CreateCapture();
        capture.AddText("(module)", "environment");
        capture.AddInvocation(BinaryenToolIds.WasmMerge, []);
        capture.AddExportPruning("merged", "sanitized", "cm32p2");
        var operations = Assert.IsAssignableFrom<IComponentPackageOperationRunner>(
            new BrowserCoreModuleValidationOperationCapture(capture));
        Assert.Throws<ArgumentNullException>(() => operations.Run(null!, "validate"));
        Assert.Throws<ArgumentException>(() => operations.Run([], " "));
        foreach (var arguments in new string[][]
        {
            [], ["parse", "sanitized", "--features", "mvp"],
            ["validate", "sanitized", "--invalid", "mvp"],
        }) Assert.Throws<InvalidOperationException>(() => operations.Run(arguments, "validate"));
        Assert.Throws<ArgumentException>(() => operations.Run(["validate", "sanitized", "--features", " "], "validate"));
        operations.Run(["validate", "sanitized", "--features", "mvp"], "validate");
        capture.AddCopy("sanitized", "output");
        foreach (var path in new[] { "environment", "host", "adapter", "merged", "sanitized" }) capture.AddCleanup(path);

        Assert.Equal(Validation("sanitized").Arguments, capture.Snapshot().Validation!.Arguments);
    }

    private static BrowserCoreModuleValidation Validation(string path) =>
        new(path, ["validate", path, "--features", "mvp"]);

    private static BrowserComponentLinkPlanCapture CreateCapture() => new(
        ImmutableHashSet.Create(StringComparer.Ordinal, "environment", "host", "adapter", "merged", "sanitized"));
}
