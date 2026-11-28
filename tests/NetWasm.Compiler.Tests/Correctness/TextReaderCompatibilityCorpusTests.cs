// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// StreamReader, StringReader and TextReader are upstream's: every way of reading lines and blocks, synchronously and
// asynchronously, over text whose line ends (\r, \n, \r\n) and long lines fall at and across the reader's buffer
// boundaries, with and without byte order marks, answers as desktop .NET 11 does.
public sealed class TextReaderCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ReadersMatchDesktop() =>
        runner.Run(new(
            "TextReaderCompatibilityCorpus",
            "NetWasm.Correctness.TextReaders",
            """
            using System;
            using System.IO;
            using System.Text;

            namespace NetWasm.Correctness.TextReaders;

            public static class EntryPoint
            {
                private static int _hash;

                private static void Mix(int value) => _hash = unchecked((_hash ^ value) * 16777619);

                private static void Mix(string? text)
                {
                    if (text is null)
                    {
                        Mix(-1);
                        return;
                    }
                    Mix(text.Length);
                    foreach (var c in text) Mix(c);
                }

                // Lines of every ending, an empty line, lines longer than the reader's buffer, and a carriage return
                // that ends one buffer while its line feed starts the next.
                private static string Text(int variant)
                {
                    var builder = new StringBuilder();
                    var endings = variant switch { 0 => new[] { "\n" }, 1 => new[] { "\r\n" }, 2 => new[] { "\r" }, _ => new[] { "\n", "\r\n", "\r", "\r\n" } };
                    for (var line = 0; line < 40; line++)
                    {
                        var length = line % 7 == 3 ? 0 : (line * 37 + variant * 11) % 300;
                        for (var i = 0; i < length; i++) builder.Append((char)('a' + (i + line) % 26));
                        builder.Append(endings[line % endings.Length]);
                    }
                    builder.Append(new string('x', 127)).Append("\r\n").Append("tail without an end");
                    return builder.ToString();
                }

                private static MemoryStream Bytes(string text, Encoding encoding, bool preamble)
                {
                    var stream = new MemoryStream();
                    if (preamble) stream.Write(encoding.GetPreamble());
                    stream.Write(encoding.GetBytes(text));
                    stream.Position = 0;
                    return stream;
                }

                private static void StreamLines(string text, Encoding encoding, bool preamble, bool detect, int bufferSize)
                {
                    using var reader = new StreamReader(Bytes(text, encoding, preamble), encoding, detect, bufferSize);
                    Mix(reader.Peek());
                    string? line;
                    while ((line = reader.ReadLine()) is not null) Mix(line);
                    Mix(reader.ReadLine());
                    Mix(reader.EndOfStream ? 1 : 0);
                    Mix(reader.CurrentEncoding.CodePage);
                }

                private static void StreamLinesAsync(string text, Encoding encoding, int bufferSize)
                {
                    using var reader = new StreamReader(Bytes(text, encoding, false), encoding, false, bufferSize);
                    string? line;
                    while ((line = reader.ReadLineAsync().GetAwaiter().GetResult()) is not null) Mix(line);
                    Mix(reader.ReadLineAsync(default).AsTask().GetAwaiter().GetResult());
                }

                private static void StreamBlocks(string text, Encoding encoding, int bufferSize)
                {
                    using var reader = new StreamReader(Bytes(text, encoding, false), encoding, false, bufferSize);
                    var buffer = new char[50];
                    Mix(reader.Read());
                    Mix(reader.ReadBlock(buffer, 0, buffer.Length));
                    Mix(new string(buffer));
                    Mix(reader.Read(buffer.AsSpan(3, 20)));
                    Mix(new string(buffer));
                    Mix(reader.ReadLine());
                    Mix(reader.ReadToEnd());
                    Mix(reader.Read());
                }

                private static void StreamToEndAsync(string text, Encoding encoding, int bufferSize)
                {
                    using var reader = new StreamReader(Bytes(text, encoding, false), encoding, false, bufferSize);
                    Mix(reader.ReadLine());
                    Mix(reader.ReadToEndAsync().GetAwaiter().GetResult());
                }

                private static void StringLines(string text)
                {
                    using var reader = new StringReader(text);
                    Mix(reader.Peek());
                    string? line;
                    while ((line = reader.ReadLine()) is not null) Mix(line);
                    using var again = new StringReader(text);
                    while ((line = again.ReadLineAsync().GetAwaiter().GetResult()) is not null) Mix(line);
                    using var blocks = new StringReader(text);
                    var buffer = new char[33];
                    Mix(blocks.ReadBlock(buffer, 0, buffer.Length));
                    Mix(new string(buffer));
                    Mix(blocks.Read(buffer.AsSpan(1, 9)));
                    Mix(blocks.ReadToEnd());
                    Mix(blocks.ReadToEndAsync().GetAwaiter().GetResult());
                }

                private static void Closed()
                {
                    var reader = new StringReader("x");
                    reader.Dispose();
                    try { reader.ReadLine(); Mix(1); } catch (ObjectDisposedException) { Mix(2); }
                    var stream = new StreamReader(new MemoryStream(new byte[] { 65 }));
                    stream.Dispose();
                    try { stream.ReadLine(); Mix(3); } catch (ObjectDisposedException) { Mix(4); }
                }

                public static int Run(int input)
                {
                    _hash = unchecked((int)2166136261);
                    var text = Text(input);
                    foreach (var bufferSize in new[] { 1, 128, 129, 1024 })
                    {
                        StreamLines(text, Encoding.UTF8, preamble: input % 2 == 0, detect: true, bufferSize);
                        StreamLines(text, Encoding.Unicode, preamble: true, detect: true, bufferSize);
                        StreamLinesAsync(text, Encoding.UTF8, bufferSize);
                        StreamBlocks(text, Encoding.UTF8, bufferSize);
                        StreamToEndAsync(text, Encoding.Unicode, bufferSize);
                    }
                    StringLines(text);
                    Closed();
                    return _hash;
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
