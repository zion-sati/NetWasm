using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Browser;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser;

public sealed class BrowserComponentLinkPlanCaptureTests
{
    [Fact]
    public void RejectsUnexpectedStagesToolsAndRepeatedCapture()
    {
        var capture = CreateCapture();
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
    public void CompleteSnapshotOwnsValuesAndCleanupCannotEraseCapturedModules()
    {
        var capture = CreateCapture();
        capture.AddText("(module)", "environment");
        capture.AddInvocation(BinaryenToolIds.WasmMerge, ["original"]);
        capture.AddExportPruning("merged", "sanitized", "cm32p2");
        capture.AddInvocation(BinaryenToolIds.WasmOpt, ["optimized"]);
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

    private static BrowserComponentLinkPlanCapture CreateCapture() => new(
        ImmutableHashSet.Create(StringComparer.Ordinal, "environment", "host", "adapter", "merged", "sanitized"));
}
