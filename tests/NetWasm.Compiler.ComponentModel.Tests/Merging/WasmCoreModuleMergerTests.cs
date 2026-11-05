using System.Text;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Merging;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Merging;

// The merger links NetWasm's runtime, application and support modules into one core module, as
// wasm-merge did: imports between the inputs become the entities they name, everything else is
// kept, and the application's code is copied with only its moved indices rewritten.
public sealed class WasmCoreModuleMergerTests
{
    private static readonly WasmCoreModuleMerger Merger = new();

    [Fact]
    public void ImportsBetweenInputsBecomeTheEntitiesTheyName()
    {
        var a = Module(
            Section(1, Vec(FuncType([], []), FuncType([0x7f], []))),
            Section(2, Vec(ImportFunction("b", "g", 0), ImportFunction("host", "h", 1))),
            Section(3, Vec(U(0))),
            Section(7, Vec(Export("fa", 0, 2))),
            Section(10, Vec(Body([], [0x10, 0], [0x41, 1, 0x10, 1], [0x10, 2]))));
        var b = Module(
            Section(1, Vec(FuncType([0x7f], []), FuncType([], []))),
            Section(2, Vec(ImportFunction("host", "h", 0), ImportFunction("a", "fa", 1))),
            Section(3, Vec(U(1))),
            Section(7, Vec(Export("g", 0, 2))),
            Section(10, Vec(Body([], [0x10, 1]))));

        var merged = Merger.Merge([new("a", a), new("b", b)]);

        // Identical types are one; both inputs' host import is one; a's call to b's g lands on g.
        Assert.Equal(Module(
            Section(1, Vec(FuncType([], []), FuncType([0x7f], []))),
            Section(2, Vec(ImportFunction("host", "h", 1))),
            Section(3, Vec(U(0), U(0))),
            Section(7, Vec(Export("fa", 0, 1), Export("g", 0, 2))),
            Section(10, Vec(Body([], [0x10, 2], [0x41, 1, 0x10, 0], [0x10, 1]), Body([], [0x10, 1])))), merged);
    }

    [Fact]
    public void EveryInputKeepsItsDefinitionsSegmentsAndStart()
    {
        var p = Module(
            Section(1, Vec(FuncType([], []))),
            Section(3, Vec(U(0))),
            Section(4, Vec([0x70, 0x00, 1])),
            Section(5, Vec([0x00, 1])),
            Section(13, Vec([0, 0])),
            Section(6, Vec([0x7f, 0x00, 0x41, 7, 0x0b])),
            Section(7, Vec(Export("pm", 2, 0), Export("pt", 1, 0), Export("pg", 3, 0), Export("ptag", 4, 0),
                Export("pf", 0, 0))),
            Section(8, U(0)),
            Section(9, Vec([1, 0x00, .. Vec(U(0))])),
            Section(12, U(1)),
            Section(10, Vec(Body([], [0x41, 0, 0x28, 0x02, 0x00, 0x1a]))),
            Section(11, Vec([1, .. Bytes(new byte[300])])),
            Custom("binaryen.inline", [.. U(1), .. U(0), .. U(2), .. U(0), .. U(1), 0x7f, .. U(0), .. U(1), 0x00]),
            Custom("name", [1, 2]),
            Custom("keep", [1, 2, 3]));
        var q = Module(
            Section(1, Vec(FuncType([], []))),
            Section(2, Vec([.. Name("p"), .. Name("pg"), 3, 0x7f, 0x00])),
            Section(3, Vec(U(0))),
            Section(4, Vec([0x70, 0x00, 1])),
            Section(5, Vec([0x01, 1, 2])),
            Section(13, Vec([0, 0])),
            Section(6, Vec([0x7f, 0x00, 0x23, 0, 0x0b])),
            Section(7, Vec(Export("qf", 0, 0))),
            Section(8, U(0)),
            Section(9, Vec(
                [0, 0x41, 0, 0x0b, .. Vec(U(0))],
                [4, 0x41, 0, 0x0b, .. Vec([0xd2, 0, 0x0b])],
                [2, 0, 0x41, 0, 0x0b, 0x00, .. Vec(U(0))],
                [6, 0, 0x41, 0, 0x0b, 0x70, .. Vec([0xd2, 0, 0x0b])],
                [3, 0x00, .. Vec(U(0))],
                [5, 0x70, .. Vec([0xd2, 0, 0x0b])],
                [7, 0x70, .. Vec([0xd2, 0, 0x0b])])),
            Section(12, U(3)),
            Section(10, Vec(Body([],
                [0x41, 0, 0x28, 0x02, 0x00, 0x1a],
                [0x41, 0, 0x28, 0x42, 0x00, 0x00, 0x1a],
                [0x23, 0, 0x1a, 0x25, 0, 0x1a, 0x08, 0, 0x3f, 0, 0x1a],
                [0xfc, 9, 0, 0xfc, 13, 0, 0x10, 0]))),
            Section(11, Vec(
                [0, 0x41, 16, 0x0b, .. Bytes("a"u8)],
                [2, 0, 0x41, 0, 0x0b, .. Bytes("b"u8)],
                [1, .. Bytes("c"u8)])));

        var merged = Merger.Merge([new("p", p), new("q", q)]);

        // q's own table, memory, tag, data and elements land after p's: its implicit memory and
        // table 0 accesses and segments name them; one more function runs both starts.
        Assert.Equal(Module(
            Section(1, Vec(FuncType([], []))),
            Section(3, Vec(U(0), U(0), U(0))),
            Section(4, Vec([0x70, 0x00, 1], [0x70, 0x00, 1])),
            Section(5, Vec([0x00, 1], [0x01, 1, 2])),
            Section(13, Vec([0, 0], [0, 0])),
            Section(6, Vec([0x7f, 0x00, 0x41, 7, 0x0b], [0x7f, 0x00, 0x23, 0, 0x0b])),
            Section(7, Vec(Export("pm", 2, 0), Export("pt", 1, 0), Export("pg", 3, 0), Export("ptag", 4, 0),
                Export("pf", 0, 0), Export("qf", 0, 1))),
            Section(8, U(2)),
            Section(9, Vec(
                [1, 0x00, .. Vec(U(0))],
                [2, 1, 0x41, 0, 0x0b, 0x00, .. Vec(U(1))],
                [6, 1, 0x41, 0, 0x0b, 0x70, .. Vec([0xd2, 1, 0x0b])],
                [2, 1, 0x41, 0, 0x0b, 0x00, .. Vec(U(1))],
                [6, 1, 0x41, 0, 0x0b, 0x70, .. Vec([0xd2, 1, 0x0b])],
                [3, 0x00, .. Vec(U(1))],
                [5, 0x70, .. Vec([0xd2, 1, 0x0b])],
                [7, 0x70, .. Vec([0xd2, 1, 0x0b])])),
            Section(12, U(4)),
            Custom("binaryen.inline", [.. U(1), .. U(0), .. U(2), .. U(0), .. U(1), 0x7f, .. U(0), .. U(1), 0x00]),
            Section(10, Vec(
                Body([], [0x41, 0, 0x28, 0x02, 0x00, 0x1a]),
                Body([],
                    [0x41, 0, 0x28, 0x42, 0x01, 0x00, 0x1a],
                    [0x41, 0, 0x28, 0x42, 0x01, 0x00, 0x1a],
                    [0x23, 0, 0x1a, 0x25, 1, 0x1a, 0x08, 1, 0x3f, 1, 0x1a],
                    [0xfc, 9, 1, 0xfc, 13, 1, 0x10, 1]),
                Body([], [0x10, 0, 0x10, 1]))),
            Section(11, Vec(
                [1, .. Bytes(new byte[300])],
                [2, 1, 0x41, 16, 0x0b, .. Bytes("a"u8)],
                [2, 1, 0x41, 0, 0x0b, .. Bytes("b"u8)],
                [1, .. Bytes("c"u8)])),
            Custom("keep", [1, 2, 3])), merged);
    }

    // Function 0 is always inlined (127), function 1 never (0): size-aware selection drops the
    // first, and removal both.
    [Theory]
    [InlineData(WasmInlineAnnotationSelection.Preserve, true, true)]
    [InlineData(WasmInlineAnnotationSelection.SizeAware, false, true)]
    [InlineData(WasmInlineAnnotationSelection.Remove, false, false)]
    public void EachInputKeepsTheInlineHintsItsSelectionKeeps(
        WasmInlineAnnotationSelection selection, bool always, bool never)
    {
        byte[] alwaysHint = [.. U(0), .. U(1), .. U(0), .. U(1), 0x7f];
        byte[] neverHint = [.. U(1), .. U(1), .. U(0), .. U(1), 0x00];
        byte[][] Sections(params byte[][] hints) =>
        [
            Section(1, Vec(FuncType([], []))),
            Section(3, Vec(U(0), U(0))),
            .. hints.Length == 0 ? Array.Empty<byte[]>() : [Custom("binaryen.inline", [.. U((uint)hints.Length), .. hints.SelectMany(hint => hint)])],
            Section(10, Vec(Body([]), Body([]))),
        ];
        var input = Module(Sections(alwaysHint, neverHint));

        var merged = Merger.Merge([new("a", input) { InlineAnnotations = selection }]);

        var kept = new List<byte[]>();
        if (always) kept.Add(alwaysHint);
        if (never) kept.Add(neverHint);
        Assert.Equal(Module(Sections([.. kept])), merged);
    }

    [Fact]
    public void SizeAwareSelectionKeepsAHintThatIsNotAlwaysInline()
    {
        var module = Module(
            Section(1, Vec(FuncType([], []))),
            Section(3, Vec(U(0))),
            Custom("binaryen.inline", [.. U(1), .. U(0), .. U(1), .. U(0), .. U(2), 0x7f, 0x00]),
            Section(10, Vec(Body([]))));

        Assert.Equal(module, Merger.Merge([new("a", module) { InlineAnnotations = WasmInlineAnnotationSelection.SizeAware }]));
    }

    // A hint inside a body cannot move with its instruction: only a selection that drops it merges.
    [Theory]
    [InlineData(WasmInlineAnnotationSelection.Preserve, true)]
    [InlineData(WasmInlineAnnotationSelection.SizeAware, true)]
    [InlineData(WasmInlineAnnotationSelection.Remove, false)]
    public void AHintInsideABodyMergesOnlyWhenItsSelectionDropsIt(WasmInlineAnnotationSelection selection, bool rejects)
    {
        var module = Module(Custom("binaryen.inline", [1, 0, 1, 5, 1, 0x7f]));

        var merge = () => Merger.Merge([new("bad", module) { InlineAnnotations = selection }]);

        if (rejects) Assert.Contains("inside a function body", Assert.Throws<CompilerException>(merge).Diagnostic.Message);
        else Assert.Equal(Module(), merge());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Merger.Merge([new("bad", module) { InlineAnnotations = (WasmInlineAnnotationSelection)9 }]));
    }

    // A component keeps only its canonical ABI's exports, among them its one memory; a raw module
    // drops the internal exports that only linked its inputs.
    [Fact]
    public void TheMergedModuleKeepsTheExportsItsSelectionKeeps()
    {
        var a = Module(
            Section(1, Vec(FuncType([], []))),
            Section(3, Vec(U(0))),
            Section(5, Vec([0x00, 1])),
            Section(7, Vec(Export("cm32_memory", 2, 0), Export("cm32_run", 0, 0), Export("cm32|init", 0, 0),
                Export("internal", 0, 0))),
            Section(10, Vec(Body([]))));

        var component = Merger.Merge([new("a", a)], new("cm32", []));
        var raw = Merger.Merge([new("a", a)], new(null, [new("internal", 0)]));

        byte[] Expected(params byte[][] exports) => Module(
            Section(1, Vec(FuncType([], []))),
            Section(3, Vec(U(0))),
            Section(5, Vec([0x00, 1])),
            Section(7, Vec(exports)),
            Section(10, Vec(Body([]))));
        Assert.Equal(Expected(Export("cm32_memory", 2, 0), Export("cm32_run", 0, 0), Export("cm32|init", 0, 0)), component);
        Assert.Equal(Expected(Export("cm32_memory", 2, 0), Export("cm32_run", 0, 0), Export("cm32|init", 0, 0)), raw);
        Assert.Contains("exactly one memory named 'other_memory'", Assert.Throws<CompilerException>(() =>
            Merger.Merge([new("a", a)], new("other", []))).Diagnostic.Message);
        Assert.Contains("missing a required internal export", Assert.Throws<CompilerException>(() =>
            Merger.Merge([new("a", a)], new(null, [new("absent", 0)]))).Diagnostic.Message);
        Assert.Contains("conflicting kind", Assert.Throws<CompilerException>(() =>
            Merger.Merge([new("a", a)], new(null, [new("internal", 3)]))).Diagnostic.Message);
    }

    // Every kind of immediate, in an input whose indices all move: type indices past 63 need a
    // second byte as the signed block and heap types encode them.
    [Fact]
    public void EveryImmediateThatNamesAnIndexMovesWithIt()
    {
        var types = Enumerable.Range(1, 64).Select(count => FuncType([.. Enumerable.Repeat((byte)0x7e, count)], [])).ToArray();
        var y = Module(
            Section(1, Vec(types)),
            Section(3, Vec(U(0))),
            Section(4, Vec([0x70, 0x00, 1])),
            Section(5, Vec([0x00, 1])),
            Section(13, Vec([0, 0])),
            Section(6, Vec([0x7f, 0x00, 0x41, 0, 0x0b])),
            Section(8, U(0)),
            Section(9, Vec([0, 0x41, 0, 0x0b, .. Vec(U(0))])),
            Section(12, U(1)),
            Section(10, Vec(Body([]))),
            Section(11, Vec([0, 0x41, 0, 0x0b, .. Bytes("y"u8)])));
        var z = Module(
            Section(1, Vec(FuncType([], []), FuncType([0x7d], []))),
            Section(3, Vec(U(0))),
            Section(4, Vec([0x70, 0x00, 1])),
            Section(5, Vec([0x00, 1])),
            Section(13, Vec([0, 0])),
            Section(6, Vec([0x7f, 0x00, 0x41, 0, 0x28, 0x02, 0x00, 0x0b])),
            Section(9, Vec([1, 0x00, .. Vec(U(0))])),
            Section(12, U(1)),
            Section(10, Vec(Body(Vec([1, 0x7f]), Instructions(0, 0)))),
            Section(11, Vec([1, .. Bytes("z"u8)])));

        var merged = Merger.Merge([new("y", y), new("z", z)]);

        Assert.Equal(Module(
            Section(1, Vec([.. types, FuncType([], []), FuncType([0x7d], [])])),
            Section(3, Vec(U(0), U(64))),
            Section(4, Vec([0x70, 0x00, 1], [0x70, 0x00, 1])),
            Section(5, Vec([0x00, 1], [0x00, 1])),
            Section(13, Vec([0, 0], [0, .. U(64)])),
            Section(6, Vec([0x7f, 0x00, 0x41, 0, 0x0b], [0x7f, 0x00, 0x41, 0, 0x28, 0x42, 0x01, 0x00, 0x0b])),
            Section(8, U(0)),
            Section(9, Vec([0, 0x41, 0, 0x0b, .. Vec(U(0))], [1, 0x00, .. Vec(U(1))])),
            Section(12, U(2)),
            Section(10, Vec(Body([]), Body(Vec([1, 0x7f]), Instructions(1, 64)))),
            Section(11, Vec([0, 0x41, 0, 0x0b, .. Bytes("y"u8)], [1, .. Bytes("z"u8)]))), merged);
    }

    // An import of a re-exported import lands on the definition it names; imports no input
    // provides stay, one for each module, name and signature.
    [Fact]
    public void ReExportedImportsLandOnWhatTheyName()
    {
        var a = Module(
            Section(1, Vec(FuncType([], []))),
            Section(2, Vec([.. Name("host"), .. Name("g"), 3, 0x7f, 0x00])),
            Section(3, Vec(U(0))),
            Section(4, Vec([0x70, 0x00, 1])),
            Section(5, Vec([0x00, 1])),
            Section(13, Vec([0, 0])),
            Section(7, Vec(Export("f", 0, 0), Export("t", 1, 0), Export("m", 2, 0), Export("x", 4, 0))),
            Section(10, Vec(Body([]))));
        var b = Module(
            Section(1, Vec(FuncType([], []))),
            Section(2, Vec(
                ImportFunction("a", "f", 0),
                [.. Name("a"), .. Name("t"), 1, 0x70, 0x00, 1],
                [.. Name("a"), .. Name("m"), 2, 0x00, 1],
                [.. Name("a"), .. Name("x"), 4, 0, 0])),
            Section(7, Vec(Export("bf", 0, 0), Export("bt", 1, 0), Export("bm", 2, 0), Export("bx", 4, 0))));
        var c = Module(
            Section(1, Vec(FuncType([], []))),
            Section(2, Vec(
                ImportFunction("b", "bf", 0),
                [.. Name("b"), .. Name("bt"), 1, 0x70, 0x00, 1],
                [.. Name("b"), .. Name("bm"), 2, 0x00, 1],
                [.. Name("b"), .. Name("bx"), 4, 0, 0],
                [.. Name("host"), .. Name("g"), 3, 0x7f, 0x00],
                [.. Name("host"), .. Name("e"), 4, 0, 0])),
            Section(3, Vec(U(0))),
            Section(10, Vec(Body([], [0x10, 0, 0x25, 0, 0x1a, 0x3f, 0, 0x1a, 0x08, 0, 0x23, 0, 0x1a]))));

        var merged = Merger.Merge([new("a", a), new("b", b), new("c", c)]);

        Assert.Equal(Module(
            Section(1, Vec(FuncType([], []))),
            Section(2, Vec([.. Name("host"), .. Name("g"), 3, 0x7f, 0x00], [.. Name("host"), .. Name("e"), 4, 0, 0])),
            Section(3, Vec(U(0), U(0))),
            Section(4, Vec([0x70, 0x00, 1])),
            Section(5, Vec([0x00, 1])),
            Section(13, Vec([0, 0])),
            Section(7, Vec(Export("f", 0, 0), Export("t", 1, 0), Export("m", 2, 0), Export("x", 4, 1),
                Export("bf", 0, 0), Export("bt", 1, 0), Export("bm", 2, 0), Export("bx", 4, 1))),
            Section(10, Vec(Body([]), Body([], [0x10, 0, 0x25, 0, 0x1a, 0x3f, 0, 0x1a, 0x08, 1, 0x23, 0, 0x1a])))), merged);
    }

    public static TheoryData<string, byte[]> UnreadableModules => new()
    {
        { "is not a WebAssembly core module", [1, 2, 3] },
        { "is truncated", Module([1, 5]) },
        { "is truncated", Module([1]) },
        { "with trailing data", Module(Section(1, [0, 0xff])) },
        { "declares 1 functions but holds 0 bodies", Module(Section(1, Vec(FuncType([], []))), Section(3, Vec(U(0)))) },
        { "section 14", Module(Section(14, [])) },
        { "not a function type", Module(Section(1, Vec([0x5f]))) },
        { "typed references", Module(Section(1, Vec(FuncType([0x63, 0], [])))) },
        { "external of kind 5", Module(Section(2, Vec([.. Name("m"), .. Name("n"), 5]))) },
        { "external of kind 9", Module(Section(7, Vec([.. Name("e"), 9, 0]))) },
        { "a table with an initializer", Module(Section(4, Vec([0x40, 0x00]))) },
        { "element segment of form 8", Module(Section(9, Vec([8]))) },
        { "data segment of form 3", Module(Section(11, Vec([3]))) },
        { "not an exception", Module(Section(13, Vec([1, 0]))) },
        { "inside a function body", Module(Custom("binaryen.inline", [1, 0, 1, 5, 1, 0x7f])) },
        { "two inline hint sections", Module(Custom("binaryen.inline", [0]), Custom("binaryen.inline", [0])) },
        { "hints function 0 twice", Module(Custom("binaryen.inline", [2, 0, 0, 0, 0])) },
        { "not valid UTF-8", Module(Section(0, [1, 0xff])) },
        { "out of range", Module([1, 0xff, 0xff, 0xff, 0xff, 0x7f]) },
        { "longer than 64 bits", Module(Section(5, Vec([0x00, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x00]))) },
        { "runs past its end", Module(Section(6, Vec([0x7f, 0x00, 0x41, 0]))) },
    };

    [Theory]
    [MemberData(nameof(UnreadableModules))]
    public void RejectsWhatItCannotRead(string message, byte[] module)
    {
        var exception = Assert.Throws<CompilerException>(() => Merger.Merge([new("bad", module)]));
        Assert.Contains(message, exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, byte[]> UncopyableBodies => new()
    {
        { "instruction 0xfb", [0xfb, 0] },
        { "catch of kind 4", [0x1f, 0x40, 1, 4, 0] },
        { "instruction 0xfc 18", [0xfc, 18] },
        { "instruction 0xfd 276", [0xfd, .. U(276)] },
        { "instruction 0xfe 4", [0xfe, 4] },
        { "instruction 0xfe 80", [0xfe, 80] },
        { "refers to index 5", [0x10, 5] },
        { "typed references", [0x02, 0x63, 0] },
        { "typed references", [0x1c, 1, 0x64, 0] },
    };

    [Theory]
    [MemberData(nameof(UncopyableBodies))]
    public void RejectsInstructionsItCannotCopy(string message, byte[] instructions)
    {
        var module = Module(Section(1, Vec(FuncType([], []))), Section(3, Vec(U(0))), Section(10, Vec(Body([], instructions))));
        var exception = Assert.Throws<CompilerException>(() => Merger.Merge([new("bad", module)]));
        Assert.Contains(message, exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsABodyThatRunsPastItsEnd()
    {
        var module = Module(
            Section(1, Vec(FuncType([], []))),
            Section(3, Vec(U(0))),
            Section(10, Vec([2, 0, 0x41])),
            Custom("after", [0]));
        var exception = Assert.Throws<CompilerException>(() => Merger.Merge([new("bad", module)]));
        Assert.Contains("runs past its end", exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, byte[], byte[]> UnlinkableModules => new()
    {
        {
            "imports function 'x' from 'b', which does not export it",
            Module(Section(1, Vec(FuncType([], []))), Section(2, Vec(ImportFunction("b", "x", 0)))),
            Module()
        },
        {
            "exports function 'x' as an index it does not define",
            Module(Section(1, Vec(FuncType([], []))), Section(2, Vec(ImportFunction("b", "x", 0)))),
            Module(Section(7, Vec(Export("x", 0, 9))))
        },
        {
            "imports function 'x' from 'b' with another signature",
            Module(Section(1, Vec(FuncType([], []))), Section(2, Vec(ImportFunction("b", "x", 0)))),
            Module(Section(1, Vec(FuncType([0x7f], []))), Section(3, Vec(U(0))), Section(7, Vec(Export("x", 0, 0))),
                Section(10, Vec(Body([]))))
        },
        {
            "imports tag 'x' from 'b' with another signature",
            Module(Section(1, Vec(FuncType([], []))), Section(2, Vec([.. Name("b"), .. Name("x"), 4, 0, 0]))),
            Module(Section(1, Vec(FuncType([0x7f], []))), Section(13, Vec([0, 0])), Section(7, Vec(Export("x", 4, 0))))
        },
        {
            "in a cycle of imports",
            Module(Section(1, Vec(FuncType([], []))), Section(2, Vec(ImportFunction("b", "x", 0))),
                Section(7, Vec(Export("y", 0, 0)))),
            Module(Section(1, Vec(FuncType([], []))), Section(2, Vec(ImportFunction("a", "y", 0))),
                Section(7, Vec(Export("x", 0, 0))))
        },
        {
            "both export 'e'",
            Module(Section(1, Vec(FuncType([], []))), Section(3, Vec(U(0))), Section(7, Vec(Export("e", 0, 0))),
                Section(10, Vec(Body([])))),
            Module(Section(1, Vec(FuncType([], []))), Section(3, Vec(U(0))), Section(7, Vec(Export("e", 0, 0))),
                Section(10, Vec(Body([]))))
        },
        { "both named", Module(), Module() },
    };

    [Theory]
    [MemberData(nameof(UnlinkableModules))]
    public void RejectsInputsThatDoNotLink(string message, byte[] a, byte[] b)
    {
        var exception = Assert.Throws<CompilerException>(() =>
            Merger.Merge([new("a", a), new(message == "both named" ? "a" : "b", b)]));
        Assert.Contains(message, exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresInputs()
    {
        Assert.Throws<ArgumentNullException>(() => Merger.Merge(null!));
        Assert.Throws<ArgumentException>(() => Merger.Merge([]));
        Assert.Throws<ArgumentException>(() => Merger.Merge([null!]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheRunnerMergesTheRuntimeApplicationAndSupportModules(bool managedExecutable)
    {
        var files = new Files();
        var merger = new RecordingMerger();
        var runner = new WasmCoreModuleMergeRunner(files, files, merger);

        WasmExportSelection exports = new("cm32", []);
        runner.Run(new("application.wasm", "runtime.wasm", "environment.wasm", "merged.wasm", ComponentTarget.Wasm32Wasi02,
            managedExecutable ? "host.wasm" : null, managedExecutable ? "adapter.wasm" : null)
        {
            ApplicationInlineAnnotations = WasmInlineAnnotationSelection.SizeAware,
            RuntimeInlineAnnotations = WasmInlineAnnotationSelection.Remove,
            Exports = exports,
            Intermediates = new("unused-application.wasm", "unused-runtime.wasm", "unused-merged.wasm"),
        });

        // One pass: the inputs as they are, each with its hint selection; no intermediate module.
        string[] expected = managedExecutable
            ? ["netwasm.runtime.v1=runtime.wasm:Remove", "netwasm.application.v1=application.wasm:SizeAware",
                "env=environment.wasm:Preserve", "netwasm.host.v1=host.wasm:Preserve", "netwasm.command.v1=adapter.wasm:Preserve"]
            : ["netwasm.runtime.v1=runtime.wasm:Remove", "netwasm.application.v1=application.wasm:SizeAware",
                "env=environment.wasm:Preserve"];
        Assert.Equal(expected, merger.Inputs);
        Assert.Same(exports, merger.Exports);
        Assert.Equal(("merged.wasm", "merged"), files.Written);
        Assert.Throws<ArgumentNullException>(() => runner.Run(null!));
        Assert.Throws<ArgumentNullException>(() => new WasmCoreModuleMergeRunner(null!, files, merger));
        Assert.Throws<ArgumentNullException>(() => new WasmCoreModuleMergeRunner(files, null!, merger));
        Assert.Throws<ArgumentNullException>(() => new WasmCoreModuleMergeRunner(files, files, null!));
    }

    // Every kind of immediate; `move` is how far function, table, memory, global, tag, data and
    // element indices moved, and `types` how far type indices did.
    private static byte[][] Instructions(byte move, uint types)
    {
        var memory = move == 0 ? new byte[] { 0x02 } : [0x42, move];
        return
        [
            [0x01, 0x02, 0x40, 0x0b, 0x02, 0x7f, 0x0b, 0x02, .. S(1 + types), 0x0b],
            [0x0c, 0, 0x0e, 2, 0, 1, 0],
            [0x10, move, 0x12, move, 0xd2, move, 0x1a],
            [0x11, .. U(1 + types), move, 0x14, .. U(1 + types)],
            [0x20, 0, 0x23, move, 0x25, move],
            [0x28, .. memory, 0x00, 0x28, 0x42, move, 0x00, 0x3f, move],
            [0x41, 0x7f, 0x42, 0x00, 0x43, 0, 0, 0, 0, 0x44, 0, 0, 0, 0, 0, 0, 0, 0],
            [0xd0, 0x70, 0xd0, .. S(1 + types), 0x1c, 1, 0x7f],
            [0x08, move, 0x1f, 0x40, 4, 0, move, 0, 1, move, 0, 2, 0, 3, 0],
            [0xfc, 0, 0xfc, 8, move, move, 0xfc, 9, move, 0xfc, 10, move, move, 0xfc, 11, move],
            [0xfc, 12, move, move, 0xfc, 13, move, 0xfc, 14, move, move, 0xfc, 15, move],
            [0xfd, 0, .. memory, 0x00, 0xfd, 12, .. new byte[16], 0xfd, 21, 0, 0xfd, 84, .. memory, 0x00, 0, 0xfd, 14],
            [0xfe, 0x10, .. memory, 0x00, 0xfe, 3, 0x00],
        ];
    }

    private static byte[] Module(params byte[][] sections) =>
        [0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00, .. sections.SelectMany(section => section)];

    private static byte[] Section(byte id, byte[] payload) => [id, .. U((uint)payload.Length), .. payload];

    private static byte[] Custom(string name, byte[] payload) => Section(0, [.. Name(name), .. payload]);

    private static byte[] Vec(params byte[][] items) => [.. U((uint)items.Length), .. items.SelectMany(item => item)];

    private static byte[] Bytes(ReadOnlySpan<byte> bytes) => [.. U((uint)bytes.Length), .. bytes];

    private static byte[] Name(string name) => Bytes(Encoding.UTF8.GetBytes(name));

    private static byte[] FuncType(byte[] parameters, byte[] results) =>
        [0x60, .. U((uint)parameters.Length), .. parameters, .. U((uint)results.Length), .. results];

    private static byte[] ImportFunction(string module, string name, uint type) => [.. Name(module), .. Name(name), 0, .. U(type)];

    private static byte[] Export(string name, byte kind, uint index) => [.. Name(name), kind, .. U(index)];

    // A function body: its locals vector (none when empty), its instructions and its end.
    private static byte[] Body(byte[] locals, params byte[][] instructions)
    {
        byte[] content = [.. locals.Length == 0 ? new byte[] { 0 } : locals,
            .. instructions.SelectMany(instruction => instruction), 0x0b];
        return [.. U((uint)content.Length), .. content];
    }

    private static byte[] U(uint value)
    {
        var bytes = new List<byte>();
        do
        {
            var next = (byte)(value & 0x7f);
            value >>= 7;
            bytes.Add(value == 0 ? next : (byte)(next | 0x80));
        }
        while (value != 0);
        return [.. bytes];
    }

    private static byte[] S(uint value)
    {
        var bytes = new List<byte>();
        while (value >= 0x40)
        {
            bytes.Add((byte)(value & 0x7f | 0x80));
            value >>= 7;
        }
        bytes.Add((byte)value);
        return [.. bytes];
    }

    private sealed class Files : IByteFileReader, IByteFileWriter
    {
        public (string Path, string Content) Written { get; private set; }

        public byte[] Read(string path) => Encoding.UTF8.GetBytes(path);

        public void Write(string path, byte[] bytes) => Written = (path, Encoding.UTF8.GetString(bytes));
    }

    private sealed class RecordingMerger : IWasmCoreModuleMerger
    {
        public string[] Inputs { get; private set; } = [];
        public WasmExportSelection? Exports { get; private set; }

        public byte[] Merge(IReadOnlyList<WasmMergeInput> inputs, WasmExportSelection? exports = null)
        {
            Inputs = [.. inputs.Select(input => $"{input.Name}={Encoding.UTF8.GetString(input.Module)}:{input.InlineAnnotations}")];
            Exports = exports;
            return "merged"u8.ToArray();
        }
    }
}
