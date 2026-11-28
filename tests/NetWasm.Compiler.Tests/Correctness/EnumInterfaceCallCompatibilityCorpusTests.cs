// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// An enum declares no methods: System.Enum implements its interface methods, some explicitly
// (ISpanFormattable.TryFormat, IConvertible's). A constrained call on an enum binds to them through System.Enum's
// method implementations, and a box of an enum passed to an interface call (string.Format's holes) binds to the same
// method; a struct's inherited ValueType methods and a default interface method bind the same way. All match desktop.
public sealed class EnumInterfaceCallCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void EnumInterfaceCallsMatchDesktop() =>
        runner.Run(new(
            "EnumInterfaceCallCompatibilityCorpus",
            "NetWasm.Correctness.EnumInterfaceCalls",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    private const string Source = """
        using System;

        namespace NetWasm.Correctness.EnumInterfaceCalls;

        public enum Color { Red, Green, Blue }

        [Flags]
        public enum Access : byte { None = 0, Read = 1, Write = 2, Execute = 4 }

        public struct Point
        {
            public int X;
            public int Y;
            public Point(int x, int y) { X = x; Y = y; }
        }

        public interface IGreeter
        {
            string Hello() => "hello";
        }

        public struct Greeter : IGreeter { }

        public struct LoudGreeter : IGreeter
        {
            public string Hello() => "HELLO";
        }

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => SpanFormat(Color.Green) + SpanFormat(Access.Read | Access.Write),
                1 => Convert(Color.Blue) * 10 + Convert(Access.Execute),
                2 => Checksum(Format(Color.Red, "D") + Format(Access.Write, "X") + Format(Color.Blue, "G")),
                3 => Checksum(string.Format("{0}/{1}/{2:D}", Color.Green, Access.Read | Access.Execute, Color.Blue)),
                4 => Checksum($"{Color.Red} {Access.None} {(Access)8}"),
                5 => Compare(Color.Green, Color.Red) * 100 + Compare(Access.Read, Access.Write) * 10 + Compare(Color.Blue, Color.Blue),
                6 => Checksum(Describe(new Point(3, 4)) + Describe(new Point(0, 0))),
                7 => Checksum(Greet(new Greeter()) + Greet(new LoudGreeter())),
                8 => Checksum(Convertible(Color.Blue) + "|" + Convertible(Access.Read | Access.Execute)),
                _ => -1,
            };

            public static int Trace() => 0;

            private static int SpanFormat<T>(T value) where T : ISpanFormattable
            {
                Span<char> buffer = stackalloc char[64];
                return value.TryFormat(buffer, out var written, default, null) ? Checksum(buffer[..written].ToString()) : -1;
            }

            private static int Convert<T>(T value) where T : IConvertible => value.ToInt32(null);

            private static int Compare<T>(T value, object other) where T : IComparable => Math.Sign(value.CompareTo(other)) + 1;

            // ValueType's Equals, inherited; its ToString names the type, which fixtures do not read.
            private static string Describe<T>(T value) where T : struct =>
                value.Equals(value) + ":" + value.Equals(default(T)) + ":" + value.Equals((object)42);

            private static string Greet<T>(T greeter) where T : IGreeter => greeter.Hello();

            private static string Convertible<T>(T value) where T : IConvertible =>
                value.ToInt64(null) + "," + value.ToDouble(null) + "," + value.ToString(null) + "," + value.ToBoolean(null) +
                "," + value.GetTypeCode();

            private static string Format<T>(T value, string format) where T : IFormattable => value.ToString(format, null);

            private static int Checksum(string text)
            {
                var sum = 0;
                for (var index = 0; index < text.Length; index++)
                {
                    sum = unchecked(sum * 31 + text[index]);
                }
                return sum & 0x7fffffff;
            }
        }
        """;
}
