// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Interop;

namespace NetWasm.Compiler.Tests;

// Bitcode written here bit by bit: forms clang does not write for the pinned toolchain (typed pointers, a wrapper
// header, abbreviations of every encoding, a block's trailing words) are read as LLVM defines them, and a stream LLVM
// would not read fails with what is wrong.
public sealed class LlvmBitcodeFunctionTypeReaderTests
{
    private const int Literal = 0;
    private const int Fixed = 1;
    private const int Vbr = 2;
    private const int Array = 3;
    private const int Char6 = 4;
    private const int Blob = 5;

    // f1 is int(long, ptr, fn, i128, ?) through a typed pointer; f2 is void(float, double, ...). The others define
    // nothing linkable or cannot be named: a pointer to no type, a pointer without a pointee, a prototype, internal
    // and private functions, a name past the string table, a type past the table, a short record and a type that is no
    // function.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsTheLinkableFunctionsOfEveryEncoding(bool wrapped)
    {
        var bytes = Module(strings: true);
        if (wrapped)
            bytes = [0xde, 0xc0, 0x17, 0x0b, 0, 0, 0, 0, 20, 0, 0, 0, .. BitConverter.GetBytes(bytes.Length), 0, 0, 0, 0, .. bytes];
        var functions = new Dictionary<string, NativeFunctionType>();

        Assert.True(LlvmBitcodeFunctionTypeReader.IsBitcode(bytes));
        LlvmBitcodeFunctionTypeReader.Read(bytes, functions);

        Assert.Equal(["f1", "f2"], functions.Keys.Order());
        ImmutableArray<CliValueKind> parameters =
            [CliValueKind.I8, CliValueKind.NativeInt, CliValueKind.Unknown, CliValueKind.Unknown, CliValueKind.Unknown];
        Assert.Equal(new NativeFunctionType(parameters, CliValueKind.I4) { Variadic = false }, functions["f1"]);
        Assert.Equal(new NativeFunctionType([CliValueKind.F4, CliValueKind.F8], CliValueKind.Void) { Variadic = true },
            functions["f2"]);
    }

    // A module without a string table names nothing; zero padding ends the stream; a short buffer is no bitcode.
    [Fact]
    public void WithoutAStringTableOrModuleNothingIsRead()
    {
        var functions = new Dictionary<string, NativeFunctionType>();

        LlvmBitcodeFunctionTypeReader.Read(Module(strings: false), functions);
        LlvmBitcodeFunctionTypeReader.Read(new BitWriter().Fixed(0, 2).Bytes(), functions);

        Assert.Empty(functions);
        Assert.False(LlvmBitcodeFunctionTypeReader.IsBitcode([0x42, 0x43]));
        Assert.False(LlvmBitcodeFunctionTypeReader.IsBitcode([0, 0, 0, 0]));
    }

    public static TheoryData<string, string> Malformed => new()
    {
        { "top-level record", "bitcode has a top-level record" },
        { "info abbreviation", "an abbreviation before SETBID" },
        { "unknown abbreviation", "an unknown abbreviation" },
        { "unknown encoding", "an unknown abbreviation encoding" },
        { "no code", "an abbreviated record without a code" },
        { "array of arrays", "an array of arrays or blobs" },
        { "wide field", "a field wider than 64 bits" },
        { "negative field", "a field wider than 64 bits" },
        { "narrow vbr", "a VBR field narrower than 2 bits" },
        { "long vbr", "a VBR value longer than 64 bits" },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void BitcodeLlvmWouldNotReadFails(string problem, string message)
    {
        var writer = new BitWriter();
        _ = problem switch
        {
            "top-level record" => writer.Fixed(3, 2),
            "info abbreviation" => writer.Enter(0).Abbreviation((Literal, 1)).End(),
            "unknown abbreviation" => writer.Enter(8).Fixed(4, writer.Width).End(),
            "unknown encoding" => writer.Enter(8).Fixed(2, writer.Width).Vbr(1, 5).Fixed(0, 1).Fixed(6, 3).End(),
            "no code" => writer.Enter(8).Abbreviation().Fixed(4, writer.Width).End(),
            "array of arrays" => writer.Enter(8).Abbreviation((Literal, 1), (Array, 0), (Array, 0)).Fixed(4, writer.Width)
                .Vbr(1, 6).End(),
            "wide field" => writer.Enter(8).Abbreviation((Fixed, 65)).Fixed(4, writer.Width).End(),
            "negative field" => writer.Enter(8).Abbreviation((Fixed, 0x80000000)).Fixed(4, writer.Width).End(),
            "narrow vbr" => writer.Enter(8).Abbreviation((Vbr, 1)).Fixed(4, writer.Width).End(),
            // Thirteen continued five-bit pieces carry 65 bits.
            _ => Repeat(writer.Enter(8).Fixed(3, writer.Width), 14, piece => piece.Fixed(0b111111, 6)),
        };

        var exception = Assert.Throws<FormatException>(() =>
            LlvmBitcodeFunctionTypeReader.Read(writer.Bytes(), new Dictionary<string, NativeFunctionType>()));

        Assert.Equal(message, exception.Message);
    }

    private static BitWriter Repeat(BitWriter writer, int count, Func<BitWriter, BitWriter> write)
    {
        for (var index = 0; index < count; index++) write(writer);
        return writer;
    }

    private static byte[] Module(bool strings)
    {
        const string Names = "f1f2f3f4f5f6f7f8f9fA";
        var writer = new BitWriter();
        // BLOCKINFO gives the type block a FUNCTION abbreviation: its code, whether it is variadic, then the result and
        // the parameters as an array of six-bit VBRs.
        writer.Enter(0).Record(1, 17).Abbreviation((Literal, 21), (Fixed, 1), (Array, 0), (Vbr, 6)).End();
        writer.Enter(8).Record(1, 2);
        writer.Enter(17).Abbreviation((Literal, 19), (Array, 0), (Char6, 0))
            .Record(1, 15)                      // NUMENTRY
            .Record(7, 32)                      // 0: i32
            .Record(7, 64)                      // 1: i64
            .Record(7, 128)                     // 2: i128, which no Wasm value holds
            .Record(7)                          // 3: an integer without a width
            .Fixed(4, writer.Width).Fixed(0, 1).Vbr(6, 6).Vbr(0, 6).Vbr(1, 6).Vbr(8, 6).Vbr(4, 6).Vbr(2, 6).Vbr(99, 6) // 4
            .Record(8, 4)                       // 5: a typed pointer to 4
            .Record(8)                          // 6: a pointer without a pointee
            .Record(8, 99)                      // 7: a pointer to no type
            .Record(25, 0)                      // 8: an opaque pointer
            .Record(2)                          // 9: void
            .Record(3)                          // 10: float
            .Record(4)                          // 11: double
            .Record(21, 1)                      // 12: a FUNCTION record too short to be one
            .Record(18, 0, 0)                   // 13: an anonymous struct
            .Fixed(4, writer.Width).Fixed(1, 1).Vbr(3, 6).Vbr(9, 6).Vbr(10, 6).Vbr(11, 6) // 14: void(float, double, ...)
            .Fixed(5, writer.Width).Vbr(3, 6).Fixed(0, 6).Fixed(1, 6).Fixed(2, 6) // STRUCT_NAME "abc" in char6
            .End();
        ulong offset = 0;
        foreach (var (type, prototype, linkage) in new (ulong, ulong, ulong)[]
                 { (5, 0, 0), (14, 0, 0), (7, 0, 0), (6, 0, 0), (4, 1, 0), (4, 0, 3), (4, 0, 9), (99, 0, 0), (9, 0, 0) })
        {
            writer.Record(8, offset, 2, type, 0, prototype, linkage);
            offset += 2;
        }
        writer.Record(8, 1000, 2, 4, 0, 0, 0).Record(8, 0, 2, 4).End();
        if (strings)
        {
            // The string table's blob, a nested block it does not read, and a word past its end.
            writer.Enter(23).Abbreviation((Literal, 1), (Blob, 0)).Enter(99).End().Fixed(4, writer.Width)
                .Blob(Encoding.ASCII.GetBytes(Names)).End(trailingWords: 1);
        }
        return writer.Bytes();
    }

    // LLVM's bitstream as its writer lays it out: little-endian bits, blocks of 32-bit words whose length is written
    // once they end.
    private sealed class BitWriter
    {
        private readonly List<bool> _bits = [];
        private readonly Stack<(int LengthAt, int Width)> _blocks = new();

        public int Width { get; private set; } = 2;

        public BitWriter Fixed(ulong value, int width)
        {
            for (var index = 0; index < width; index++) _bits.Add(((value >> index) & 1) != 0);
            return this;
        }

        public BitWriter Vbr(ulong value, int width)
        {
            var limit = 1UL << (width - 1);
            do
            {
                var piece = value & (limit - 1);
                value >>= width - 1;
                Fixed(piece | (value != 0 ? limit : 0), width);
            }
            while (value != 0);
            return this;
        }

        public BitWriter Enter(int block, int width = 4)
        {
            Fixed(1, Width).Vbr((ulong)block, 8).Vbr((ulong)width, 4).Align32();
            _blocks.Push((_bits.Count, Width));
            Fixed(0, 32);
            Width = width;
            return this;
        }

        public BitWriter End(int trailingWords = 0)
        {
            Fixed(0, Width).Align32();
            for (var word = 0; word < trailingWords; word++) Fixed(0, 32);
            var (at, width) = _blocks.Pop();
            var words = (ulong)(_bits.Count - at - 32) / 32;
            for (var index = 0; index < 32; index++) _bits[at + index] = ((words >> index) & 1) != 0;
            Width = width;
            return this;
        }

        public BitWriter Record(ulong code, params ulong[] operands)
        {
            Fixed(3, Width).Vbr(code, 6).Vbr((ulong)operands.Length, 6);
            foreach (var operand in operands) Vbr(operand, 6);
            return this;
        }

        public BitWriter Abbreviation(params (int Encoding, ulong Value)[] operands)
        {
            Fixed(2, Width).Vbr((ulong)operands.Length, 5);
            foreach (var (encoding, value) in operands)
            {
                if (encoding == Literal)
                {
                    Fixed(1, 1).Vbr(value, 8);
                    continue;
                }
                Fixed(0, 1).Fixed((ulong)encoding, 3);
                if (encoding is LlvmBitcodeFunctionTypeReaderTests.Fixed or LlvmBitcodeFunctionTypeReaderTests.Vbr) Vbr(value, 5);
            }
            return this;
        }

        public BitWriter Blob(byte[] bytes)
        {
            Vbr((ulong)bytes.Length, 6).Align32();
            foreach (var value in bytes) Fixed(value, 8);
            return Align32();
        }

        public byte[] Bytes()
        {
            Align32();
            var bytes = new byte[_bits.Count / 8];
            for (var index = 0; index < _bits.Count; index++)
            {
                if (_bits[index]) bytes[index >> 3] |= (byte)(1 << (index & 7));
            }
            return [0x42, 0x43, 0xc0, 0xde, .. bytes];
        }

        private BitWriter Align32()
        {
            while (_bits.Count % 32 != 0) _bits.Add(false);
            return this;
        }
    }
}
