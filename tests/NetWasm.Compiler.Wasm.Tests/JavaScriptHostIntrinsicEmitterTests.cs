using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class JavaScriptHostIntrinsicEmitterTests
{
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void ReportsTheCompiledHostFlavourAsAConstant(bool javaScriptExportBoundary, byte expected)
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.JavaScriptHost, []);
        var instruction = request.Instruction;
        var target = instruction.Target with
        {
            InteropImports = instruction.Target.InteropImports with
            {
                UseJavaScriptExportBoundary = javaScriptExportBoundary,
            },
        };
        var hosted = request with { Call = request.Call with { Instruction = instruction with { Target = target } } };

        new JavaScriptHostIntrinsicEmitter().Emit(hosted, EmitterTestSupport.GetCodeWriter(instruction));

        var code = EmitterTestSupport.GetCodeBytes(instruction);
        Assert.Equal([WasmOpcodes.I32Constant, expected, WasmOpcodes.LocalSet], code[..3]);
    }
}
