using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

public sealed class WasmIndexOperandScannerTests
{
    // One local group, then: i32.const 5; call 300; throw 0; block (type 64) ... end;
    // try_table (catch 1 0) (catch_all 1) ... end; memory.copy 0 0; i32.load align=2 offset=8;
    // v128.const; i32x4.extract_lane 3; br_table 0 1 0; end.
    private static readonly byte[] Body =
    [
        0x01, 0x02, 0x7F,
        0x41, 0x05,
        0x10, 0xAC, 0x02,
        0x08, 0x00,
        0x02, 0xC0, 0x00, 0x0B,
        0x1F, 0x40, 0x02, 0x00, 0x01, 0x00, 0x02, 0x01, 0x0B,
        0xFC, 0x0A, 0x00, 0x00,
        0x28, 0x02, 0x08,
        0xFD, 0x0C, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
        0xFD, 0x1B, 0x03,
        0x0E, 0x02, 0x00, 0x01, 0x00,
        0x0B,
    ];

    [Fact]
    public void ScanFindsEveryIndexWithItsPlaceAndEncoding()
    {
        var operands = new WasmIndexOperandScanner().Scan(Body);

        Assert.Equal(
        [
            new WasmIndexOperand(6, 2, WasmIndexSpace.Function, 300),
            new WasmIndexOperand(9, 1, WasmIndexSpace.Tag, 0),
            new WasmIndexOperand(11, 2, WasmIndexSpace.Type, 64, IsSigned33: true),
            new WasmIndexOperand(18, 1, WasmIndexSpace.Tag, 1),
            new WasmIndexOperand(25, 1, WasmIndexSpace.Memory, 0),
            new WasmIndexOperand(26, 1, WasmIndexSpace.Memory, 0),
        ], operands);
    }

    [Fact]
    public void RewritingWithTheSameIndicesReproducesTheBody()
    {
        var operands = new WasmIndexOperandScanner().Scan(Body);

        Assert.Equal(Body, new WasmIndexOperandRewriter().Rewrite(Body, operands, new Shift(0)));
    }

    [Fact]
    public void RewritingReencodesEachIndexAtItsMinimalLength()
    {
        var scanner = new WasmIndexOperandScanner();
        var remapped = new WasmIndexOperandRewriter().Rewrite(
            Body, scanner.Scan(Body), new Table(new()
            {
                [(WasmIndexSpace.Function, 300)] = 5,
                [(WasmIndexSpace.Type, 64)] = 63,
                [(WasmIndexSpace.Tag, 1)] = 200,
            }));

        Assert.Equal(Body.Length - 1 - 1 + 1, remapped.Length);
        Assert.Equal(
        [
            new WasmIndexOperand(6, 1, WasmIndexSpace.Function, 5),
            new WasmIndexOperand(8, 1, WasmIndexSpace.Tag, 0),
            new WasmIndexOperand(10, 1, WasmIndexSpace.Type, 63, IsSigned33: true),
            new WasmIndexOperand(16, 2, WasmIndexSpace.Tag, 200),
            new WasmIndexOperand(24, 1, WasmIndexSpace.Memory, 0),
            new WasmIndexOperand(25, 1, WasmIndexSpace.Memory, 0),
        ], scanner.Scan(remapped));
        // A non-negative s33 whose seventh bit is set needs a continuation byte.
        var widened = new WasmIndexOperandRewriter().Rewrite(
            remapped, scanner.Scan(remapped), new Table(new() { [(WasmIndexSpace.Type, 63)] = 64 }));
        Assert.Equal(new WasmIndexOperand(10, 2, WasmIndexSpace.Type, 64, IsSigned33: true),
            scanner.Scan(widened)[2]);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0xFB, 0x00 })]
    [InlineData(new byte[] { 0x00, 0xFC, 0x20 })]
    [InlineData(new byte[] { 0x00, 0x10 })]
    [InlineData(new byte[] { 0x00, 0x1F, 0x40, 0x01, 0x07 })]
    [InlineData(new byte[] { 0x01, 0x01, 0x63, 0x0B })]
    public void ScanRejectsWhatItCannotDecode(byte[] body)
    {
        Assert.Throws<InvalidDataException>(() => new WasmIndexOperandScanner().Scan(body));
    }

    [Fact]
    public void RewriteRejectsOperandsThatAreNotTheBodysInOrder()
    {
        var operands = new WasmIndexOperandScanner().Scan(Body);

        Assert.Throws<ArgumentException>(() => new WasmIndexOperandRewriter().Rewrite(
            Body, [operands[1], operands[0]], new Shift(0)));
        Assert.Throws<ArgumentNullException>(() => new WasmIndexOperandRewriter().Rewrite(
            Body, operands, null!));
    }

    private sealed class Shift(uint by) : IWasmIndexRemapping
    {
        public uint Remap(WasmIndexSpace space, uint index) => index + by;
    }

    private sealed class Table(Dictionary<(WasmIndexSpace, uint), uint> map) : IWasmIndexRemapping
    {
        public uint Remap(WasmIndexSpace space, uint index) =>
            map.TryGetValue((space, index), out var mapped) ? mapped : index;
    }
}
