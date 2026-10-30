using System.Globalization;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A loop whose try holds a nested protected region (try/finally, try/catch or lock) followed by
// an exit (throw, return, break or goto) recorded the leave and then ran the code after it
// whenever the structurer placed code after the leave in the nested region's continuation:
// do loops, goto-built loops with two entries and async loops whose awaits resume inside the
// try. Each generated shape runs four inputs and checks its result and how often finally and
// catch ran against desktop .NET. The expected signatures were computed on desktop .NET 10 in
// Debug and Release, which agree.
public sealed class LoopExitAfterNestedProtectedRegionCompilationTests
{
    private const int BlockSize = 10;

    public static TheoryData<bool, WasmTarget> Cases() => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void LoopExitsAfterNestedProtectedRegionsMatchDesktop(bool optimize, WasmTarget target)
    {
        var shapes = Shapes();
        Assert.Equal(ExpectedSignatures.Length, shapes.Length);
        using var assets = TestAssets.Create();
        var name = $"LoopExitAfterNestedProtected{(optimize ? "Release" : "Debug")}{target}";
        var source = CreateSource(shapes);
        var assembly = optimize ? assets.CompileOptimizedSource(name, source) : assets.CompileSource(name, source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "LoopRegionAudit.EntryPoint", "Run",
            [
                new("run", "LoopRegionAudit.EntryPoint", "Run"),
                new("observe", "LoopRegionAudit.EntryPoint", "Observe"),
            ],
            Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/async-platform@1.0.0"));

        var mismatches = new List<string>();
        for (var block = 0; block * BlockSize < shapes.Length; block++)
        {
            var observed = CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
                assets.Directory, block, target, result.StaticDataEnd, drainReactor: true,
                observeExportName: "observe");
            if (observed == 42)
            {
                continue;
            }
            Assert.InRange(observed, 1001, 1000 + (1 << BlockSize) - 1);
            for (var offset = 0; offset < BlockSize; offset++)
            {
                if (((observed - 1000) >> offset & 1) != 0)
                {
                    mismatches.Add(shapes[block * BlockSize + offset].Name);
                }
            }
        }
        Assert.Empty(mismatches);
    }

    private static readonly string[] Loops = ["While", "For", "Do", "Goto", "AsyncWhile", "AsyncGoto"];

    private static readonly (string Name, string Template)[] Nests =
    [
        ("CatchFinally", "try { s += 1;AWAIT try { s += 3; } finally { s *= 2; F++; } EXIT } " +
            "catch (InvalidOperationException) { C++; s += 5; }"),
        ("CatchAllFinally", "try { s += 1;AWAIT try { s += 3; } finally { s *= 2; F++; } EXIT } " +
            "catch { C++; s += 5; }"),
        ("FinallyCatch", "try { s += 1;AWAIT try { s += 3; if (n == 3 && i == 0) throw new InvalidOperationException(); } " +
            "catch (InvalidOperationException) { C++; s += 5; } EXIT } finally { F++; s += 11; }"),
        ("CatchLock", "try { s += 1;AWAIT lock (Gate) { s += 3; } EXIT } " +
            "catch (InvalidOperationException) { C++; s += 5; }"),
        ("CatchTwoFinally", "try { s += 1;AWAIT try { try { s += 3; } finally { F++; s *= 2; } s += 2; } " +
            "finally { F += 10; } EXIT } catch (InvalidOperationException) { C++; s += 5; }"),
    ];

    private static readonly (string Name, string Statement)[] Exits =
    [
        ("None", ""),
        ("Throw", "if (n == 3 && i == 1) throw new InvalidOperationException();"),
        ("Return", "if (n == 2 && i == 1) return s * 3 + 1;"),
        ("Break", "if (n == 1 && i == 1) BREAK;"),
        ("Continue", "if (n == 0 && i == 1) CONTINUE;"),
        ("Goto", "if (n == 2 && i == 0) goto Out;"),
    ];

    // The audit's original repros: a goto-built loop with two entries whose try holds a nested
    // try/finally followed by a conditional throw.
    private static readonly (string Name, string Before, string Catch)[] OriginalRepros =
    [
        ("G4_NoReturn", "", "catch { C++; s += 1; }"),
        ("G4_Typed", "if (i == 9) return -2;", "catch (InvalidOperationException) { C++; s += 1; }"),
        ("G4_CatchAll", "if (i == 9) return -2;", "catch { C++; s += 1; }"),
    ];

    private static (string Name, bool IsAsync, string Method)[] Shapes()
    {
        var shapes = new List<(string, bool, string)>();
        foreach (var loop in Loops)
        {
            foreach (var (nest, template) in Nests)
            {
                foreach (var (exit, statement) in Exits)
                {
                    shapes.Add(CreateShape(loop, nest, template, exit, statement));
                }
            }
        }
        foreach (var (name, before, handler) in OriginalRepros)
        {
            shapes.Add((name, false, $$"""
                        static int {{name}}(int n)
                        {
                            int s = 0, i = 0;
                            if (n > 1) goto Middle;
                        Top:
                            if (i >= 2) goto Done;
                            s += 7;
                        Middle:
                            try { {{before}} try { s += 3; } finally { s *= 2; F++; } if (n == 3) throw new InvalidOperationException(); } {{handler}}
                            i++;
                            goto Top;
                        Done:
                            return s;
                        }

                """));
        }
        return [.. shapes];
    }

    private static (string Name, bool IsAsync, string Method) CreateShape(
        string loop,
        string nest,
        string template,
        string exit,
        string statement)
    {
        var isAsync = loop.StartsWith("Async", StringComparison.Ordinal);
        var twoEntries = loop.EndsWith("Goto", StringComparison.Ordinal);
        var exitStatement = twoEntries
            ? statement.Replace("BREAK", "goto Done", StringComparison.Ordinal)
                .Replace("CONTINUE", "goto Next", StringComparison.Ordinal)
            : statement.Replace("BREAK", "break", StringComparison.Ordinal)
                .Replace("CONTINUE", "continue", StringComparison.Ordinal);
        var body = template.Replace("AWAIT", isAsync ? " await Task.Yield();" : "", StringComparison.Ordinal)
            .Replace("EXIT", exitStatement, StringComparison.Ordinal);
        var name = $"{loop}_{nest}_{exit}";
        var outLabel = exit == "Goto" ? " return s; Out: s += 13;" : "";
        var core = twoEntries
            ? $$"""
                int s = 0, i = 0;
                if (n > 1) goto Middle;
            Top:
                if (i >= 3) goto Done;
                s += 7;
            Middle:
                {{body}}
            Next:
                i++;
                goto Top;
            Done:{{outLabel}}
                return s;
            """
            : loop is "While" or "AsyncWhile"
                ? $$"""
                    int s = 0, i = -1;
                    while (++i < 3)
                    {
                        s += 7;
                        {{body}}
                    }{{outLabel}}
                    return s;
                    """
                : loop == "For"
                    ? $$"""
                        int s = 0;
                        for (int i = 0; i < 3; i++)
                        {
                            s += 7;
                            {{body}}
                        }{{outLabel}}
                        return s;
                        """
                    : $$"""
                        int s = 0, i = -1;
                        do
                        {
                            i++;
                            s += 7;
                            {{body}}
                        }
                        while (i < 2);{{outLabel}}
                        return s;
                        """;
        return (name, isAsync, $$"""
            static {{(isAsync ? "async Task<int>" : "int")}} {{name}}(int n)
            {
            {{core}}
            }

            """);
    }

    private static string CreateSource((string Name, bool IsAsync, string Method)[] shapes)
    {
        var source = new StringBuilder();
        source.Append("""
            #pragma warning disable CS0164, CS0162
            using System;
            using System.Threading.Tasks;

            namespace LoopRegionAudit;

            public static class Shapes
            {
                public static readonly object Gate = new();
                public static int F;
                public static int C;

                public static async Task<int> Call(int index, int n)
                {
                    switch (index)
                    {

            """);
        for (var index = 0; index < shapes.Length; index++)
        {
            source.Append(CultureInfo.InvariantCulture,
                $"            case {index}: return {(shapes[index].IsAsync ? "await " : "")}{shapes[index].Name}(n);\n");
        }
        source.Append("""
                        default: throw new ArgumentOutOfRangeException(nameof(index));
                    }
                }

                public static async Task<long> Signature(int index)
                {
                    long sig = 17;
                    for (var n = 0; n < 4; n++)
                    {
                        F = 0;
                        C = 0;
                        int value;
                        try { value = await Call(index, n); }
                        catch (InvalidOperationException) { value = -7; }
                        sig = sig * 31 + value;
                        sig = sig * 31 + F * 7 + C;
                    }
                    return sig;
                }

            """);
        foreach (var shape in shapes)
        {
            source.Append(shape.Method);
        }
        source.Append(CultureInfo.InvariantCulture, $$"""
            }

            public static class EntryPoint
            {
                private static Task<int>? s_pending;
                private static readonly long[] Expected = [{{string.Join(", ", ExpectedSignatures.Select(value => value.ToString(CultureInfo.InvariantCulture) + "L"))}}];

                public static int Run(int input)
                {
                    s_pending = CheckBlock(input);
                    return input + 1;
                }

                public static int Observe() =>
                    s_pending is not { IsCompleted: true } pending ? 98
                    : pending.IsFaulted || pending.IsCanceled ? 99
                    : pending.Result;

                private static async Task<int> CheckBlock(int block)
                {
                    var mask = 0;
                    for (var k = 0; k < {{BlockSize}}; k++)
                    {
                        var index = block * {{BlockSize}} + k;
                        if (index >= Expected.Length) break;
                        if (await Shapes.Signature(index) != Expected[index]) mask |= 1 << k;
                    }
                    return mask == 0 ? 42 : 1000 + mask;
                }
            }

            """);
        return source.ToString();
    }

    private static readonly long[] ExpectedSignatures =
    [
        18759160682877L, 18759160683188L, 18759162016745L, 18756634852942L,
        18759160682877L, 18759157124294L, 18759160682877L, 18759160683188L,
        18759162016745L, 18756634852942L, 18759160682877L, 18759157124294L,
        16335528651641L, 16335528649216L, 16335529657808L, 16334892345672L,
        16335528651641L, 16335527714666L, 15408009648269L, 15408009648425L,
        15408010661163L, 15407694727608L, 15408009648269L, 15408009380150L,
        19331308420493L, 19331308420804L, 19331309806255L, 19328488910880L,
        19331308420493L, 19331304369878L, 18759160682877L, 18759160683188L,
        18759162016745L, 18756634852942L, 18759160682877L, 18759157124294L,
        18759160682877L, 18759160683188L, 18759162016745L, 18756634852942L,
        18759160682877L, 18759157124294L, 16335528651641L, 16335528649216L,
        16335529657808L, 16334892345672L, 16335528651641L, 16335527714666L,
        15408009648269L, 15408009648425L, 15408010661163L, 15407694727608L,
        15408009648269L, 15408009380150L, 19331308420493L, 19331308420804L,
        19331309806255L, 19328488910880L, 19331308420493L, 19331304369878L,
        18759160682877L, 18759160683188L, 18759162016745L, 18756634852942L,
        18759160682877L, 18759157124294L, 18759160682877L, 18759160683188L,
        18759162016745L, 18756634852942L, 18759160682877L, 18759157124294L,
        16335528651641L, 16335528649216L, 16335529657808L, 16334892345672L,
        16335528651641L, 16335527714666L, 15408009648269L, 15408009648425L,
        15408010661163L, 15407694727608L, 15408009648269L, 15408009380150L,
        19331308420493L, 19331308420804L, 19331309806255L, 19328488910880L,
        19331308420493L, 19331304369878L, 18759159012845L, 18759159013156L,
        18759159512565L, 18756633182910L, 18759159012845L, 18759156705484L,
        18759159012845L, 18759159013156L, 18759159512565L, 18756633182910L,
        18759159012845L, 18759156705484L, 16335528442887L, 16335528440679L,
        16335529031980L, 16334892136918L, 16335528442887L, 16335527505912L,
        15408009439515L, 15408009439671L, 15408010035335L, 15407694518854L,
        15408009439515L, 15408009171396L, 19331306750461L, 19331306750772L,
        19331307302075L, 19328487240848L, 19331306750461L, 19331303951068L,
        18759160682877L, 18759160683188L, 18759162016745L, 18756634852942L,
        18759160682877L, 18759157124294L, 18759160682877L, 18759160683188L,
        18759162016745L, 18756634852942L, 18759160682877L, 18759157124294L,
        16335528651641L, 16335528649216L, 16335529657808L, 16334892345672L,
        16335528651641L, 16335527714666L, 15408009648269L, 15408009648425L,
        15408010661163L, 15407694727608L, 15408009648269L, 15408009380150L,
        19331308420493L, 19331308420804L, 19331309806255L, 19328488910880L,
        19331308420493L, 19331304369878L, 18759159012845L, 18759159013156L,
        18759159512565L, 18756633182910L, 18759159012845L, 18759156705484L,
        18759159012845L, 18759159013156L, 18759159512565L, 18756633182910L,
        18759159012845L, 18759156705484L, 16335528442887L, 16335528440679L,
        16335529031980L, 16334892136918L, 16335528442887L, 16335527505912L,
        15408009439515L, 15408009439671L, 15408010035335L, 15407694518854L,
        15408009439515L, 15408009171396L, 19331306750461L, 19331306750772L,
        19331307302075L, 19328487240848L, 19331306750461L, 19331303951068L,
        16164061180912L, 16164061180912L, 16164061180912L,
    ];
}
