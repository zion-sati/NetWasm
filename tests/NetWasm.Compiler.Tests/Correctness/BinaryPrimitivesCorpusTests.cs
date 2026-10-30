namespace NetWasm.Compiler.Tests.Correctness;

// Base64, Base64Url, the hex converter, BitConverter, BinaryPrimitives, UInt128 and BitOperations
// are upstream's sources, with their vectorized paths. Each runs over inputs of every length class
// and edge value and compares with desktop.
[Collection(CorrectnessTestGroup.Name)]
public sealed class BinaryPrimitivesCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void BinaryPrimitivesMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "BinaryPrimitivesCorpus",
            "NetWasm.Correctness.BinaryPrimitivesCorpus",
            """
            using System;
            using System.Buffers;
            using System.Buffers.Binary;
            using System.Buffers.Text;
            using System.Numerics;

            namespace NetWasm.Correctness.BinaryPrimitivesCorpus;

            public static class EntryPoint
            {
                private static readonly int[] Lengths = [0, 1, 2, 3, 4, 5, 11, 12, 15, 16, 17, 23, 24, 31, 32, 33, 47, 48, 64, 95, 96, 100, 255];

                public static int Run(int input) => input switch
                {
                    0 => Base64Text(),
                    1 => Base64Utf8(),
                    2 => Base64UrlText(),
                    3 => Hex(),
                    4 => Converter(),
                    5 => Endianness(),
                    6 => WideIntegers(),
                    _ => Bits(),
                };

                private static byte[] Data(int length, int seed)
                {
                    var data = new byte[length];
                    for (var index = 0; index < length; index++) data[index] = (byte)(index * 37 + seed * 11);
                    return data;
                }

                private static int Base64Text()
                {
                    var hash = 17;
                    foreach (var length in Lengths)
                    {
                        var data = Data(length, 1);
                        var text = Convert.ToBase64String(data);
                        hash = Mix(hash, Text(text));
                        hash = Mix(hash, Bytes(Convert.FromBase64String(text)));
                        hash = Mix(hash, Text(Convert.ToBase64String(data, Base64FormattingOptions.InsertLineBreaks)));
                        var chars = new char[length * 2 + 4];
                        hash = Mix(hash, Convert.TryToBase64Chars(data, chars, out var written) ? written : -1);
                        var decoded = new byte[length + 3];
                        hash = Mix(hash, Convert.TryFromBase64Chars(chars.AsSpan(0, written), decoded, out var read) ? read : -1);
                        if (length > 0)
                        {
                            var broken = text.ToCharArray();
                            broken[broken.Length / 2] = '*';
                            try { hash = Mix(hash, Convert.FromBase64String(new string(broken)).Length); }
                            catch (FormatException) { hash = Mix(hash, -2); }
                        }
                    }
                    return hash;
                }

                private static int Base64Utf8()
                {
                    var hash = 19;
                    foreach (var length in Lengths)
                    {
                        var data = Data(length, 2);
                        var encoded = new byte[Base64.GetMaxEncodedToUtf8Length(length)];
                        var status = Base64.EncodeToUtf8(data, encoded, out var consumed, out var written);
                        hash = Mix(Mix(Mix(hash, (int)status), consumed), written);
                        hash = Mix(hash, Bytes(encoded.AsSpan(0, written).ToArray()));
                        var decoded = new byte[Base64.GetMaxDecodedFromUtf8Length(written)];
                        status = Base64.DecodeFromUtf8(encoded.AsSpan(0, written), decoded, out consumed, out var decodedLength);
                        hash = Mix(Mix(Mix(hash, (int)status), consumed), decodedLength);
                        hash = Mix(hash, Base64.IsValid(encoded.AsSpan(0, written)) ? 1 : 0);
                        var small = new byte[written / 2];
                        status = Base64.EncodeToUtf8(data, small, out consumed, out var partial, isFinalBlock: false);
                        hash = Mix(Mix(Mix(hash, (int)status), consumed), partial);
                        var inPlace = new byte[Base64.GetMaxEncodedToUtf8Length(length)];
                        data.CopyTo(inPlace, 0);
                        status = Base64.EncodeToUtf8InPlace(inPlace, length, out var inPlaceWritten);
                        hash = Mix(Mix(hash, (int)status), inPlaceWritten);
                    }
                    return hash;
                }

                private static int Base64UrlText()
                {
                    var hash = 23;
                    foreach (var length in Lengths)
                    {
                        var data = Data(length, 3);
                        var text = Base64Url.EncodeToString(data);
                        hash = Mix(hash, Text(text));
                        hash = Mix(hash, Bytes(Base64Url.DecodeFromChars(text)));
                        hash = Mix(hash, Base64Url.IsValid(text) ? 1 : 0);
                        var utf8 = Base64Url.EncodeToUtf8(data);
                        hash = Mix(hash, Bytes(utf8));
                        hash = Mix(hash, Base64Url.GetEncodedLength(length) * 3 + Base64Url.GetMaxDecodedLength(utf8.Length));
                    }
                    return hash;
                }

                private static int Hex()
                {
                    var hash = 29;
                    foreach (var length in Lengths)
                    {
                        var data = Data(length, 4);
                        var upper = Convert.ToHexString(data);
                        var lower = Convert.ToHexStringLower(data);
                        hash = Mix(Mix(hash, Text(upper)), Text(lower));
                        hash = Mix(hash, Bytes(Convert.FromHexString(lower)));
                        var bytes = new byte[length];
                        var status = Convert.FromHexString(upper.AsSpan(), bytes, out var consumed, out var written);
                        hash = Mix(Mix(Mix(hash, (int)status), consumed), written);
                        if (length > 0)
                        {
                            var broken = upper.ToCharArray();
                            broken[^1] = 'G';
                            status = Convert.FromHexString(new string(broken).AsSpan(), bytes, out consumed, out written);
                            hash = Mix(Mix(Mix(hash, (int)status), consumed), written);
                        }
                        hash = Mix(hash, Text(BitConverter.ToString(data)));
                    }
                    return hash;
                }

                private static int Converter()
                {
                    var hash = 31;
                    hash = Mix(hash, Bytes(BitConverter.GetBytes(0x12345678)));
                    hash = Mix(hash, Bytes(BitConverter.GetBytes(-1234567890123L)));
                    hash = Mix(hash, Bytes(BitConverter.GetBytes(3.25)));
                    hash = Mix(hash, Bytes(BitConverter.GetBytes(-0.5f)));
                    hash = Mix(hash, Bytes(BitConverter.GetBytes((Half)1.5f)));
                    hash = Mix(hash, Bytes(BitConverter.GetBytes('x')));
                    hash = Mix(hash, Bytes(BitConverter.GetBytes(true)));
                    var data = Data(16, 5);
                    hash = Mix(hash, BitConverter.ToInt32(data, 3));
                    hash = Mix(hash, (int)BitConverter.ToInt64(data, 5));
                    hash = Mix(hash, (int)BitConverter.ToUInt16(data, 1));
                    hash = Mix(hash, BitConverter.SingleToInt32Bits(BitConverter.ToSingle(data, 2)));
                    hash = Mix(hash, (int)BitConverter.DoubleToInt64Bits(BitConverter.Int64BitsToDouble(0x400921FB54442D18)));
                    hash = Mix(hash, BitConverter.ToBoolean(data, 0) ? 1 : 0);
                    hash = Mix(hash, BitConverter.TryWriteBytes(new byte[3], 5) ? 1 : 0);
                    hash = Mix(hash, BitConverter.IsLittleEndian ? 1 : 0);
                    try { _ = BitConverter.ToInt32(data, 14); }
                    catch (ArgumentException exception) { hash = Mix(hash, exception.ParamName?.Length ?? -1); }
                    try { _ = BitConverter.ToString(data, 10, 9); }
                    catch (ArgumentException exception) { hash = Mix(hash, exception.ParamName?.Length ?? -1); }
                    return hash;
                }

                private static int Endianness()
                {
                    var hash = 37;
                    var buffer = new byte[16];
                    BinaryPrimitives.WriteInt32BigEndian(buffer, 0x01020304);
                    BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(4), -2);
                    BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(12), 0xBEEF);
                    BinaryPrimitives.WriteSingleBigEndian(buffer.AsSpan(8), 1.5f);
                    hash = Mix(hash, Bytes(buffer));
                    hash = Mix(hash, BinaryPrimitives.ReadInt32LittleEndian(buffer));
                    hash = Mix(hash, (int)BinaryPrimitives.ReadUInt64BigEndian(buffer.AsSpan(4)));
                    hash = Mix(hash, BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(12)));
                    hash = Mix(hash, BinaryPrimitives.ReverseEndianness(0x11223344));
                    hash = Mix(hash, (int)BinaryPrimitives.ReverseEndianness(0x1122334455667788UL));
                    hash = Mix(hash, (int)(BinaryPrimitives.ReverseEndianness((UInt128)0x0102030405060708UL) >> 64));
                    var values = new int[] { 1, 0x0A0B0C0D, -1, 77, 1 << 30 };
                    var reversed = new int[5];
                    BinaryPrimitives.ReverseEndianness(values, reversed);
                    foreach (var value in reversed) hash = Mix(hash, value);
                    hash = Mix(hash, BinaryPrimitives.TryReadInt32BigEndian(buffer.AsSpan(14), out var missing) ? missing : -3);
                    return hash;
                }

                private static int WideIntegers()
                {
                    var hash = 41;
                    var a = new UInt128(0x0123456789ABCDEF, 0xFEDCBA9876543210);
                    var b = new UInt128(0, 0x1000000000000001);
                    hash = Mix(hash, a * b); hash = Mix(hash, a / b); hash = Mix(hash, a % b); hash = Mix(hash, a + b);
                    hash = Mix(hash, a - b); hash = Mix(hash, a >> 7); hash = Mix(hash, a << 70); hash = Mix(hash, ~a);
                    hash = Mix(hash, UInt128.LeadingZeroCount(b)); hash = Mix(hash, UInt128.PopCount(a));
                    hash = Mix(hash, UInt128.RotateLeft(a, 13)); hash = Mix(hash, UInt128.Log2(a));
                    var c = (Int128)(-123456789012345L) * 1000000007;
                    hash = Mix(hash, (UInt128)c); hash = Mix(hash, (UInt128)(c / 97)); hash = Mix(hash, (UInt128)Int128.Abs(c));
                    hash = Mix(hash, (UInt128)(double)a); hash = Mix(hash, (int)BitConverter.DoubleToInt64Bits((double)a));
                    hash = Mix(hash, a > b ? 1 : 0); hash = Mix(hash, (UInt128)(c >> 3));
                    try { _ = a / UInt128.Zero; }
                    catch (DivideByZeroException) { hash = Mix(hash, -4); }
                    return hash;
                }

                private static int Bits()
                {
                    var hash = 43;
                    foreach (var value in new uint[] { 0, 1, 2, 3, 0x80000000, 0x7FFFFFFF, 0xFFFFFFFF, 12345, 0x00F00000 })
                    {
                        hash = Mix(hash, BitOperations.LeadingZeroCount(value));
                        hash = Mix(hash, BitOperations.TrailingZeroCount(value));
                        hash = Mix(hash, BitOperations.PopCount(value));
                        hash = Mix(hash, BitOperations.Log2(value));
                        hash = Mix(hash, (int)BitOperations.RotateLeft(value, 5));
                        hash = Mix(hash, (int)BitOperations.RotateRight((ulong)value * 3, 33));
                        hash = Mix(hash, BitOperations.IsPow2(value) ? 1 : 0);
                        hash = Mix(hash, (int)BitOperations.RoundUpToPowerOf2(value & 0x7FFFFFFF));
                        hash = Mix(hash, (int)BitOperations.Crc32C(value, (byte)value));
                        hash = Mix(hash, (int)BitOperations.Crc32C(value, (ulong)value * 0x9E3779B97F4A7C15));
                        hash = Mix(hash, BitOperations.LeadingZeroCount((ulong)value << 17));
                        // nint is 32 bits on Wasm32, so a zero counts to its width there.
                        hash = Mix(hash, Math.Min(BitOperations.TrailingZeroCount((nint)value), 32));
                    }
                    return hash;
                }

                private static int Text(string text)
                {
                    var hash = text.Length;
                    foreach (var c in text) hash = unchecked(hash * 31 + c);
                    return hash;
                }

                private static int Bytes(byte[] bytes)
                {
                    var hash = bytes.Length;
                    foreach (var value in bytes) hash = unchecked(hash * 31 + value);
                    return hash;
                }

                private static int Mix(int hash, int value) => unchecked(hash * 31 + value);
                private static int Mix(int hash, UInt128 value) => Mix(Mix(hash, (int)(ulong)value), (int)(ulong)(value >> 64));

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5, 6, 7])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
