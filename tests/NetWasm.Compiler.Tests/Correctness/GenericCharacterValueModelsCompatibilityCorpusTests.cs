namespace NetWasm.Compiler.Tests.Correctness;

public sealed class GenericCharacterValueModelsCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void GenericCharacterModelsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "GenericCharacterValueModelsCompatibilityCorpus",
            "NetWasm.Correctness.GenericCharacterValueModels",
            """
            using System;

            namespace NetWasm.Correctness.GenericCharacterValueModels;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    1 => ParseAndFormatUtf16(),
                    2 => ParseAndFormatUtf8(),
                    3 => RejectInvalidVersions(),
                    4 => ParseAndFormatGuidUtf16(),
                    5 => ParseAndFormatGuidUtf8(),
                    6 => ExerciseGuidWrappers(),
                    7 => ParseFrameworkName(),
                    8 => RejectInvalidFrameworkName(),
                    _ => -1,
                };

                private static int ParseAndFormatUtf16()
                {
                    var value = Version.Parse("10.20.300.4000".AsSpan());
                    Span<char> destination = stackalloc char[14];
                    return value.TryFormat(destination, 4, out var written) &&
                        written == 14 && destination[0] == '1' && destination[13] == '0' &&
                        value.ToString(4) == "10.20.300.4000"
                            ? value.Major + value.Minor + value.Build + value.Revision
                            : -1;
                }

                private static int ParseAndFormatUtf8()
                {
                    var value = Version.Parse("10.20.300.4000"u8);
                    Span<byte> destination = stackalloc byte[14];
                    return value.TryFormat(destination, 4, out var written) &&
                        written == 14 && destination[0] == (byte)'1' && destination[13] == (byte)'0'
                            ? value.Major + value.Minor + value.Build + value.Revision
                            : -1;
                }

                private static int RejectInvalidVersions() =>
                    !Version.TryParse("1"u8, out _) &&
                    !Version.TryParse("1.2.3.4.5"u8, out _) &&
                    !Version.TryParse("1.-2"u8, out _)
                        ? 1
                        : -1;

                private static int ParseAndFormatGuidUtf16()
                {
                    const string digits = "00112233445566778899aabbccddeeff";
                    var value = Guid.ParseExact(digits.AsSpan(), "N".AsSpan());
                    Span<char> destination = stackalloc char[36];
                    if (!value.TryFormat(destination, out var written, "D")) return -10;
                    if (written != 36) return -11;
                    if (destination[8] != '-') return -12;
                    Span<char> compact = stackalloc char[32];
                    if (!value.TryFormat(compact, out written, "N") || written != 32) return -13;
                    for (var index = 0; index < compact.Length; index++)
                    {
                        if (compact[index] != digits[index]) return -100 - index;
                    }
                    var text = value.ToString("N");
                    if (text.Length != digits.Length) return -200 - text.Length;
                    for (var index = 0; index < text.Length; index++)
                    {
                        if (text[index] != digits[index]) return -300 - index;
                    }
                    return value.ToByteArray()[0] * 100 + value.ToByteArray()[15];
                }

                private static int ParseAndFormatGuidUtf8()
                {
                    var value = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"u8);
                    Span<byte> destination = stackalloc byte[68];
                    return value.TryFormat(destination, out var written, "X") &&
                        written == 68 && destination[0] == (byte)'{' && destination[67] == (byte)'}'
                            ? value.ToByteArray()[0] * 100 + value.ToByteArray()[15]
                            : -1;
                }

                private static int ExerciseGuidWrappers()
                {
                    var braces = Guid.Parse("{00112233-4455-6677-8899-aabbccddeeff}"u8);
                    var parens = Guid.Parse("(00112233-4455-6677-8899-aabbccddeeff)"u8);
                    var hex = Guid.Parse("{0x00112233,0x4455,0x6677,{0x88,0x99,0xaa,0xbb,0xcc,0xdd,0xee,0xff}}"u8);
                    return braces == parens && parens == hex &&
                        !Guid.TryParse("not-a-guid"u8, out _)
                            ? 1
                            : -1;
                }

                private static int ParseFrameworkName()
                {
                    var value = new System.Runtime.Versioning.FrameworkName(
                        " NetWasm , Profile = Core , Version = V1.2.3 ");
                    return value.Identifier == "NetWasm" &&
                        value.Version == new Version(1, 2, 3) &&
                        value.Profile == "Core" &&
                        value.FullName == "NetWasm,Version=v1.2.3,Profile=Core"
                            ? value.Identifier.Length * 100 + value.Version.Major * 10 + value.Version.Minor
                            : -1;
                }

                private static int RejectInvalidFrameworkName()
                {
                    try
                    {
                        _ = new System.Runtime.Versioning.FrameworkName(
                            "NetWasm,Version=v1.2,Profile=Core,Extra=No");
                        return -1;
                    }
                    catch (ArgumentException)
                    {
                        return 1;
                    }
                }

                public static int Trace() => 0;
            }
            """,
            [1, 2, 3, 4, 5, 6, 7, 8])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
