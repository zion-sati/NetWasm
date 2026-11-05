using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

public sealed class WasmCodePatcherTests
{
    // A module with one type, two functions of it, their bodies and a custom section after them.
    private static readonly byte[] Module =
    [
        0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
        0x01, 0x04, 0x01, 0x60, 0x00, 0x00,             // type: () -> ()
        0x03, 0x03, 0x02, 0x00, 0x00,                   // functions: two of type 0
        0x0A, 0x08, 0x02,                               // code: two bodies
        0x02, 0x00, 0x0B,                               //   no locals, end
        0x03, 0x00, 0x01, 0x0B,                         //   no locals, nop, end
        0x00, 0x05, 0x04, 0x6E, 0x6F, 0x74, 0x65,       // custom "note", empty
    ];

    [Fact]
    public void ReplacesOneBodyAndKeepsEverythingElse()
    {
        var patched = WasmCodePatcher.ReplaceBodies(Module, new Dictionary<int, byte[]>
        {
            [1] = [0x00, 0x01, 0x01, 0x0B],
        });

        Assert.NotNull(patched);
        var bodies = WasmCodePatcher.Bodies(patched);
        Assert.Equal([[0x00, 0x0B], [0x00, 0x01, 0x01, 0x0B]], bodies!);
        Assert.Equal(Module[..19], patched![..19]);
        Assert.Equal(Module[^7..], patched[^7..]);
        Assert.Equal(Module.Length + 1, patched.Length);
    }

    [Fact]
    public void ReplacingABodyWithItselfGivesTheSameModule() =>
        Assert.Equal(Module, WasmCodePatcher.ReplaceBodies(Module, new Dictionary<int, byte[]>
        {
            [0] = [0x00, 0x0B],
        }));

    [Fact]
    public void RefusesAPositionPastTheDefinedFunctionsAndMalformedModules()
    {
        Assert.Null(WasmCodePatcher.ReplaceBodies(Module, new Dictionary<int, byte[]> { [2] = [0x00, 0x0B] }));
        Assert.Null(WasmCodePatcher.ReplaceBodies(Module.AsSpan(0, 24), new Dictionary<int, byte[]>()));
        Assert.Null(WasmCodePatcher.ReplaceBodies([1, 2, 3], new Dictionary<int, byte[]>()));
        Assert.Null(WasmCodePatcher.Bodies([1, 2, 3]));
    }
}
