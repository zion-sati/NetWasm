using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class EnumValueBoxEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Store)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I32Store)]
    public void BoxesAnEnumValueThroughItsCapability(
        WasmTarget target,
        byte expectedStore)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = Assert.IsAssignableFrom<IEnumValueBoxEmitter>(
            new EnumValueBoxEmitter(
                layouts,
                layouts,
                WasmRuntimeImports.CreateCatalog(),
                new AddressInstructionEmitter(layouts),
                layouts));
        var underlying = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var entry = new EnumMetadataLayout(
            new EntityKey(new AssemblyIdentity("EnumValueBoxTests"), 0x02000001),
            7,
            64,
            underlying,
            false,
            []);
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Emit(code, entry, valueLocal: 1, resultLocal: 2);

        Assert.Contains(WasmOpcodes.Call, code.ToArray());
        Assert.Contains(expectedStore, code.ToArray());
        Assert.Equal(entry.Type, layouts.ObjectRequest);
    }
}
