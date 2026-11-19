// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Globalization;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Buffer and Array.Copy read an array's element type as desktop .NET's runtime records it, an enum's being its
// underlying primitive: Buffer takes rectangular and enum arrays of primitives, and Array.Copy copies between
// primitive element types that are the same integral type up to sign and widens where desktop does, for every pair
// of primitive and enum element types. The expected values are desktop .NET's own, computed here.
public sealed class ArrayElementTypeCompilationTests
{
    public enum EB : byte { }

    public enum EL : long { }

    // Each element type's C# name, a source array of it, and how an element is described: integers and enums by
    // their numeric value (nuint read as signed, so a 32-bit target describes it as 64-bit desktop does), floating
    // point by its shortest round-trip text.
    private static readonly (string Name, string Literal, Array Source, string Describe)[] Types =
    [
        ("bool", "new bool[] { true, false, true }", new[] { true, false, true }, "(e ? \"T\" : \"F\")"),
        ("char", "new char[] { 'A', '\\uFFFE', '0' }", new[] { 'A', '￾', '0' }, "((int)e).ToString(CultureInfo.InvariantCulture)"),
        ("sbyte", "new sbyte[] { -2, 100, 127 }", new sbyte[] { -2, 100, 127 }, "e.ToString(CultureInfo.InvariantCulture)"),
        ("byte", "new byte[] { 254, 100, 1 }", new byte[] { 254, 100, 1 }, "e.ToString(CultureInfo.InvariantCulture)"),
        ("short", "new short[] { -2, 30000, 7 }", new short[] { -2, 30000, 7 }, "e.ToString(CultureInfo.InvariantCulture)"),
        ("ushort", "new ushort[] { 65534, 30000, 7 }", new ushort[] { 65534, 30000, 7 }, "e.ToString(CultureInfo.InvariantCulture)"),
        ("int", "new int[] { -2, 2000000000, 16777217 }", new[] { -2, 2000000000, 16777217 }, "e.ToString(CultureInfo.InvariantCulture)"),
        ("uint", "new uint[] { 4294967294, 2000000000, 16777217 }", new uint[] { 4294967294, 2000000000, 16777217 }, "e.ToString(CultureInfo.InvariantCulture)"),
        ("long", "new long[] { -2, 9007199254740993, 5 }", new long[] { -2, 9007199254740993, 5 }, "e.ToString(CultureInfo.InvariantCulture)"),
        ("ulong", "new ulong[] { 18446744073709551614, 9007199254740993, 5 }", new ulong[] { 18446744073709551614, 9007199254740993, 5 }, "e.ToString(CultureInfo.InvariantCulture)"),
        ("float", "new float[] { -2.5f, 1e30f, 16777217f }", new[] { -2.5f, 1e30f, 16777217f }, "e.ToString(\"R\", CultureInfo.InvariantCulture)"),
        ("double", "new double[] { -2.5, 1e300, 0.1 }", new[] { -2.5, 1e300, 0.1 }, "e.ToString(\"R\", CultureInfo.InvariantCulture)"),
        ("nint", "new nint[] { -2, 100, 7 }", new nint[] { -2, 100, 7 }, "((long)e).ToString(CultureInfo.InvariantCulture)"),
        ("nuint", "new nuint[] { 65534, 100, 7 }", new nuint[] { 65534, 100, 7 }, "((long)(nint)e).ToString(CultureInfo.InvariantCulture)"),
        ("DayOfWeek", "new DayOfWeek[] { DayOfWeek.Friday, (DayOfWeek)(-2), DayOfWeek.Monday }",
            new[] { DayOfWeek.Friday, (DayOfWeek)(-2), DayOfWeek.Monday }, "((int)e).ToString(CultureInfo.InvariantCulture)"),
        ("EB", "new EB[] { (EB)254, (EB)1, (EB)0 }", new[] { (EB)254, (EB)1, (EB)0 }, "((byte)e).ToString(CultureInfo.InvariantCulture)"),
        ("EL", "new EL[] { (EL)(-2), (EL)5, (EL)7 }", new[] { (EL)(-2), (EL)5, (EL)7 }, "((long)e).ToString(CultureInfo.InvariantCulture)"),
    ];

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void BuffersAndCopiesReadElementTypesAsDesktop(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "ArrayElementTypes", Source(), "EntryPoint", optimized, target, 0, [])));
    }

    private static string Source()
    {
        var checks = new StringBuilder();
        var methods = new StringBuilder();
        var check = 100;
        // A check that throws reports its number plus 10000.
        void Expect(string condition)
        {
            check++;
            checks.AppendLine(CultureInfo.InvariantCulture,
                $"        try {{ if (!({condition})) return {check}; }} catch (Exception) {{ return {check + 10000}; }}");
        }
        void Throws<TException>(string statement) where TException : Exception =>
            checks.AppendLine(CultureInfo.InvariantCulture,
                $"        try {{ {statement}; return {++check}; }} catch ({typeof(TException).Name}) {{ }}");

        // Array.Copy between every pair of element types: the destination's elements, or the exception.
        foreach (var source in Types)
        {
            foreach (var destination in Types)
            {
                var name = $"Copy_{source.Name}_{destination.Name}";
                methods.AppendLine(CultureInfo.InvariantCulture, $$"""
                        private static string {{name}}()
                        {
                            var destination = new {{destination.Name}}[3];
                            try { Array.Copy({{source.Literal}}, destination, 3); }
                            catch (Exception exception) { return exception is ArrayTypeMismatchException ? "mismatch" : "other"; }
                            var text = "";
                            foreach (var e in destination) text += {{destination.Describe}} + ";";
                            return text;
                        }
                    """);
                Expect($"{name}() == \"{CopyOnDesktop(source.Source, destination.Name)}\"");
            }
        }

        // Rectangular arrays copy and widen as one-dimensional ones do; ConstrainedCopy never widens.
        var rectangularSource = new int[,] { { -1, 2 }, { 3, -4 } };
        var rectangularTarget = new long[2, 2];
        Array.Copy(rectangularSource, rectangularTarget, 4);
        Expect($"WideningRectangularCopy() == \"{string.Join(";", rectangularTarget.Cast<long>())}\"");
        int[] ints = [1, 2];
        uint[] uints = [1, 2];
        DayOfWeek[] daysOfWeek = [DayOfWeek.Monday, DayOfWeek.Friday];
        Expect($"ConstrainedCopy(new int[] {{ 1, 2 }}, new long[2]) == \"{ConstrainedCopyOnDesktop(ints, new long[2])}\"");
        Expect($"ConstrainedCopy(new uint[] {{ 1, 2 }}, new int[2]) == \"{ConstrainedCopyOnDesktop(uints, new int[2])}\"");
        Expect($"ConstrainedCopy(new DayOfWeek[] {{ DayOfWeek.Monday, DayOfWeek.Friday }}, new int[2]) == \"{ConstrainedCopyOnDesktop(daysOfWeek, new int[2])}\"");
        Throws<RankException>("Array.Copy(new int[2, 1], new long[2], 2)");

        // Buffer takes primitive arrays of any shape and arrays of enums, and rejects anything else.
        Expect($"Buffer.ByteLength(new int[2, 3]) == {Buffer.ByteLength(new int[2, 3])}");
        Expect($"Buffer.ByteLength(new double[2, 2, 2]) == {Buffer.ByteLength(new double[2, 2, 2])}");
        Expect($"Buffer.ByteLength(Array.CreateInstance(typeof(short), new[] {{ 2, 3 }}, new[] {{ 1, -2 }})) == " +
            Buffer.ByteLength(Array.CreateInstance(typeof(short), [2, 3], [1, -2])));
        Expect($"Buffer.ByteLength(new DayOfWeek[3]) == {Buffer.ByteLength(new DayOfWeek[3])}");
        Expect($"Buffer.ByteLength(new DayOfWeek[2, 2]) == {Buffer.ByteLength(new DayOfWeek[2, 2])}");
        Expect($"Buffer.ByteLength(new EL[2]) == {Buffer.ByteLength(new EL[2])}");
        Expect($"Buffer.ByteLength(new nint[3]) == IntPtr.Size * 3");
        Expect($"Buffer.GetByte(new short[,] {{ {{ 1, 2 }}, {{ 0x0304, 5 }} }}, 5) == {Buffer.GetByte(new short[,] { { 1, 2 }, { 0x0304, 5 } }, 5)}");
        var rectangular = new int[2, 3] { { 1, 2, 3 }, { 4, 5, 6 } };
        var copied = new int[2];
        Buffer.BlockCopy(rectangular, 4, copied, 0, 8);
        Expect($"RectangularBlockCopy() == {copied[0] * 10 + copied[1]}");
        var days = new[] { DayOfWeek.Tuesday, DayOfWeek.Saturday };
        var bytes = new byte[8];
        Buffer.BlockCopy(days, 0, bytes, 0, 8);
        Expect($"EnumBlockCopy() == {BitConverter.ToInt64(bytes)}L");
        Throws<ArgumentException>("Buffer.ByteLength(new object[1])");
        Throws<ArgumentException>("Buffer.ByteLength(new string[1, 1])");
        Throws<ArgumentException>("Buffer.ByteLength(new Guid[1])");
        Throws<ArgumentException>("Buffer.BlockCopy(new object[2, 2], 0, new byte[8], 0, 4)");
        Expect($"((object)new uint[1] is int[]) == {((object)new uint[1] is int[]).ToString().ToLowerInvariant()}");
        Expect($"((object)new DayOfWeek[1] is int[]) == {((object)new DayOfWeek[1] is int[]).ToString().ToLowerInvariant()}");

        return $$"""
            using System;
            using System.Globalization;

            public enum EB : byte { }
            public enum EL : long { }

            public static class EntryPoint
            {
            {{methods}}
                private static string WideningRectangularCopy()
                {
                    var source = new int[,] { { -1, 2 }, { 3, -4 } };
                    var target = new long[2, 2];
                    Array.Copy(source, target, 4);
                    return target[0, 0] + ";" + target[0, 1] + ";" + target[1, 0] + ";" + target[1, 1];
                }

                private static string ConstrainedCopy(Array source, Array destination)
                {
                    try { Array.ConstrainedCopy(source, 0, destination, 0, 2); }
                    catch (ArrayTypeMismatchException) { return "mismatch"; }
                    return "copied";
                }

                private static int RectangularBlockCopy()
                {
                    var data = new int[2, 3] { { 1, 2, 3 }, { 4, 5, 6 } };
                    var copied = new int[2];
                    Buffer.BlockCopy(data, 4, copied, 0, 8);
                    return copied[0] * 10 + copied[1];
                }

                private static long EnumBlockCopy()
                {
                    var days = new[] { DayOfWeek.Tuesday, DayOfWeek.Saturday };
                    var bytes = new byte[8];
                    Buffer.BlockCopy(days, 0, bytes, 0, 8);
                    return BitConverter.ToInt64(bytes);
                }

                public static int Run(int input)
                {
            {{checks}}
                    return 42;
                }
            }
            """;
    }

    private static string CopyOnDesktop(Array source, string destinationName)
    {
        var destinationType = Types.Single(type => type.Name == destinationName).Source.GetType().GetElementType()!;
        var destination = Array.CreateInstance(destinationType, 3);
        try
        {
            Array.Copy((Array)source.Clone(), destination, 3);
        }
        catch (Exception exception)
        {
            return exception is ArrayTypeMismatchException ? "mismatch" : "other";
        }
        var text = new StringBuilder();
        foreach (var element in destination)
        {
            text.Append(element switch
            {
                bool value => value ? "T" : "F",
                char value => ((int)value).ToString(CultureInfo.InvariantCulture),
                float value => value.ToString("R", CultureInfo.InvariantCulture),
                double value => value.ToString("R", CultureInfo.InvariantCulture),
                nint value => ((long)value).ToString(CultureInfo.InvariantCulture),
                nuint value => ((long)(nint)value).ToString(CultureInfo.InvariantCulture),
                DayOfWeek value => ((int)value).ToString(CultureInfo.InvariantCulture),
                EB value => ((byte)value).ToString(CultureInfo.InvariantCulture),
                EL value => ((long)value).ToString(CultureInfo.InvariantCulture),
                IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
                _ => throw new InvalidOperationException(),
            });
            text.Append(';');
        }
        return text.ToString();
    }

    private static string ConstrainedCopyOnDesktop(Array source, Array destination)
    {
        try
        {
            Array.ConstrainedCopy(source, 0, destination, 0, 2);
        }
        catch (ArrayTypeMismatchException)
        {
            return "mismatch";
        }
        return "copied";
    }
}
