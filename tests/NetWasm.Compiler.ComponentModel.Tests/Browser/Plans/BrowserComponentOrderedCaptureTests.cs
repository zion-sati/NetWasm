using NetWasm.Compiler.ComponentModel.Browser;
using NetWasm.Compiler.ComponentModel.Browser.Plans;
using NetWasm.Compiler.ComponentModel.Inlining;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserComponentOrderedCaptureTests
{
    [Theory]
    [InlineData(FinalWasmOptimization.None, false)]
    [InlineData(FinalWasmOptimization.O3, false)]
    [InlineData(FinalWasmOptimization.Oz, false)]
    [InlineData(FinalWasmOptimization.None, true)]
    [InlineData(FinalWasmOptimization.O3, true)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    public void RecordsActualProducerOrderIncludingModeDependentValidation(FinalWasmOptimization optimization, bool memory64)
    {
        var request = new ComponentCoreModuleLinkRequest("v/app.wasm", "v/runtime.wasm", "v/output.wasm",
            memory64 ? ComponentTarget.Wasm64Wasi02 : ComponentTarget.Wasm32Wasi02)
        { Optimization = optimization };
        var workspace = new BrowserComponentCoreModuleWorkspace("v/env.wasm", "v/host.wasm", "v/command.wasm",
            "v/merged.wasm", "v/sanitized.wasm", "v/runtime-sanitized.wasm");
        var captured = BrowserComponentCoreModules.CreateOrderedLinkPlan(request, workspace);
        var operations = captured.Operations;

        Assert.IsType<BrowserLinkTextOperation>(operations[0]);
        Assert.Equal(request.ApplicationModulePath, Assert.IsType<BrowserLinkInlineOperation>(operations[1]).InputPath);
        Assert.Equal(request.RuntimeModulePath, Assert.IsType<BrowserLinkInlineOperation>(operations[2]).InputPath);
        Assert.Equal("wasm-merge", Assert.IsType<BrowserLinkToolOperation>(operations[3]).ToolId);
        Assert.IsType<BrowserLinkExportOperation>(operations[4]);
        if (optimization == FinalWasmOptimization.None)
        {
            Assert.Equal(7, operations.Length);
            Assert.IsType<BrowserLinkValidationOperation>(operations[5]);
            Assert.Equal(WasmInlineAnnotationSelection.Remove, Assert.IsType<BrowserLinkInlineOperation>(operations[6]).Selection);
        }
        else
        {
            Assert.Equal(8, operations.Length);
            Assert.Equal("wasm-opt", Assert.IsType<BrowserLinkToolOperation>(operations[5]).ToolId);
            Assert.Equal(WasmInlineAnnotationSelection.Remove, Assert.IsType<BrowserLinkInlineOperation>(operations[6]).Selection);
            Assert.IsType<BrowserLinkValidationOperation>(operations[7]);
        }
        var plan = BrowserComponentCoreModules.CreateOrderedLinkPlan(request, workspace);
        Assert.Equal(operations.Length, plan.Operations.Length);
        Assert.Equal(captured.TemporaryPaths, plan.TemporaryPaths);
        var serializer = new BrowserLinkPlanSerializer();
        Assert.Equal(serializer.Serialize(captured), serializer.Serialize(plan));
        BrowserLinkPlanValidation.Create().Validate(plan);
    }
}
