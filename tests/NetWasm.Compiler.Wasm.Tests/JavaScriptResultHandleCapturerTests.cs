using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class JavaScriptResultHandleCapturerTests
{
    [Fact]
    public void CapturesTheDescriptorHandleAndLeavesTheValueFrame()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var code = new RecordingInstructionWriter();
        var capturer = ThroughContract(new JavaScriptResultHandleCapturer(imports));

        capturer.Capture(code, CreateMethodEmissionContext());

        var bytes = code.ToArray();
        byte[] expectedPrefix =
        [
            WasmOpcodes.LocalGet, 23, WasmOpcodes.I32Load, 2, 0,
            WasmOpcodes.LocalSet, 25,
        ];
        Assert.True(bytes.AsSpan().StartsWith(expectedPrefix));
        Assert.True(bytes.AsSpan().IndexOf(
            [WasmOpcodes.Call,
                (byte)imports.Resolve(RuntimeImportSymbol.ValueFrameLeave)]) >= 0);
    }

    private static IJavaScriptResultHandleCapturer ThroughContract(
        JavaScriptResultHandleCapturer capturer) => new[]
        {
            capturer,
        }.Cast<IJavaScriptResultHandleCapturer>().Single();
}
