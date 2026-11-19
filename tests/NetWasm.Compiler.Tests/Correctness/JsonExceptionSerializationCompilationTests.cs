// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// JsonException has upstream's legacy serialization members, so a library exception derived from it binds: its
// (SerializationInfo, StreamingContext) constructor calls the base one, and its GetObjectData override, which a
// constructed exception keeps as a virtual method, calls the base one. NetWasm does not support formatter
// serialization (SerializationInfo throws PlatformNotSupportedException), so the fixture compares the exception's
// ordinary use against desktop .NET.
public sealed class JsonExceptionSerializationCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ADerivedJsonExceptionWithSerializationMembersBindsAndBehavesAsOnDesktop() =>
        runner.Run(new("JsonExceptionSerialization", "NetWasm.Correctness.JsonExceptionSerialization", Source, [0, 1, 2])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
        #pragma warning disable SYSLIB0051, CS0672
        using System;
        using System.Runtime.Serialization;
        using System.Text.Json;

        namespace NetWasm.Correctness.JsonExceptionSerialization;

        public sealed class ParseFailure : JsonException
        {
            public ParseFailure(string message, string? path, long? line, long? position) : base(message, path, line, position) { }
            private ParseFailure(SerializationInfo info, StreamingContext context) : base(info, context) => Code = info.GetInt32("Code");
            public int Code { get; init; }

            public override void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                base.GetObjectData(info, context);
                info.AddValue("Code", Code);
            }
        }

        public static class EntryPoint
        {
            private static int _trace;

            public static int Trace() => _trace;

            private static int Hash(string? text)
            {
                var hash = 17;
                foreach (var c in text ?? "<null>") hash = unchecked(hash * 31 + c);
                return hash;
            }

            private static void Fail(int input) => throw input switch
            {
                0 => new ParseFailure("bad token", "$.items[3]", 7, 42) { Code = 3 },
                1 => new ParseFailure("no position", null, null, null),
                _ => new ParseFailure("", "$", 0, long.MaxValue) { Code = -1 },
            };

            public static int Run(int input)
            {
                try
                {
                    Fail(input);
                    return 0;
                }
                catch (JsonException failure)
                {
                    var hash = Hash(failure.Message) ^ Hash(failure.Path);
                    hash = unchecked(hash * 31 + (int)(failure.LineNumber ?? -1));
                    hash = unchecked(hash * 31 + (int)((failure.BytePositionInLine ?? -1) >> 3));
                    hash = unchecked(hash * 31 + (failure is ParseFailure { Code: var code } ? code : 99));
                    _trace = hash;
                    return hash;
                }
            }
        }
        """;
}
