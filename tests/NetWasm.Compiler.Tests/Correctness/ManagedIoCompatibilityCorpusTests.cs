namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ManagedIoCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ManagedIoMatchesDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "ManagedIoCompatibilityCorpus",
            "NetWasm.Correctness.ManagedIo",
            """
            using System;
            using System.IO;
            using System.Text;

            namespace NetWasm.Correctness.ManagedIo;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var result = input;
                    result = Mix(result, ExerciseMemoryStream());
                    result = Mix(result, ExerciseStreamCopy());
                    result = Mix(result, ExerciseBufferedStream());
                    result = Mix(result, ExerciseStringWriter());
                    result = Mix(result, ExerciseBroadcastingWriter());
                    result = Mix(result, ExerciseStringReader());
                    result = Mix(result, ExerciseEncodedText());
                    result = Mix(result, ExerciseBinaryRoundTrip());
                    result = Mix(result, ExerciseDerivedMemoryStreamBinaryReader());
                    result = Mix(result, ExerciseLexicalPaths());
                    result = Mix(result, ExercisePathComposition());
                    result = Mix(result, ExerciseIoExceptionData());
                    try
                    {
                        _ = ReadTruncatedBinary();
                    }
                    catch (EndOfStreamException)
                    {
                        result = Mix(result, 1);
                    }
                    return result;
                }

                private static int Mix(int current, int value) =>
                    unchecked(current * 31 + value);

                private static int ExerciseMemoryStream()
                {
                    using var stream = new MemoryStream();
                    stream.Write(new byte[] { 1, 2, 3, 4 }, 0, 4);
                    stream.Position = 1;
                    var second = stream.ReadByte();
                    stream.SetLength(3);
                    return (int)stream.Length * 100 + second * 10 + stream.ToArray()[2];
                }

                private static int ExerciseStreamCopy()
                {
                    using var source = new MemoryStream(new byte[] { 3, 4, 5 });
                    using var destination = new MemoryStream();
                    destination.WriteByte(2);
                    source.CopyTo(destination, 2);
                    var bytes = destination.ToArray();
                    return bytes[0] * 1000 + bytes[1] * 100 + bytes[2] * 10 + bytes[3];
                }

                private static int ExerciseBufferedStream()
                {
                    using var storage = new MemoryStream();
                    using var stream = new BufferedStream(storage, 2);
                    stream.WriteByte(6);
                    stream.Write(new byte[] { 7, 8 }, 0, 2);
                    stream.Flush();
                    stream.Position = 0;
                    return stream.ReadByte() * 100 + stream.ReadByte() * 10 + stream.ReadByte();
                }

                private static int ExerciseStringWriter()
                {
                    using var writer = new StringWriter();
                    writer.Write('a');
                    writer.Write(12);
                    writer.WriteLine("b");
                    var value = writer.ToString();
                    return value.Length * 100 + value[0] + value[3];
                }

                private static int ExerciseStringReader()
                {
                    using var reader = new StringReader("ab\ncd");
                    var first = reader.Read();
                    var rest = reader.ReadLine()!;
                    var final = reader.ReadToEnd();
                    return first + rest.Length * 100 + final.Length * 1000;
                }

                private static int ExerciseBroadcastingWriter()
                {
                    using var first = new StringWriter();
                    using var second = new StringWriter();
                    var inputs = new TextWriter[] { first, second };
                    using var writer = TextWriter.CreateBroadcasting(inputs);
                    inputs[0] = TextWriter.Null;
                    writer.NewLine = "|";
                    writer.Write(12);
                    writer.WriteLine("x");
                    writer.Flush();

                    var rejectsNullArray = false;
                    try
                    {
                        _ = TextWriter.CreateBroadcasting((TextWriter[])null!);
                    }
                    catch (ArgumentNullException)
                    {
                        rejectsNullArray = true;
                    }

                    var rejectsNullWriter = false;
                    try
                    {
                        _ = TextWriter.CreateBroadcasting(first, null!);
                    }
                    catch (ArgumentNullException)
                    {
                        rejectsNullWriter = true;
                    }

                    return first.ToString() == "12x|" &&
                        second.ToString() == "12x|" &&
                        ReferenceEquals(TextWriter.CreateBroadcasting(), TextWriter.Null) &&
                        rejectsNullArray && rejectsNullWriter
                            ? 1
                            : 0;
                }

                private static int ExerciseEncodedText()
                {
                    using var stream = new MemoryStream();
                    using (var writer = new StreamWriter(
                        stream,
                        new UTF8Encoding(false),
                        8,
                        true))
                    {
                        writer.Write("hé");
                        writer.Flush();
                    }
                    stream.Position = 0;
                    using var reader = new StreamReader(
                        stream,
                        Encoding.UTF8,
                        true,
                        8,
                        true);
                    var value = reader.ReadToEnd();
                    return value.Length * 1000 + value[0] * 10 + value[1];
                }

                private static int ExerciseBinaryRoundTrip()
                {
                    using var stream = new MemoryStream();
                    using (var writer = new BinaryWriter(
                        stream,
                        new UTF8Encoding(false),
                        true))
                    {
                        writer.Write(true);
                        writer.Write((short)-12);
                        writer.Write(345678);
                        writer.Write("xy");
                        writer.Flush();
                    }
                    stream.Position = 0;
                    using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                    return (reader.ReadBoolean() ? 1_000_000 : 0) +
                        (reader.ReadInt16() + 12) * 100_000 +
                        reader.ReadInt32() + reader.ReadString().Length;
                }

                private static int ExerciseDerivedMemoryStreamBinaryReader()
                {
                    using var stream = new TrackingMemoryStream(
                        new byte[] { 0x78, 0x56, 0x34, 0x12 });
                    using var reader = new BinaryReader(stream);
                    return reader.ReadInt32() == 0x12345678 && stream.ReadCount > 0 ? 1 : 0;
                }

                private static int ExerciseLexicalPaths()
                {
                    var combined = Path.Combine("root", "folder", "file.txt");
                    var directory = Path.GetDirectoryName(combined)!;
                    var name = Path.GetFileNameWithoutExtension(combined)!;
                    var extension = Path.GetExtension(combined)!;
                    var changed = Path.ChangeExtension(combined, ".bin")!;
                    return directory.Length * 1000 + name.Length * 100 +
                        extension.Length * 10 + (changed.EndsWith(".bin") ? 1 : 0);
                }

                private static int ExercisePathComposition()
                {
                    var full = Path.GetFullPath("../d/./file", "/a/b/c");
                    var combined = Path.Combine("ignored", "/root", "child", "file");
                    var joined = Path.Join("a", null, "b", "c");
                    var relative = Path.GetRelativePath("/a/b/c", "/a/d/");
                    var invalidFile = Path.GetInvalidFileNameChars();
                    var invalidPath = Path.GetInvalidPathChars();
                #pragma warning disable CS0618
                    var legacyInvalidPath = Path.InvalidPathChars;
                #pragma warning restore CS0618
                    return full == "/a/b/d/file" &&
                        combined == "/root/child/file" &&
                        joined == "a/b/c" &&
                        relative == "../../d/" &&
                        invalidFile.Length == 2 && invalidFile[0] == '\0' && invalidFile[1] == '/' &&
                        invalidPath.Length == 1 && invalidPath[0] == '\0' &&
                        legacyInvalidPath.Length == 1 && legacyInvalidPath[0] == '\0'
                            ? 1
                            : 0;
                }

                private static int ExerciseIoExceptionData()
                {
                    var file = new FileNotFoundException("missing", "a.bin");
                    var directory = new DirectoryNotFoundException("missing");
                    return file.FileName!.Length * 100 + directory.Message.Length;
                }

                private static int ReadTruncatedBinary()
                {
                    using var stream = new MemoryStream(new byte[] { 1, 2 });
                    using var reader = new BinaryReader(stream);
                    return reader.ReadInt32();
                }

                private sealed class TrackingMemoryStream : MemoryStream
                {
                    public int ReadCount;

                    public TrackingMemoryStream(byte[] bytes) : base(bytes) { }

                    public override int Read(Span<byte> buffer)
                    {
                        ReadCount++;
                        return base.Read(buffer);
                    }
                }

                public static int Trace() => 0;
            }
            """,
            [17])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
