using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class InteropHandleReleaserTests
{
    [Fact]
    public void ReleasesTheArgumentHandleThroughThePlannedHostImport()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var code = new RecordingInstructionWriter();
        var releaser = ThroughContract(imports);
        var target = new InteropMarshallingTarget(new(
            [],
            default,
            default,
            OptionalFunctionIndex.At(23),
            default,
            default,
            default));

        releaser.Release(code, CreateMethodEmissionContext(), target);

        Assert.Equal(
            [WasmOpcodes.LocalGet, 25, WasmOpcodes.Call, 23],
            code.ToArray());
    }

    [Fact]
    public void ReleasesANonzeroResultHandleThroughTheRuntimeImport()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var code = new RecordingInstructionWriter();
        var releaser = ThroughContract(imports);

        releaser.Release(code, CreateMethodEmissionContext());

        var bytes = code.ToArray();
        Assert.Equal(WasmOpcodes.LocalGet, bytes[0]);
        Assert.Contains(WasmOpcodes.I32EqualZero, bytes);
        Assert.Contains(WasmOpcodes.Else, bytes);
        Assert.True(bytes.AsSpan().IndexOf(
            [WasmOpcodes.Call,
                (byte)imports.Resolve(RuntimeImportSymbol.HandleRelease)]) >= 0);
        Assert.Equal(WasmOpcodes.End, bytes[^1]);
    }

    private static IInteropHandleReleaser ThroughContract(
        IRuntimeImportResolver imports) => new[]
        {
            new InteropHandleReleaser(imports),
        }.Cast<IInteropHandleReleaser>().Single();
}
