using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NetWasm.Compiler.Tests;

public sealed class EnumAlgorithmsTests
{
    [Theory]
    [InlineData("Read", false, 1UL)]
    [InlineData("read", true, 1UL)]
    [InlineData("Read, Write", false, 3UL)]
    [InlineData(" Write , Read, Read ", false, 3UL)]
    [InlineData("3", false, 3UL)]
    [InlineData("  +3 ", false, 3UL)]
    public unsafe void ParsesNamesCompositesAndNumbers(string text, bool ignoreCase, ulong expected)
    {
        using var descriptor = Descriptor(6, true,
            [(0, "None"), (1, "Read"), (2, "Write")]);

        Assert.Equal(expected, EnumAlgorithms.Parse(descriptor.View, text, ignoreCase));
        Assert.True(EnumAlgorithms.TryParse(descriptor.View, text, ignoreCase, out var result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(1, "-128", 128UL)]
    [InlineData(2, "255", 255UL)]
    [InlineData(3, "-32768", 32768UL)]
    [InlineData(4, "65535", 65535UL)]
    [InlineData(5, "-2147483648", 2147483648UL)]
    [InlineData(6, "4294967295", 4294967295UL)]
    [InlineData(7, "-9223372036854775808", 9223372036854775808UL)]
    [InlineData(8, "18446744073709551615", ulong.MaxValue)]
    [InlineData(9, "65535", 65535UL)]
    public unsafe void ParsesEverySupportedUnderlyingWidth(
        int typeCode, string text, ulong expected)
    {
        using var descriptor = Descriptor(typeCode, false, [(0, "Zero")]);

        Assert.Equal(expected, EnumAlgorithms.Parse(descriptor.View, text, false));
        Assert.True(EnumAlgorithms.TryParse(descriptor.View, text, false, out var result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Unknown")]
    [InlineData("Read,")]
    [InlineData("read")]
    [InlineData("4294967296")]
    [InlineData("1x")]
    [InlineData("+")]
    [InlineData("-")]
    public unsafe void TryParseRejectsInvalidInput(string? text)
    {
        using var descriptor = Descriptor(6, true, [(1, "Read")]);

        Assert.False(EnumAlgorithms.TryParse(descriptor.View, text, false, out var result));
        Assert.Equal(0UL, result);
    }

    [Fact]
    public unsafe void ParsePreservesFailureClasses()
    {
        using var descriptor = Descriptor(2, false, [(1, "One")]);

        Assert.Throws<ArgumentNullException>(() =>
            EnumAlgorithms.Parse(descriptor.View, null, false));
        Assert.Throws<ArgumentException>(() =>
            EnumAlgorithms.Parse(descriptor.View, " ", false));
        Assert.Throws<ArgumentException>(() =>
            EnumAlgorithms.Parse(descriptor.View, "missing", false));
        Assert.Throws<ArgumentException>(() =>
            EnumAlgorithms.Parse(descriptor.View, "1x", false));
        Assert.Throws<ArgumentException>(() =>
            EnumAlgorithms.Parse(descriptor.View, "+", false));
        Assert.Throws<OverflowException>(() =>
            EnumAlgorithms.Parse(descriptor.View, "256", false));
        Assert.Throws<OverflowException>(() =>
            EnumAlgorithms.Parse(descriptor.View, "999", false));
        Assert.Throws<OverflowException>(() =>
            EnumAlgorithms.Parse(descriptor.View, "-1", false));
    }

    [Theory]
    [InlineData("256x")]
    [InlineData("999x")]
    [InlineData("-1x")]
    [InlineData("999, One")]
    public unsafe void ParseReportsInvalidSyntaxBeforeNumericOverflow(string text)
    {
        using var descriptor = Descriptor(2, false, [(1, "One")]);

        Assert.Throws<ArgumentException>(() =>
            EnumAlgorithms.Parse(descriptor.View, text, false));
        Assert.False(EnumAlgorithms.TryParse(
            descriptor.View, text, false, out var result));
        Assert.Equal(0UL, result);
    }

    [Theory]
    [InlineData(null, "Read, Write")]
    [InlineData("G", "Read, Write")]
    [InlineData("g", "Read, Write")]
    [InlineData("F", "Read, Write")]
    [InlineData("f", "Read, Write")]
    [InlineData("D", "3")]
    [InlineData("d", "3")]
    [InlineData("X", "00000003")]
    [InlineData("x", "00000003")]
    public unsafe void FormatsNamesFlagsDecimalAndHex(string? format, string expected)
    {
        using var descriptor = Descriptor(6, true,
            [(0, "None"), (1, "Read"), (2, "Write")]);

        Assert.Equal(expected, EnumAlgorithms.Format(descriptor.View, 3, format));
    }

    [Fact]
    public unsafe void FormattingHandlesExactAliasesZeroAndUnknownBits()
    {
        using var descriptor = Descriptor(2, true,
            [(0, "None"), (1, "First"), (1, "Alias"), (2, "Second")]);

        Assert.Equal("None", EnumAlgorithms.Format(descriptor.View, 0, null));
        Assert.Equal("Alias", EnumAlgorithms.Format(descriptor.View, 1, null));
        Assert.Equal("3", EnumAlgorithms.Format(descriptor.View, 3, "D"));
        Assert.Equal("Alias, Second", EnumAlgorithms.Format(descriptor.View, 3, "F"));
        Assert.Equal("4", EnumAlgorithms.Format(descriptor.View, 4, "G"));
        Assert.Equal("4", EnumAlgorithms.Format(descriptor.View, 4, "F"));
        Assert.Throws<FormatException>(() =>
            EnumAlgorithms.Format(descriptor.View, 1, "Q"));
        Assert.Throws<FormatException>(() =>
            EnumAlgorithms.Format(descriptor.View, 1, "GG"));
    }

    [Fact]
    public unsafe void NonFlagsGeneralFormattingUsesOnlyExactNames()
    {
        using var descriptor = Descriptor(3, false,
            [(1, "One"), (2, "Two")]);

        Assert.Equal("One", EnumAlgorithms.Format(descriptor.View, 1, "G"));
        Assert.Equal("3", EnumAlgorithms.Format(descriptor.View, 3, "G"));
        Assert.Equal("One, Two", EnumAlgorithms.Format(descriptor.View, 3, "F"));
        Assert.Equal("-1", EnumAlgorithms.Format(descriptor.View, ushort.MaxValue, "D"));
        Assert.Equal("FFFF", EnumAlgorithms.Format(descriptor.View, ushort.MaxValue, "X"));
    }

    [Theory]
    [InlineData(1, 255UL, "-1", "FF")]
    [InlineData(2, 255UL, "255", "FF")]
    [InlineData(3, 65535UL, "-1", "FFFF")]
    [InlineData(4, 65535UL, "65535", "FFFF")]
    [InlineData(5, 4294967295UL, "-1", "FFFFFFFF")]
    [InlineData(6, 4294967295UL, "4294967295", "FFFFFFFF")]
    [InlineData(7, ulong.MaxValue, "-1", "FFFFFFFFFFFFFFFF")]
    [InlineData(8, ulong.MaxValue, "18446744073709551615", "FFFFFFFFFFFFFFFF")]
    [InlineData(9, 65535UL, "65535", "FFFF")]
    public unsafe void FormatsEverySupportedUnderlyingWidth(
        int typeCode, ulong raw, string decimalText, string hexadecimalText)
    {
        using var descriptor = Descriptor(typeCode, false, []);

        Assert.Equal(decimalText, EnumAlgorithms.Format(descriptor.View, raw, "D"));
        Assert.Equal(hexadecimalText, EnumAlgorithms.Format(descriptor.View, raw, "X"));
    }

    [Theory]
    [InlineData(1, "128")]
    [InlineData(1, "-129")]
    [InlineData(2, "-1")]
    [InlineData(3, "32768")]
    [InlineData(4, "65536")]
    [InlineData(5, "2147483648")]
    [InlineData(6, "4294967296")]
    [InlineData(7, "9223372036854775808")]
    [InlineData(8, "18446744073709551616")]
    [InlineData(9, "65536")]
    public unsafe void RejectsNumericOverflowAtEveryWidth(int typeCode, string text)
    {
        using var descriptor = Descriptor(typeCode, false, []);

        Assert.False(EnumAlgorithms.TryParse(
            descriptor.View, text, false, out var result));
        Assert.Equal(0UL, result);
        Assert.Throws<OverflowException>(() =>
            EnumAlgorithms.Parse(descriptor.View, text, false));
    }

    [Fact]
    public unsafe void NativeAddressEntrypointsShareTheViewAlgorithms()
    {
        using var descriptor = Descriptor(6, true, [(1, "Read"), (2, "Write")]);

        Assert.Equal(1UL, EnumAlgorithms.Parse(descriptor.Address, "Read", false));
        Assert.True(EnumAlgorithms.TryParse(
            descriptor.Address, "Write", false, out var parsed));
        Assert.Equal(2UL, parsed);
        Assert.Equal("Read, Write", EnumAlgorithms.Format(descriptor.Address, 3, "F"));
    }

    [Fact]
    public unsafe void UnsignedParsingAcceptsNegativeZeroAndFormatsZeroWithoutAName()
    {
        using var descriptor = Descriptor(2, true, [(1, "One")]);

        Assert.Equal(0UL, EnumAlgorithms.Parse(descriptor.View, "-0", false));
        Assert.Equal("0", EnumAlgorithms.Format(descriptor.View, 0, "F"));
    }

    private static unsafe PinnedDescriptor Descriptor(
        int typeCode, bool flags, params (ulong Value, string Name)[] members) =>
        new(typeCode, flags, members);

    private sealed unsafe class PinnedDescriptor : IDisposable
    {
        private readonly ulong[] _values;
        private readonly string[] _names;
        private readonly byte[] _descriptor;
        private readonly GCHandle _valuesPin;
        private readonly GCHandle _descriptorPin;

        internal PinnedDescriptor(
            int typeCode, bool flags, (ulong Value, string Name)[] members)
        {
            _values = members.Select(member => member.Value).ToArray();
            _names = members.Select(member => member.Name).ToArray();
            _descriptor = new byte[16 + (2 * sizeof(nint))];
            _valuesPin = GCHandle.Alloc(_values, GCHandleType.Pinned);
            _descriptorPin = GCHandle.Alloc(_descriptor, GCHandleType.Pinned);
            var address = (byte*)_descriptorPin.AddrOfPinnedObject();
            ((int*)address)[0] = 1;
            ((int*)address)[1] = typeCode;
            ((int*)address)[2] = (flags ? 1 : 0) | 6;
            ((int*)address)[3] = members.Length;
            *(nint*)(address + 16) = _valuesPin.AddrOfPinnedObject();
            *(nint*)(address + 16 + sizeof(nint)) =
                (nint)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(_names));
            View = new EnumMetadataView((nint)address);
            Address = (nint)address;
        }

        internal EnumMetadataView View { get; }
        internal nint Address { get; }

        public void Dispose()
        {
            _descriptorPin.Free();
            _valuesPin.Free();
            GC.KeepAlive(_names);
        }
    }
}
