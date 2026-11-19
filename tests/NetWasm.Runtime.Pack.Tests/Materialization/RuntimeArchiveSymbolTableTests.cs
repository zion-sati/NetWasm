// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Text;
using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeArchiveSymbolTableTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsTheSymbolsAGnuSymbolTableNames(bool wide)
    {
        var symbols = RuntimeArchiveSymbolTable.Parse(GnuArchive(wide, "glClear", "emscripten_has_asyncify", "glClear"));

        Assert.Equal(["emscripten_has_asyncify", "glClear"], symbols.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void RejectsWhatIsNotAnArchiveWithASymbolTable()
    {
        Assert.Throws<InvalidOperationException>(() => RuntimeArchiveSymbolTable.Parse("not an archive"u8));
        Assert.Throws<InvalidOperationException>(() => RuntimeArchiveSymbolTable.Parse("!<arch>\n"u8));
        var member = Encoding.ASCII.GetBytes("!<arch>\n" + Header("object.o/", 0));
        Assert.Throws<InvalidOperationException>(() => RuntimeArchiveSymbolTable.Parse(member));
        var archive = GnuArchive(false, "glClear");
        Assert.Throws<InvalidOperationException>(() => RuntimeArchiveSymbolTable.Parse(archive.AsSpan(0, archive.Length - 2)));
        foreach (var size in new[] { "x", "-1", "2" })
        {
            var bad = (byte[])archive.Clone();
            Encoding.ASCII.GetBytes(size.PadRight(10)).CopyTo(bad, 8 + 48);
            Assert.Throws<InvalidOperationException>(() => RuntimeArchiveSymbolTable.Parse(bad));
        }
        // A count larger than the table, and names without their terminator.
        var counted = (byte[])archive.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(counted.AsSpan(68), 9);
        Assert.Throws<InvalidOperationException>(() => RuntimeArchiveSymbolTable.Parse(counted));
        var unterminated = (byte[])archive.Clone();
        unterminated[^1] = (byte)'x';
        Assert.Throws<InvalidOperationException>(() => RuntimeArchiveSymbolTable.Parse(unterminated));
    }

    // An archive whose only member is its symbol table, as llvm-ar writes it ahead of the objects.
    internal static byte[] GnuArchive(bool wide, params string[] symbols)
    {
        var width = wide ? 8 : 4;
        var names = Encoding.ASCII.GetBytes(string.Concat(symbols.Select(symbol => symbol + "\0")));
        var table = new byte[width * (symbols.Length + 1) + names.Length];
        if (wide) BinaryPrimitives.WriteUInt64BigEndian(table, (ulong)symbols.Length);
        else BinaryPrimitives.WriteUInt32BigEndian(table, (uint)symbols.Length);
        names.CopyTo(table, width * (symbols.Length + 1));
        return [.. Encoding.ASCII.GetBytes("!<arch>\n" + Header(wide ? "/SYM64/" : "/", table.Length)), .. table];
    }

    private static string Header(string name, int size) =>
        name.PadRight(16) + "0".PadRight(12) + "0".PadRight(6) + "0".PadRight(6) + "644".PadRight(8)
        + size.ToString(System.Globalization.CultureInfo.InvariantCulture).PadRight(10) + "`\n";
}
