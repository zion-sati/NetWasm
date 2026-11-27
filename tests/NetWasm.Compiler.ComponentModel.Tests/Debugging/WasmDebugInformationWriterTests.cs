// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Debugging;
using static NetWasm.Compiler.ComponentModel.Tests.Debugging.DebugModuleBuilder;

namespace NetWasm.Compiler.ComponentModel.Tests.Debugging;

public sealed class WasmDebugInformationWriterTests
{
    private static readonly byte[] Symbols = MethodSymbolsBuilder.Build(
        [(1, "Program::Main"), (2, "Program::Main in Program.cs:line 7"), (3, "Program::Other")]);

    [Fact]
    public void WritesNamesAndDwarfFromTheMarkersAndReplacesEarlierOnes()
    {
        var plain = Module(Marker(1, 2), Marker(3, 3), Op(Nop));
        var module = WasmCustomSections.Replace(
            WasmDebugLineScannerTests.WithNames(plain, (2, "Program.Main"), (3, "kept"), (4, "helper")),
            _ => false,
            [new(".debug_str", [1])]);
        var files = new MemoryFiles { ["module.wasm"] = module, ["symbols"] = Symbols };

        new WasmDebugInformationWriter(files, files).Write("module.wasm", new("symbols", "/src"));

        // The marker names a function only where the module has no name for it.
        var expected = WasmCustomSections.Replace(
            WasmDebugLineScannerTests.WithNames(plain, (2, "Program.Main"), (3, "kept"), (4, "helper")),
            name => name == "name",
            [new("name", FunctionNames((2, "Program.Main"), (3, "kept"), (4, "helper"))), .. Dwarf(plain)]);
        Assert.Equal(expected[..plain.Length], files["module.wasm"][..plain.Length]);
        Assert.Equal(
            Sections(expected).Select(section => (section.Name, Hex(section.Payload))),
            Sections(files["module.wasm"]).Select(section => (section.Name, Hex(section.Payload))));
    }

    [Fact]
    public void NamesTheFunctionsWithMarkersInAModuleWithoutNames()
    {
        var plain = Module(Marker(1, 2), Marker(3, 3), Op(Nop));
        var files = new MemoryFiles { ["module.wasm"] = plain, ["symbols"] = Symbols };

        new WasmDebugInformationWriter(files, files).Write("module.wasm", new("symbols", "/src"));

        Assert.Equal(
            [("producers", Hex(Section(plain, "producers"))), ("name", Hex(FunctionNames((2, "Program.Main"), (3, "Program.Other")))),
                .. Dwarf(plain).Select(section => (section.Name, Hex(section.Payload)))],
            Sections(files["module.wasm"]).Select(section => (section.Name, Hex(section.Payload))));
    }

    [Fact]
    public void AModuleWithoutMarkersOrNamesGainsNothing()
    {
        var plain = Module(Op(Nop));
        var files = new MemoryFiles { ["module.wasm"] = plain, ["symbols"] = Symbols };

        new WasmDebugInformationWriter(files, files).Write("module.wasm", new("symbols", null));

        Assert.Equal(plain, files["module.wasm"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void LeavesTheModuleAloneWithoutMethodSymbols(string? symbols)
    {
        var files = new MemoryFiles();

        new WasmDebugInformationWriter(files, files).Write("module.wasm", new(symbols, "/src"));

        Assert.Empty(files.Reads);
        Assert.Empty(files.Writes);
    }

    [Fact]
    public void RejectsMissingArguments()
    {
        var files = new MemoryFiles();
        Assert.Throws<ArgumentNullException>(() => new WasmDebugInformationWriter(null!, files));
        Assert.Throws<ArgumentNullException>(() => new WasmDebugInformationWriter(files, null!));
        var writer = new WasmDebugInformationWriter(files, files);
        Assert.Throws<ArgumentException>(() => writer.Write(" ", new()));
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!, new()));
        Assert.Throws<ArgumentNullException>(() => writer.Write("module.wasm", null!));
    }

    private static IReadOnlyList<WasmCustomSection> Dwarf(byte[] module) =>
        DwarfLineSections.Encode(
            WasmDebugLineScanner.Scan(module, MethodSymbolsFormat.Parse(Symbols)),
            "/src");

    private static byte[] FunctionNames(params (uint Function, string Name)[] names)
    {
        var entries = new List<byte>(Unsigned((uint)names.Length));
        foreach (var (function, name) in names)
        {
            entries.AddRange(Unsigned(function));
            entries.AddRange(Name(name));
        }
        return [1, .. Unsigned((uint)entries.Count), .. entries];
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

    private static byte[] Section(byte[] module, string name) =>
        Sections(module).Single(section => section.Name == name).Payload;

    // Each custom section's name and payload, in order.
    private static List<(string Name, byte[] Payload)> Sections(byte[] module)
    {
        var result = new List<(string, byte[])>();
        var at = 8;
        while (at < module.Length)
        {
            var id = module[at++];
            var size = ReadUnsigned(module, ref at);
            var end = at + size;
            if (id == 0)
            {
                var length = ReadUnsigned(module, ref at);
                result.Add((System.Text.Encoding.UTF8.GetString(module, at, length), module[(at + length)..end]));
            }
            at = end;
        }
        return result;
    }

    private static int ReadUnsigned(byte[] bytes, ref int at)
    {
        var (value, shift) = (0, 0);
        byte next;
        do
        {
            next = bytes[at++];
            value |= (next & 0x7f) << shift;
            shift += 7;
        }
        while ((next & 0x80) != 0);
        return value;
    }

    private sealed class MemoryFiles : Dictionary<string, byte[]>, IByteFileReader, IByteFileWriter
    {
        public List<string> Reads { get; } = [];
        public List<string> Writes { get; } = [];

        public byte[] Read(string path)
        {
            Reads.Add(path);
            return this[path];
        }

        public void Write(string path, byte[] content)
        {
            Writes.Add(path);
            this[path] = content;
        }
    }
}
