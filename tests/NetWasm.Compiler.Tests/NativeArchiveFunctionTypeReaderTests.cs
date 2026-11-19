// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Interop;

namespace NetWasm.Compiler.Tests;

// A native archive's Wasm objects name the functions they define in their linking section's symbol table; a defined
// function that is not local is one another object links to, and its type is the one a call uses. Imported, local and
// undefined functions, data and section symbols, the archive's own tables and non-Wasm members define nothing.
public sealed class NativeArchiveFunctionTypeReaderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("netwasm-archive-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void ReadsTheTypesOfEachObjectsLinkableFunctions()
    {
        var first = WasmObject(
            types: [Type([0x7f, 0x7f], [0x7f]), Type([0x7e, 0x7d, 0x7c, 0x7b], [])],
            imports: [FunctionImport("env", "imported", 0), MemoryImport(), GlobalImport(), TableImport(), TagImport()],
            functions: [0, 1, 0],
            symbols:
            [
                FunctionSymbol(0x10, 0, null),            // the import, undefined
                FunctionSymbol(0x00, 1, "first"),
                FunctionSymbol(0x04, 2, "hidden"),        // hidden visibility still links within the program
                FunctionSymbol(0x02, 3, "local"),
                FunctionSymbol(0x50, 0, "named_import"),  // undefined with an explicit name
                DataSymbol(0x00, "table", defined: true),
                DataSymbol(0x10, "external", defined: false),
                SectionSymbol(1),
                FunctionSymbol(0x00, 9, "out_of_range"),
                GlobalSymbol("stack_pointer"),
            ]);
        var second = WasmObject(types: [Type([], [0x7f, 0x7f])], imports: [], functions: [0],
            symbols: [FunctionSymbol(0, 0, "pair")]);
        var archive = Archive(("/", Encoding.ASCII.GetBytes("symbols")), ("//", Encoding.ASCII.GetBytes("first_object.o/\n")),
            ("/0", first), ("#1/12", [.. Encoding.ASCII.GetBytes("second.o\0\0\0\0"), .. second]), ("notes/", [1, 2, 3]));

        var functions = new NativeArchiveFunctionTypeReader().Read(archive);

        Assert.Equal(["first", "hidden", "pair"], functions.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new NativeFunctionType([CliValueKind.I4, CliValueKind.I4], CliValueKind.I4), functions["first"]);
        // A v128 has no CLI kind of its own.
        Assert.Equal(new NativeFunctionType([CliValueKind.I8, CliValueKind.F4, CliValueKind.F8, CliValueKind.Unknown],
            CliValueKind.Void), functions["hidden"]);
        Assert.Equal(CliValueKind.Unknown, functions["pair"].Result);
    }

    // An LTO archive holds LLVM bitcode, whose types also say which functions are variadic (their parameters are then
    // the fixed ones): built here by the pinned Emscripten's clang for each target.
    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void ReadsBitcodeFunctionsAndWhetherTheyAreVariadic(string target)
    {
        var clang = Path.Combine(Emscripten(), "upstream", "bin", "clang");
        var archiver = Path.Combine(Emscripten(), "upstream", "bin", "llvm-ar");
        var source = Path.Combine(_directory, "bitcode.c");
        File.WriteAllText(source, """
            extern int declared(int value);
            static int hidden(int value) { return value + 1; }
            int variadic(int op, ...) { return hidden(op) + declared(op); }
            long long wide(void *pointer, double exact, float narrow, char small) { return (long long)exact + small; }
            void nothing(void) { }
            __attribute__((weak)) int weak(void) { return 1; }
            """);
        var bitcode = Path.Combine(_directory, target + ".o");
        Run(clang, "--target=" + target + "-unknown-emscripten", "-O0", "-flto", "-ffreestanding", "-c", source, "-o", bitcode);
        Assert.True(LlvmBitcodeFunctionTypeReader.IsBitcode(File.ReadAllBytes(bitcode)));
        var archive = Path.Combine(_directory, target + ".a");
        Run(archiver, "rcs", archive, bitcode);

        var functions = new NativeArchiveFunctionTypeReader().Read(archive);

        Assert.Equal(["nothing", "variadic", "weak", "wide"], functions.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new NativeFunctionType([CliValueKind.I4], CliValueKind.I4) { Variadic = true }, functions["variadic"]);
        CliValueKind[] wide = [CliValueKind.NativeInt, CliValueKind.F8, CliValueKind.F4, CliValueKind.I4];
        Assert.Equal(new NativeFunctionType([.. wide], CliValueKind.I8) { Variadic = false }, functions["wide"]);
        Assert.Equal(new NativeFunctionType([], CliValueKind.Void) { Variadic = false }, functions["nothing"]);

        // Cut short, the bitcode is malformed.
        var bytes = File.ReadAllBytes(bitcode);
        var truncated = Archive(("cut.o/", bytes[..(bytes.Length / 2 / 4 * 4)]));
        Assert.Equal(DiagnosticCode.NativeInterop,
            Assert.Throws<CompilerException>(() => new NativeArchiveFunctionTypeReader().Read(truncated)).Diagnostic.Code);
    }

    private static string Emscripten()
    {
        var root = Environment.GetEnvironmentVariable("NETWASM_EMSDK_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(root), "NETWASM_EMSDK_ROOT names the pinned Emscripten SDK.");
        return root!;
    }

    private static void Run(string tool, params string[] arguments)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tool, arguments)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        })!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
    }

    [Fact]
    public void AnEmptyArchiveDefinesNothing() =>
        Assert.Empty(new NativeArchiveFunctionTypeReader().Read(Archive()));

    public static TheoryData<string> Malformed => new()
    {
        "missing", "not an archive", "header", "size", "symbol kind", "import kind", "type form", "long number",
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedArchiveIsNamed(string problem)
    {
        var path = problem switch
        {
            "missing" => Path.Combine(_directory, "missing.a"),
            "not an archive" => Write("plain.a", Encoding.ASCII.GetBytes("not an archive")),
            "header" => Write("header.a", [.. Encoding.ASCII.GetBytes("!<arch>\n"), .. new byte[60]]),
            "size" => Write("size.a", [.. Encoding.ASCII.GetBytes("!<arch>\n"), .. Header("big/", 1000)]),
            "symbol kind" => Archive(("bad.o/", WasmObject([Type([], [])], [], [0], [[9, 0]]))),
            "import kind" => Archive(("bad.o/", WasmObject([Type([], [])], [[1, (byte)'m', 1, (byte)'f', 9]], [0], []))),
            "type form" => Archive(("bad.o/", WasmObject([[0x5f, 0, 0]], [], [], []))),
            _ => Archive(("bad.o/", WasmObject([[0x60, .. Enumerable.Repeat((byte)0x80, 10), 0, 0]], [], [], []))),
        };

        var exception = Assert.Throws<CompilerException>(() => new NativeArchiveFunctionTypeReader().Read(path));

        Assert.Equal(DiagnosticCode.NativeInterop, exception.Diagnostic.Code);
        Assert.Throws<ArgumentException>(() => new NativeArchiveFunctionTypeReader().Read(" "));
    }

    private string Archive(params (string Name, byte[] Data)[] members)
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("!<arch>\n"));
        foreach (var (name, data) in members)
        {
            bytes.AddRange(Header(name, data.Length));
            bytes.AddRange(data);
            if (data.Length % 2 == 1) bytes.Add((byte)'\n');
        }
        return Write($"archive-{Guid.NewGuid():N}.a", [.. bytes]);
    }

    private static byte[] Header(string name, int size) => Encoding.ASCII.GetBytes(
        name.PadRight(16) + "0".PadRight(12) + "0".PadRight(6) + "0".PadRight(6) + "644".PadRight(8) +
        size.ToString(System.Globalization.CultureInfo.InvariantCulture).PadRight(10) + "`\n");

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] WasmObject(byte[][] types, byte[][] imports, int[] functions, byte[][] symbols)
    {
        var module = new List<byte> { 0, (byte)'a', (byte)'s', (byte)'m', 1, 0, 0, 0 };
        Section(module, 1, [.. Leb(types.Length), .. types.SelectMany(type => type)]);
        if (imports.Length != 0) Section(module, 2, [.. Leb(imports.Length), .. imports.SelectMany(import => import)]);
        Section(module, 3, [.. Leb(functions.Length), .. functions.SelectMany(Leb)]);
        Section(module, 0, [.. Name("producers"), 0]);
        byte[] table = [.. Leb(symbols.Length), .. symbols.SelectMany(symbol => symbol)];
        Section(module, 0, [.. Name("linking"), 2, 5, 1, 0, 8, .. Leb(table.Length), .. table]);
        return [.. module];
    }

    private static void Section(List<byte> module, byte id, byte[] contents)
    {
        module.Add(id);
        module.AddRange(Leb(contents.Length));
        module.AddRange(contents);
    }

    private static byte[] Type(byte[] parameters, byte[] results) =>
        [0x60, .. Leb(parameters.Length), .. parameters, .. Leb(results.Length), .. results];

    private static byte[] FunctionImport(string module, string field, int type) => [.. Name(module), .. Name(field), 0, .. Leb(type)];

    private static byte[] MemoryImport() => [.. Name("env"), .. Name("memory"), 2, 1, 1, 2];

    private static byte[] GlobalImport() => [.. Name("env"), .. Name("__stack_pointer"), 3, 0x7f, 1];

    private static byte[] TableImport() => [.. Name("env"), .. Name("table"), 1, 0x70, 0, 1];

    private static byte[] TagImport() => [.. Name("env"), .. Name("tag"), 4, 0, 0];

    private static byte[] FunctionSymbol(int flags, int index, string? name) =>
        [0, .. Leb(flags), .. Leb(index), .. name is null ? [] : Name(name)];

    private static byte[] GlobalSymbol(string name) => [2, 0, 0, .. Name(name)];

    private static byte[] DataSymbol(int flags, string name, bool defined) =>
        [1, .. Leb(flags), .. Name(name), .. defined ? new byte[] { 0, 4, 8 } : []];

    private static byte[] SectionSymbol(int section) => [3, 0, .. Leb(section)];

    private static byte[] Name(string value) => [.. Leb(Encoding.UTF8.GetByteCount(value)), .. Encoding.UTF8.GetBytes(value)];

    private static byte[] Leb(int value)
    {
        var bytes = new List<byte>();
        var remaining = (uint)value;
        do
        {
            var current = (byte)(remaining & 0x7f);
            remaining >>= 7;
            bytes.Add(remaining == 0 ? current : (byte)(current | 0x80));
        }
        while (remaining != 0);
        return [.. bytes];
    }
}
