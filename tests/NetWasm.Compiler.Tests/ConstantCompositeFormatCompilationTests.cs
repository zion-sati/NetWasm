// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Globalization;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// string.Format of a constant format, called directly or through a method that passes its parameters on to it as
// each library's SR.Format does, writes what desktop .NET writes and carries only its arguments' own formatting:
// not string.Format's parser of item formats, nor every boxed number's formatting through ISpanFormattable.
public sealed class ConstantCompositeFormatCompilationTests
{
    private const string Types = """
        public static class Messages
        {
            public static string Format(string format, object? arg0) => string.Format(format, arg0);
            public static string Format(string format, object? arg0, object? arg1) => string.Format(format, arg0, arg1);
            public static string Format(string format, object? arg0, object? arg1, object? arg2) =>
                string.Format(format, arg0, arg1, arg2);
            public static string Format(IFormatProvider? provider, string format, object? arg0, object? arg1) =>
                string.Format(provider, format, arg0, arg1);
            public static string FormatMany(string format, params object?[]? args) =>
                args is null ? format : string.Format(format, args);
        }

        public sealed class IntegerFormatter : IFormatProvider, ICustomFormatter
        {
            public object? GetFormat(Type? formatType) => formatType == typeof(ICustomFormatter) ? this : null;
            public string Format(string? format, object? arg, IFormatProvider? formatProvider) => arg is int i ? "#" + i + format : null!;
        }

        public sealed class Formattable(int value) : IFormattable
        {
            public string ToString(string? format, IFormatProvider? formatProvider) => "F" + value + ":" + format;
            public override string ToString() => "plain";
        }

        public class Base
        {
            public override string ToString() => "base";
        }

        public sealed class Derived : Base, IFormattable
        {
            public string ToString(string? format, IFormatProvider? formatProvider) => "derived:" + format;
        }

        public sealed class Plain
        {
            public override string ToString() => "pl";
        }

        public class Chain
        {
            public override string ToString() => "chain";
        }

        public class Middle : Chain
        {
        }

        public sealed class Leaf : Middle, IFormattable
        {
            public string ToString(string? format, IFormatProvider? formatProvider) => "leaf:" + format;
        }

        public class Root
        {
            public override string ToString() => "root";
        }

        public sealed class Generic<T> : Root, IFormattable
        {
            public string ToString(string? format, IFormatProvider? formatProvider) => "generic:" + (default(T) is int ? "int:" : "other:") + format;
        }

        public static class Outer
        {
            public class NestedBase
            {
                public override string ToString() => "nested";
            }

            public sealed class Nested : NestedBase, IFormattable
            {
                public string ToString(string? format, IFormatProvider? formatProvider) => "nested:" + format;
            }
        }

        public readonly struct Point(int x)
        {
            public override string ToString() => "P" + x;
        }

        [Flags] public enum Permission : byte { None = 0, Read = 1, Write = 2, Execute = 4, ReadWrite = Read | Write }
        public enum Signed : long { Min = long.MinValue, MinusOne = -1, Zero = 0, Max = long.MaxValue }
        public enum Small : sbyte { Low = -5, High = 7 }
        [Flags] public enum Wide : ulong { LowBit = 1, HighBit = 0x8000000000000000 }
        public enum Ordinary { First, Second, Tenth = 10 }
        public enum Short : ushort { One = 1, Top = ushort.MaxValue }
        """;

    // The format, the arguments as the fixture writes them for an input of 42, and the same arguments on desktop.
    public static TheoryData<bool, WasmTarget> Targets => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Targets))]
    public void FormatsAsDesktopDoes(bool optimize, WasmTarget target)
    {
        (string Format, string Arguments, object?[] Desktop)[] cases =
        [
            ("n={0} [{0,5}] [{0,-4}] {{x}} }}", "input", [42]),
            ("{0:X4} {0:x} {0:X} {1:X2} {2:x8} {0,6:X}|{0,-6:x2}|", "input, (byte)200, -input", [42, (byte)200, -42]),
            ("{0:X}|{1:X}|{2:X}", "(sbyte)-input, (short)-input, -(long)input", [(sbyte)-42, (short)-42, -42L]),
            ("{0:x}|{1:X1}|{2:X0}|{2}", "(ushort)input, (uint)input, (ulong)0", [(ushort)42, 42u, 0UL]),
            ("{0} {1} {2}", "(short)-3, (long)input * 1000000000L, 'c'", [(short)-3, 42_000_000_000L, 'c']),
            ("{0}|{0:D}|{0:G}|{1:X}", "(DayOfWeek)(input % 7), 'q'", [DayOfWeek.Sunday, 'q']),
            ("[{0}] [{0,3}] [{0,-2}]", "(object?)null", [null]),
            ("<{0}>", "input > 40 ? \"big\" : null", ["big"]),
            ("<{0}>", "input > 50 ? \"big\" : null", [null]),
            ("{0:N2}|{1:F1}|{2:0.00}|{1}", "input * 1.5, 2.25, (decimal)input / 8", [63.0, 2.25, 5.25m]),
            ("{{}}", "input", [42]),
            ("{0}|{1}|{1,6}", "input > 0, 2.5f", [true, 2.5f]),
            ("{0}|{0:abc}|{0,8}", "new Formattable(input)", [new Formattable(42)]),
            ("{0}|{0:q}", "(Base)new Derived()", [new Derived()]),
            ("{0}|{0:q}", "new Base()", [new Base()]),
            ("{0,-4}|{0:zz}|{0,3}", "new Plain()", [new Plain()]),
            ("{0,4}|{0}", "new Point(input)", [new Point(42)]),
            ("{0}|{0:m}|{1}", "(Chain)new Leaf(), new Middle()", [new Leaf(), new Middle()]),
            ("{0}|{0:g}|{1:g}", "(Root)new Generic<int>(), new Root()", [new Generic<int>(), new Root()]),
            ("{0}|{0:n}|{1}", "(Outer.NestedBase)new Outer.Nested(), new Outer.NestedBase()", [new Outer.Nested(), new Outer.NestedBase()]),
            ("{0}|{1}|{0:X}", "(int?)input, (int?)null", [42, null]),
            ("{0}|{1}|{0}", "\"text\", input", ["text", 42]),
            ("{2}{1}{0}", "'a', \"b\", 3", ['a', "b", 3]),
            ("{0,300}|{1}|{0,-280}|", "input, new string('y', 700)", [42, new string('y', 700)]),
            // What the encoded format keeps: escaped braces beside holes, spaces in a hole, an empty item format,
            // text past the first 256 characters, and alignments at either end of the encoding and past them.
            ("{0 , -4 :x}|{{{0}}}|{0}}}|{0:}|}}", "input", [42]),
            ("\u00FC{0}\u20AC{0,3}\uD834\uDD1E{{", "input", [42]),
            ("[{0,2047}]|[{0,-2048}]", "input", [42]),
            ("[{0,2048}]|[{0,-2049}]", "input", [42]),
        ];
        var checks = new StringBuilder();
        var count = 0;
        foreach (var (format, arguments, desktop) in cases)
        {
            var expected = string.Format(CultureInfo.InvariantCulture, format, desktop);
            Check($"string.Format({Quote(format)}, {arguments})", expected);
            Check($"Messages.Format({Quote(format)}, {arguments})", expected);
        }
        // With a provider: the invariant culture, and a custom formatter that writes integers itself and leaves the rest,
        // which string.Format then formats as it would without one.
        foreach (var (format, arguments, desktop) in new (string, string, object?[])[]
        {
            ("{0} (0x{0:x4}) [{0,6}]", "input", [42]),
            ("{0}|{1}|{0:X}|{1,5}", "input, 2.5", [42, 2.5]),
            ("{0}|{1:N1}|{1}", "\"s\", 2.25", ["s", 2.25]),
        })
        {
            Check($"string.Format(System.Globalization.CultureInfo.InvariantCulture, {Quote(format)}, {arguments})",
                string.Format(CultureInfo.InvariantCulture, format, desktop));
            Check($"string.Format(new IntegerFormatter(), {Quote(format)}, {arguments})",
                string.Format(new IntegerFormatter(), format, desktop));
        }
        Check("Messages.Format(new IntegerFormatter(), \"<{0,5}|{1}>\", input, 'c')",
            string.Format(new IntegerFormatter(), "<{0,5}|{1}>", 42, 'c'));
        // Four to eight arguments in a params array, built where the call is, through a forwarder or not; an array of
        // nine, or one built ahead of the call, stays string.Format's.
        foreach (var (format, arguments, desktop) in new (string, string, object?[])[]
        {
            ("{0}|{1}|{2}|{3:X2}", "input, 'c', \"s\", (byte)input", [42, 'c', "s", (byte)42]),
            ("{7}{6}{5}{4}{3}{2}{1}{0}", "1, 2L, 3.5, '4', \"5\", (short)6, input > 3 ? \"x\" : null, input", [1, 2L, 3.5, '4', "5", (short)6, "x", 42]),
            ("{0}{1}{2}{3}{4}{5}{6}{7}{8}", "1, 2, 3, 4, 5, 6, 7, 8, input", [1, 2, 3, 4, 5, 6, 7, 8, 42]),
            ("{7,5}|{0,-3}|{6:x}", "1, 2, 3, 4, 5, 6, input, input", [1, 2, 3, 4, 5, 6, 42, 42]),
        })
        {
            var expected = string.Format(CultureInfo.InvariantCulture, format, desktop);
            Check($"Messages.FormatMany({Quote(format)}, {arguments})", expected);
            Check($"string.Format({Quote(format)}, new object?[] {{ {arguments} }})", expected);
            Check($"string.Format(System.Globalization.CultureInfo.InvariantCulture, {Quote(format)}, new object?[] {{ {arguments} }})", expected);
        }
        Check("((Func<object?[], string>)(parts => string.Format(\"{0}-{3}\", parts)))(new object?[] { 1, 2, 3, input })", "1-42");
        // A format the call does not alone read keeps its text for the other reader.
        checks.AppendLine("string f;");
        Check("string.Format(f = \"{0,5}|\", input) + f", "   42|{0,5}|");
        Check("Messages.Format(f = \"<{0:x}>\", input) + f", "<2a><{0:x}>");
        // A format that is no constant keeps string.Format, and one that string.Format rejects still throws.
        Check("string.Format(input > 0 ? \"a{0}\" : \"b{0}\", input)", "a42");
        // A native integer's hexadecimal digits are its target's width: four bytes on wasm32, eight on wasm64.
        var width = target == WasmTarget.Wasm64 ? 16 : 8;
        Check("string.Format(\"{0:X}|{1:x4}|{1:X}\", (nint)(-input), (nuint)input)",
            new string('F', width - 2) + "D6|002a|2A");
        foreach (var rejected in new[] { "{0", "{1}", "}", "{0:{}" })
        {
            count++;
            checks.AppendLine(CultureInfo.InvariantCulture,
                $$"""try { _ = Messages.Format({{Quote(rejected)}}, input); return -{{count}}; } catch (FormatException) { }""");
        }

        Assert.Equal(count, Execute(checks.ToString(), count, optimize, target));

        void Check(string expression, string expected)
        {
            count++;
            checks.AppendLine(CultureInfo.InvariantCulture, $$"""if ({{expression}} != {{Quote(expected)}}) return -{{count}};""");
        }
    }

    // Every format the parser's pieces make that string.Format formats with three integers writes what desktop .NET
    // writes, read from its encoding by the compiled TypedCompositeFormat.
    [Theory]
    [MemberData(nameof(Targets))]
    public void EncodedFormatsFormatAsDesktopDoes(bool optimize, WasmTarget target)
    {
        var pieces = CompositeFormatParserTests.Pieces.Where(static piece => !piece.Contains("00000", StringComparison.Ordinal) &&
            !piece.Contains("99999", StringComparison.Ordinal)).ToArray();
        var checks = new StringBuilder();
        var count = 0;
        foreach (var first in pieces)
        {
            foreach (var second in pieces)
            {
                var format = first + second;
                string expected;
                try
                {
                    expected = string.Format(CultureInfo.InvariantCulture, format, 42, (byte)42, -42);
                }
                catch (FormatException)
                {
                    continue;
                }
                count++;
                checks.AppendLine(CultureInfo.InvariantCulture,
                    $$"""if (string.Format({{Quote(format)}}, input, (byte)input, -input) != {{Quote(expected)}}) return -{{count}};""");
            }
        }
        Assert.True(count > 500);

        Assert.Equal(count, Execute(checks.ToString(), count, optimize, target));
    }

    // An enum hole writes what desktop .NET writes for it, as idiomatic code meets it in a composite format, through a
    // forwarder, in an interpolated string and by ToString: named, combined and undefined values of flags and plain
    // enums over every underlying width, with each standard format letter in both cases and with alignment.
    [Theory]
    [MemberData(nameof(Targets))]
    public void EnumHolesFormatAsDesktopDoes(bool optimize, WasmTarget target)
    {
        (string Source, Enum Desktop)[] values =
        [
            ("Permission.None", Permission.None), ("Permission.Read", Permission.Read),
            ("Permission.ReadWrite", Permission.ReadWrite), ("(Permission.Read | Permission.Execute)", Permission.Read | Permission.Execute),
            ("(Permission)8", (Permission)8), ("(Permission)9", (Permission)9), ("(Permission)255", (Permission)255),
            ("Signed.Min", Signed.Min), ("Signed.MinusOne", Signed.MinusOne), ("Signed.Max", Signed.Max), ("(Signed)(-7)", (Signed)(-7)),
            ("Small.Low", Small.Low), ("(Small)(-128)", (Small)(-128)),
            ("(Wide.LowBit | Wide.HighBit)", Wide.LowBit | Wide.HighBit), ("(Wide)6", (Wide)6),
            ("Ordinary.Tenth", Ordinary.Tenth), ("(Ordinary)7", (Ordinary)7), ("(Ordinary)(-1)", (Ordinary)(-1)),
            ("Short.Top", Short.Top), ("(Short)2", (Short)2),
        ];
        string[] formats = ["", "G", "g", "D", "d", "X", "x", "F", "f"];
        var checks = new StringBuilder();
        var count = 0;
        foreach (var (source, desktop) in values)
        {
            foreach (var format in formats)
            {
                var item = format.Length == 0 ? "" : ":" + format;
                foreach (var alignment in format is "" or "X" or "F" ? new[] { "", ",14", ",-14" } : [""])
                {
                    var hole = "{0" + alignment + item + "}";
                    var expected = string.Format(CultureInfo.InvariantCulture, "<" + hole + ">", desktop);
                    Check($"string.Format({Quote("<" + hole + ">")}, {source})", expected);
                    Check($"Messages.Format({Quote("<" + hole + ">")}, {source})", expected);
                    Check($"$\"<{{{source}{alignment}{item}}}>\"", expected);
                }
                if (format.Length != 0)
                {
                    Check($"({source}).ToString({Quote(format)})", desktop.ToString(format));
                }
            }
        }

        Assert.Equal(count, Execute(checks.ToString(), count, optimize, target));

        void Check(string expression, string expected)
        {
            count++;
            checks.AppendLine(CultureInfo.InvariantCulture, $$"""if ({{expression}} != {{Quote(expected)}}) return -{{count}};""");
        }
    }

    // Each format here is a constant whose holes the rewrite types: none reaches string.Format's parser of composite
    // formats, and only a hole with an item format other than hexadecimal reaches its argument's format parser. A
    // format that is no constant, or one whose alignment its encoding cannot hold, keeps string.Format: the negative
    // controls.
    [Theory]
    [InlineData("string.Format(\"i={0} [{0,4}] [{0,-3}]\", input)", false, false)]
    [InlineData("Messages.Format(\"i={0} t={1}\", input, typeof(int))", false, false)]
    [InlineData("Messages.Format(\"{0} {1} {2}\", \"s\", input > 3 ? \"x\" : null, input.GetType())", false, false)]
    [InlineData("string.Format(\"{0:X4} {1:x2} {2:X}\", input, (byte)input, (long)input)", false, false)]
    [InlineData("Messages.Format(\"{0} {1} {2}\", (short)input, 'c', input > 0)", false, false)]
    [InlineData("string.Format(\"{0} {1}\", new[] { \"a\", \"b\" }[input & 1], input > 3 ? new[] { \"c\" }[0] : null)", false, false)]
    [InlineData("string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{0} (0x{0:x4})\", input)", false, false)]
    [InlineData("Messages.FormatMany(\"{0} {1} {2} {3:x2}\", input, 'c', \"s\", (byte)input)", false, false)]
    [InlineData("string.Format(\"{0:N2}\", input)", false, true)]
    [InlineData("string.Format(input > 0 ? \"a{0}\" : \"b{0}\", input)", true, true)]
    [InlineData("string.Format(\"[{0,2047}]\", input)", false, false)]
    [InlineData("string.Format(\"[{0,-2049}]\", input)", true, true)]
    public void ConstantFormatsReachOnlyTheirArgumentsFormatting(string expression, bool compositeParser, bool formatParser)
    {
        var reached = ReachedMethods($"public static int Run(int input) => ({expression}).Length;");

        Assert.Equal(compositeParser, reached.Contains("System.Text.ValueStringBuilder::AppendFormatHelper"));
        Assert.Equal(formatParser, reached.Any(static method => method is "System.Number::ParseFormatSpecifier" or "System.Number::FindSection"));
        Assert.DoesNotContain(reached, static method => method.StartsWith("System.Number::FormatDouble", StringComparison.Ordinal) ||
            method.StartsWith("System.Number::FormatFloat", StringComparison.Ordinal) || method.StartsWith("System.Decimal", StringComparison.Ordinal));
    }

    // Every call in a body of thousands is rewritten, in one pass over it.
    [Fact]
    public void ABodyOfManyFormatsIsRewrittenThroughout()
    {
        var body = new StringBuilder("public static int Run(int input) { var total = 0;");
        for (var index = 0; index < 2_000; index++)
        {
            body.Append(CultureInfo.InvariantCulture, $"total += Messages.Format(\"{index}:{{0}}{{1,3}}\", input + {index}, \"t\").Length;");
        }
        body.Append("return total; }");

        var reached = ReachedMethods(body.ToString());

        Assert.DoesNotContain("System.Text.ValueStringBuilder::AppendFormatHelper", reached);
        Assert.DoesNotContain("Messages::Format", reached);
    }

    private static List<string> ReachedMethods(string run)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "ConstantCompositeFormatProbe",
            $$"""
            using System;
            {{Types}}
            public static class EntryPoint
            {
                {{run}}
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: WasmTarget.Wasm32));
        return
        [
            .. result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
                .Select(static body => $"{body.Method.DeclaringType.FullName}::{body.Method.Definition.Name}")
                .Distinct(StringComparer.Ordinal),
        ];
    }

    private static int Execute(string checks, int count, bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source = $$"""
            using System;
            namespace ConstantCompositeFormatFixture;
            {{Types}}
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    {{checks}}
                    return {{count}};
                }
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedSource("ConstantCompositeFormatFixture", source)
            : assets.CompileSource("ConstantCompositeFormatFixture", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "ConstantCompositeFormatFixture.EntryPoint", "Run", [], Target: target));
        return ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 42, target, result.StaticDataEnd);
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private sealed class IntegerFormatter : IFormatProvider, ICustomFormatter
    {
        public object? GetFormat(Type? formatType) => formatType == typeof(ICustomFormatter) ? this : null;
        public string Format(string? format, object? arg, IFormatProvider? formatProvider) => arg is int i ? "#" + i + format : null!;
    }

    private sealed class Formattable(int value) : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) => "F" + value + ":" + format;
        public override string ToString() => "plain";
    }

    private class Base
    {
        public override string ToString() => "base";
    }

    private sealed class Derived : Base, IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) => "derived:" + format;
    }

    private sealed class Plain
    {
        public override string ToString() => "pl";
    }

    private class Chain
    {
        public override string ToString() => "chain";
    }

    private class Middle : Chain
    {
    }

    private sealed class Leaf : Middle, IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) => "leaf:" + format;
    }

    private class Root
    {
        public override string ToString() => "root";
    }

    private sealed class Generic<T> : Root, IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) => "generic:" + (default(T) is int ? "int:" : "other:") + format;
    }

    private static class Outer
    {
        public class NestedBase
        {
            public override string ToString() => "nested";
        }

        public sealed class Nested : NestedBase, IFormattable
        {
            public string ToString(string? format, IFormatProvider? formatProvider) => "nested:" + format;
        }
    }

    private readonly struct Point(int x)
    {
        public override string ToString() => "P" + x;
    }

    [Flags] private enum Permission : byte { None = 0, Read = 1, Write = 2, Execute = 4, ReadWrite = Read | Write }

    private enum Signed : long { Min = long.MinValue, MinusOne = -1, Zero = 0, Max = long.MaxValue }

    private enum Small : sbyte { Low = -5, High = 7 }

    [Flags] private enum Wide : ulong { LowBit = 1, HighBit = 0x8000000000000000 }

    private enum Ordinary { First, Second, Tenth = 10 }

    private enum Short : ushort { One = 1, Top = ushort.MaxValue }
}
