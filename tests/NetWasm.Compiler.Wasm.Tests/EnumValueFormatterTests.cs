using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class EnumValueFormatterTests
{
    [Theory]
    [InlineData("i4", CliValueKind.I4, false, false)]
    [InlineData("i8", CliValueKind.I8, true, true)]
    public void EmitsKnownAndNumericFormattingThroughItsCapability(
        string underlyingName,
        CliValueKind stackKind,
        bool flags,
        bool hasFormat)
    {
        var layouts = new RecordingLayoutProvider();
        var numeric = new RecordingNumericFormatter();
        var emitter = Assert.IsAssignableFrom<IEnumValueFormatter>(
            new EnumValueFormatter(
                layouts,
                layouts,
                layouts,
                layouts,
                new AddressInstructionEmitter(layouts),
                numeric));
        var underlying = CliTypeIdentity.Primitive(underlyingName, stackKind);
        var metadata = new EnumMetadataLayout(
            new EntityKey(new AssemblyIdentity("EnumValueFormatterTests"), 0x02000001),
            7,
            64,
            underlying,
            flags,
            [
                new("Zero", 0, new StringLayout(100, 4, layouts.StringDataOffset)),
                new("One", 1, new StringLayout(104, 3, layouts.StringDataOffset)),
                new("Two", 2, new StringLayout(108, 3, layouts.StringDataOffset)),
            ]);
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Emit(
            code,
            metadata,
            underlying,
            0,
            4,
            hasFormat ? 1 : null,
            2,
            3,
            4);

        Assert.Equal(1, numeric.Calls);
        Assert.Contains(
            stackKind == CliValueKind.I8 ? WasmOpcodes.I64Equal : WasmOpcodes.I32Equal,
            code.ToArray());
    }

    private sealed class RecordingNumericFormatter : IEnumNumericFormatter
    {
        public int Calls { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            CliTypeIdentity underlying,
            int value,
            int payload,
            int? format,
            int result,
            int raw,
            int scratch) => Calls++;
    }
}
