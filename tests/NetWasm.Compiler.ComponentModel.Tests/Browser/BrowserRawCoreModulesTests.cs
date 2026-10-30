using NetWasm.Compiler.ComponentModel.Browser;
using NetWasm.Compiler.ComponentModel.Browser.Plans;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.ComponentModel.Inlining;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser;

public sealed class BrowserRawCoreModulesTests
{
    private static readonly string[] ExpectedMergePrefix =
    [
        "v/tmp/runtime-sanitized.wasm", "netwasm.runtime.v1", "v/tmp/sanitized.wasm",
        "netwasm.application.v1", "v/tmp/environment.wasm", "env",
    ];

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void PlansAuthoritativeRawLinkAndTemporaryExportRemoval(FinalWasmOptimization optimization)
    {
        var request = Request(optimization) with
        {
            InternalRuntimeExports = [new("LZ4_compressBound", 0), new("__heap_base", 3)],
        };

        var plan = BrowserRawCoreModules.CreateLinkPlan(request, Workspace());

        Assert.Equal("v/tmp/environment.wasm", Assert.Single(plan.Operations.OfType<BrowserLinkTextOperation>().ToArray()).OutputPath);
        Assert.Equal(BinaryenToolIds.WasmMerge, plan.Operations.OfType<BrowserLinkToolOperation>().First().ToolId);
        Assert.Equal(ExpectedMergePrefix, plan.Operations.OfType<BrowserLinkToolOperation>().First().Arguments.Take(6));
        // Managed AggressiveInlining hints survive at Oz; the runtime's do not.
        Assert.Equal(new BrowserLinkInlineOperation("v/application.wasm", "v/tmp/sanitized.wasm",
            optimization == FinalWasmOptimization.Oz ? WasmInlineAnnotationSelection.Preserve : WasmInlineAnnotationSelection.SizeAware),
            plan.Operations.OfType<BrowserLinkInlineOperation>().First());
        Assert.Equal(new BrowserLinkInlineOperation("v/runtime.wasm", "v/tmp/runtime-sanitized.wasm",
            WasmInlineAnnotationSelection.SizeAware), plan.Operations.OfType<BrowserLinkInlineOperation>().Skip(1).First());
        Assert.Equal(request.InternalRuntimeExports, plan.Operations.OfType<BrowserLinkExportOperation>().SingleOrDefault()!.RemovedExports);
        Assert.Equal("v/tmp/merged.wasm", plan.Operations.OfType<BrowserLinkExportOperation>().Single().InputPath);
        Assert.Equal("v/tmp/sanitized.wasm", plan.Operations.OfType<BrowserLinkExportOperation>().Single().OutputPath);
        Assert.Equal(new BrowserLinkMoveOperation("v/tmp/linked.wasm", "v/output.wasm"), plan.Operations[^1]);
        Assert.Contains("v/tmp/linked.wasm", plan.TemporaryPaths);
        if (optimization == FinalWasmOptimization.None)
        {
            Assert.Null(plan.Operations.OfType<BrowserLinkToolOperation>().Skip(1).SingleOrDefault());
            Assert.Equal(new BrowserLinkInlineOperation("v/tmp/sanitized.wasm", "v/tmp/linked.wasm",
                WasmInlineAnnotationSelection.Remove), plan.Operations.OfType<BrowserLinkInlineOperation>().Last());
            Assert.Equal("v/tmp/sanitized.wasm", plan.Operations.OfType<BrowserLinkValidationOperation>().Single().Path);
        }
        else
        {
            Assert.Equal(BinaryenToolIds.WasmOpt, plan.Operations.OfType<BrowserLinkToolOperation>().Skip(1).SingleOrDefault()!.ToolId);
            Assert.Equal(new BrowserLinkInlineOperation("v/tmp/linked.wasm", "v/tmp/linked.wasm",
                WasmInlineAnnotationSelection.Remove), plan.Operations.OfType<BrowserLinkInlineOperation>().Last());
            Assert.Equal("v/tmp/linked.wasm", plan.Operations.OfType<BrowserLinkValidationOperation>().Single().Path);
        }
    }

    [Fact]
    public void OmitsPruningWhenNoTemporaryExportsExist()
    {
        var plan = BrowserRawCoreModules.CreateLinkPlan(Request(FinalWasmOptimization.None), Workspace());

        Assert.Null(plan.Operations.OfType<BrowserLinkExportOperation>().SingleOrDefault());
        Assert.Equal(new BrowserLinkInlineOperation("v/tmp/merged.wasm", "v/tmp/linked.wasm",
            WasmInlineAnnotationSelection.Remove), plan.Operations.OfType<BrowserLinkInlineOperation>().Last());
        Assert.Equal("v/tmp/merged.wasm", plan.Operations.OfType<BrowserLinkValidationOperation>().Single().Path);
    }

    [Fact]
    public void RejectsAliasedAndUninitializedInputs()
    {
        Assert.Throws<ArgumentException>(() => BrowserRawCoreModules.CreateLinkPlan(
            Request(FinalWasmOptimization.Oz), Workspace() with { LinkedModulePath = "v/application.wasm" }));
        Assert.Throws<NetWasm.Compiler.Core.CompilerException>(() => BrowserRawCoreModules.CreateLinkPlan(
            Request(FinalWasmOptimization.Oz) with { InternalRuntimeExports = default }, Workspace()));
    }

    [Fact]
    public void RemovesTheExactPlannedExportsFromAnInMemoryModule()
    {
        var module = Convert.FromHexString(
            "0061736D010000000709020161000001620001");

        var rewritten = BrowserRawCoreModules.RemoveExports(
            module, [new("a", 0)]);

        Assert.Equal("0061736D0100000007050101620001", Convert.ToHexString(rewritten));
    }

    private static RawModuleLinkRequest Request(FinalWasmOptimization optimization) =>
        new("v/application.wasm", "v/runtime.wasm", "v/output.wasm", ComponentTarget.Wasm32Wasi02, optimization);

    private static BrowserRawCoreModuleWorkspace Workspace() => new("v/tmp", "v/tmp/linked.wasm");
}
