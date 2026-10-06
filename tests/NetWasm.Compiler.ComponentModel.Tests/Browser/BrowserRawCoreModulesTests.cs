using NetWasm.Compiler.ComponentModel.Browser;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser;

public sealed class BrowserRawCoreModulesTests
{
    private static readonly string[] ExpectedMergePrefix =
    [
        "v/runtime.wasm", "netwasm.runtime.v1", "v/application.wasm",
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

        Assert.Equal("v/tmp/environment.wasm", Assert.Single(plan.TextModules).OutputPath);
        Assert.Equal(BinaryenToolIds.WasmMerge, plan.Merge.ToolId);
        Assert.Equal(ExpectedMergePrefix, plan.Merge.Arguments.Take(6));
        Assert.Equal(request.InternalRuntimeExports, plan.ExportPruning!.RemovedExports);
        Assert.Equal("v/tmp/merged.wasm", plan.ExportPruning.InputPath);
        Assert.Equal("v/tmp/sanitized.wasm", plan.ExportPruning.OutputPath);
        Assert.Equal(new BrowserFileMove("v/tmp/linked.wasm", "v/output.wasm"), plan.Publication);
        Assert.Equal("v/tmp", plan.CleanupDirectory);
        if (optimization == FinalWasmOptimization.None)
        {
            Assert.Null(plan.Optimization);
            Assert.Equal(new BrowserFileCopy("v/tmp/sanitized.wasm", "v/tmp/linked.wasm"), plan.Copy);
            Assert.Equal("v/tmp/sanitized.wasm", plan.Validation.Path);
        }
        else
        {
            Assert.Equal(BinaryenToolIds.WasmOpt, plan.Optimization!.ToolId);
            Assert.Null(plan.Copy);
            Assert.Equal("v/tmp/linked.wasm", plan.Validation.Path);
        }
    }

    [Fact]
    public void OmitsPruningWhenNoTemporaryExportsExist()
    {
        var plan = BrowserRawCoreModules.CreateLinkPlan(Request(FinalWasmOptimization.None), Workspace());

        Assert.Null(plan.ExportPruning);
        Assert.Equal(new BrowserFileCopy("v/tmp/merged.wasm", "v/tmp/linked.wasm"), plan.Copy);
        Assert.Equal("v/tmp/merged.wasm", plan.Validation.Path);
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
