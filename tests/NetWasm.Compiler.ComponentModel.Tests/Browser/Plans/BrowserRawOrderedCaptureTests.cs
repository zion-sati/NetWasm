using NetWasm.Compiler.ComponentModel.Browser;
using NetWasm.Compiler.ComponentModel.Browser.Plans;
using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserRawOrderedCaptureTests
{
    public static TheoryData<FinalWasmOptimization, bool, bool> Modes
    {
        get
        {
            var data = new TheoryData<FinalWasmOptimization, bool, bool>();
            foreach (var mode in Enum.GetValues<FinalWasmOptimization>())
                foreach (var memory64 in new[] { false, true })
                    foreach (var prune in new[] { false, true })
                        data.Add(mode, memory64, prune);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void RecordsValidatedPublicationWithOptionalPruning(FinalWasmOptimization mode, bool memory64, bool prune)
    {
        var request = new RawModuleLinkRequest("v/app.wasm", "v/runtime.wasm", "v/output.wasm",
            memory64 ? ComponentTarget.Wasm64Wasi02 : ComponentTarget.Wasm32Wasi02, mode)
        {
            InternalApplicationExports = prune ? [new("internal", 0)] : [],
        };
        var captured = BrowserRawCoreModules.CreateOrderedLinkPlan(request, new("v/tmp", "v/tmp/linked.wasm"));
        var operations = captured.Operations;

        Assert.IsType<BrowserLinkTextOperation>(operations[0]);
        Assert.Equal(request.ApplicationModulePath, Assert.IsType<BrowserLinkInlineOperation>(operations[1]).InputPath);
        Assert.Equal(request.RuntimeModulePath, Assert.IsType<BrowserLinkInlineOperation>(operations[2]).InputPath);
        Assert.Equal("wasm-merge", Assert.IsType<BrowserLinkToolOperation>(operations[3]).ToolId);
        Assert.Equal(prune ? 1 : 0, operations.OfType<BrowserLinkExportOperation>().Count());
        if (prune)
            Assert.Equal(request.InternalApplicationExports, Assert.IsType<BrowserLinkExportOperation>(operations[4]).RemovedExports);
        Assert.Equal(mode is FinalWasmOptimization.None or FinalWasmOptimization.O0 ? 1 : 2,
            operations.OfType<BrowserLinkToolOperation>().Count());
        var publication = Assert.IsType<BrowserLinkMoveOperation>(operations[^1]);
        Assert.Equal("v/tmp/linked.wasm", publication.InputPath);
        Assert.Equal(request.OutputPath, publication.OutputPath);
        var capabilities = prune
            ? new[] { "text", "inline", "tool", "exports", "validate", "move" }
            : new[] { "text", "inline", "tool", "validate", "move" };
        var plan = BrowserRawCoreModules.CreateOrderedLinkPlan(request, new("v/tmp", "v/tmp/linked.wasm"));
        Assert.Equal(capabilities.Order(), plan.RequiredCapabilities.Order());
        Assert.Equal(operations.Length, plan.Operations.Length);
        var serializer = new BrowserLinkPlanSerializer();
        Assert.Equal(serializer.Serialize(captured), serializer.Serialize(plan));

        BrowserLinkPlanValidation.Create().Validate(plan);
    }
}
