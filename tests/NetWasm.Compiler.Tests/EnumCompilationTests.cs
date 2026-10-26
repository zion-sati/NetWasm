using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class EnumCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ConvertibleToTypeRetainsEnumNames(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "EnumConvertibleNamesFixture",
            """
            using System;
            [Flags] public enum Value : uint { Alpha = 1, Beta = 2 }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    IConvertible value = (Value)(uint)input;
                    return (string)value.ToType(typeof(string), null) == "Alpha, Beta" ? 0 : 1;
                }
            }
            """, "EntryPoint", optimize, target, 3, [])
        {
            Exports = [new("run", "EntryPoint", "Run")],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform@1.0.0/async-platform",
            Optimization = FinalWasmOptimization.None,
        });

        Assert.Equal(0, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesTypeBasedEnumParsingAcrossTargets(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "EnumTypeBasedParseFixture",
            """
            using System;

            public enum E : int { A = 1 }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if ((E)Enum.Parse(typeof(E), "42") != (E)42) return 1;
                    if (!Enum.TryParse(typeof(E), "-42", out var value)) return 2;
                    return (E)value! == (E)(-42) ? 0 : 3;
                }
            }
            """,
            "EntryPoint",
            optimize,
            target,
            0,
            [])
        {
            Exports = [new("run", "EntryPoint", "Run")],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform@1.0.0/async-platform",
            Optimization = FinalWasmOptimization.None,
        });

        Assert.Equal(0, observed);
    }

    [Theory]
    [InlineData("Enum.Parse<E>(\"A\") == E.A ? 0 : 1")]
    [InlineData("Enum.Parse<E>(\"42\") == (E)42 ? 0 : 2")]
    [InlineData("Enum.TryParse<E>(\"-42\", out var value) && value == (E)(-42) ? 0 : 3")]
    [InlineData("Enum.TryParse<E>(\"-42\", out var value) ? 0 : 10")]
    [InlineData("Enum.Parse<E>(new ReadOnlySpan<char>(new char[] { '4', '2' })) == (E)42 ? 0 : 4")]
    [InlineData("(E)Enum.Parse(typeof(E), \"42\") == (E)42 ? 0 : 5")]
    [InlineData("Enum.TryParse(typeof(E), \"-42\", out var value) && (E)value! == (E)(-42) ? 0 : 6")]
    [InlineData("Enum.Parse<I1>(\" -128 \") == (I1)(-128) && !Enum.TryParse<I1>(\"128\", out _) ? 0 : 11")]
    [InlineData("Enum.Parse<U1>(\"+255\") == (U1)255 && !Enum.TryParse<U1>(\"256\", out _) ? 0 : 12")]
    [InlineData("Enum.Parse<I2>(\"-32768\") == (I2)(-32768) ? 0 : 13")]
    [InlineData("Enum.Parse<U2>(\"65535\") == (U2)65535 ? 0 : 14")]
    [InlineData("Enum.Parse<I4>(\"-2147483648\") == (I4)int.MinValue ? 0 : 15")]
    [InlineData("Enum.Parse<U4>(\"4294967295\") == (U4)uint.MaxValue ? 0 : 16")]
    [InlineData("Enum.Parse<I8>(\"-9223372036854775808\") == (I8)long.MinValue ? 0 : 17")]
    [InlineData("Enum.Parse<U8>(\"18446744073709551615\") == (U8)ulong.MaxValue ? 0 : 18")]
    [InlineData("!Enum.TryParse<E>(null, out _) ? 0 : 8")]
    public void CompilerExecutesEnumParseSemantics(string expression)
    {
        using var assets = TestAssets.Create();
        var source = $$"""
            using System;
            public enum E : int { A = 1 }
            public enum I1 : sbyte { Zero = 0 }
            public enum U1 : byte { Zero = 0 }
            public enum I2 : short { Zero = 0 }
            public enum U2 : ushort { Zero = 0 }
            public enum I4 : int { Zero = 0 }
            public enum U4 : uint { Zero = 0 }
            public enum I8 : long { Zero = 0 }
            public enum U8 : ulong { Zero = 0 }
            public static class EntryPoint
            {
                public static int Run(int input) => {{expression}};
            }
            """;
        var assembly = assets.CompileSource("EnumParseSemanticFixture", source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [new("run", "EntryPoint", "Run")],
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform@1.0.0/async-platform"));
        Assert.Equal(0, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            0, WasmTarget.Wasm32, compilation.StaticDataEnd));
    }

    [Fact]
    public void EnumParsingValidatesNumericSyntaxBeforeReportingOverflow()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("EnumNumericSyntaxFixture", """
            using System;

            public enum U1 : byte { Zero = 0 }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (!ThrowsArgument("256x")) return 1;
                    if (!ThrowsArgument("999x")) return 2;
                    if (!ThrowsArgument("-1x")) return 3;
                    if (!ThrowsArgument("999, A")) return 4;
                    if (Enum.TryParse<U1>("256x", out _)) return 5;
                    if (Enum.TryParse<U1>("999x", out _)) return 6;
                    if (Enum.TryParse<U1>("-1x", out _)) return 7;
                    if (Enum.TryParse<U1>("999, A", out _)) return 8;
                    if (!ThrowsOverflow("256")) return 9;
                    if (Enum.TryParse<U1>("256", out _)) return 10;
                    return 0;
                }

                private static bool ThrowsArgument(string value)
                {
                    try
                    {
                        _ = Enum.Parse<U1>(value);
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }

                private static bool ThrowsOverflow(string value)
                {
                    try
                    {
                        _ = Enum.Parse<U1>(value);
                        return false;
                    }
                    catch (OverflowException)
                    {
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [new("run", "EntryPoint", "Run")],
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform@1.0.0/async-platform"));

        Assert.Equal(0, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            0, WasmTarget.Wasm32, compilation.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesGenericEnumTryFormatAcrossTargets(
        bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new(
            "EnumTryFormatFixture",
            """
            using System;
            [Flags]
            public enum Permission : byte { Read = 1, Write = 2 }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var format = new ReadOnlySpan<char>(new char[] { 'F' });
                    var destination = new char[16];
                    if (!Enum.TryFormat((Permission)3, destination, out var written, format))
                        return 1;
                    if (new string(destination, 0, written) != "Read, Write")
                        return 2;
                    ISpanFormattable boxed = (Permission)3;
                    if (!boxed.TryFormat(destination, out written, format, null) ||
                        new string(destination, 0, written) != "Read, Write")
                        return 4;
                    var small = new char[2];
                    if (Enum.TryFormat((Permission)3, small, out written, format))
                        return 3;
                    return written;
                }
            }
            """,
            "EntryPoint",
            optimize,
            target,
            0,
            [])
        {
            Exports = [new("run", "EntryPoint", "Run")],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform@1.0.0/async-platform",
            Optimization = FinalWasmOptimization.None,
        });

        Assert.Equal(0, observed);
    }

    [Fact]
    public void CompilerMatchesDesktopEnumBehaviorAcrossWidthsAndMetadataContracts()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("EnumBehaviorFixture", Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EnumBehaviorFixture.EntryPoint",
            "Run",
            [new("run", "EnumBehaviorFixture.EntryPoint", "Run")],
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform@1.0.0/async-platform"));
        Assert.Equal(0, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            0, WasmTarget.Wasm32, compilation.StaticDataEnd));
    }

    private const string Source = """
        using System;

        namespace EnumBehaviorFixture;

        public enum SignedByte : sbyte
        {
            Min = -1,
            Zero = 0,
            One = 1,
        }

        public enum UnsignedByte : byte
        {
            Max = byte.MaxValue,
            Zero = 0,
            One = 1,
        }

        public enum SignedShort : short
        {
            Min = short.MinValue,
            One = 1,
            Max = short.MaxValue,
        }

        public enum UnsignedShort : ushort
        {
            Zero = 0,
            One = 1,
            Max = ushort.MaxValue,
        }

        public enum SignedInt : int
        {
            Min = int.MinValue,
            One = 1,
            Max = int.MaxValue,
        }

        public enum UnsignedInt : uint
        {
            Zero = 0,
            One = 1,
            Max = uint.MaxValue,
        }

        public enum SignedLong : long
        {
            Min = long.MinValue,
            One = 1,
            Max = long.MaxValue,
        }

        public enum UnsignedLong : ulong
        {
            Max = ulong.MaxValue,
            Zero = 0,
            One = 1,
        }

        public enum Aliases : int
        {
            None = 0,
            One = 1,
            AliasOne = 1,
            Two = 2,
        }

        [Flags]
        public enum Permission : byte
        {
            None = 0,
            Read = 1,
            Write = 2,
            Execute = 4,
            All = Read | Write | Execute,
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                // Each underlying width and signedness has a distinct TypeCode.
                if (SignedByte.One.GetTypeCode() != TypeCode.SByte) return 1;
                if (UnsignedByte.One.GetTypeCode() != TypeCode.Byte) return 2;
                if (SignedShort.One.GetTypeCode() != TypeCode.Int16) return 3;
                if (UnsignedShort.One.GetTypeCode() != TypeCode.UInt16) return 4;
                if (SignedInt.One.GetTypeCode() != TypeCode.Int32) return 5;
                if (UnsignedInt.One.GetTypeCode() != TypeCode.UInt32) return 6;
                if (SignedLong.One.GetTypeCode() != TypeCode.Int64) return 7;
                if (UnsignedLong.One.GetTypeCode() != TypeCode.UInt64) return 8;

                // Generic and Type-based names/values retain aliases and value order.
                var aliasNames = Enum.GetNames<Aliases>();
                if (aliasNames.Length != 4 || aliasNames[0] != "None" ||
                    aliasNames[3] != "Two") return 10;
                if (aliasNames[1] != "One" || aliasNames[2] != "AliasOne") return 11;
                if (Enum.GetNames(typeof(Aliases))[1] != "One") return 12;
                if (Enum.GetName<Aliases>(Aliases.AliasOne) != "One") return 13;
                if (Enum.GetName(typeof(Aliases), Aliases.AliasOne) != "One") return 14;
                if (Enum.GetName<Aliases>((Aliases)99) != null) return 15;
                if (Enum.GetValues<Aliases>().Length != 4) return 16;
                if (Enum.GetValues(typeof(Aliases)).Length != 4) return 17;
                var aliasUnderlying = (int[])Enum.GetValuesAsUnderlyingType(typeof(Aliases));
                if (aliasUnderlying.Length != 4 || aliasUnderlying[0] != 0 ||
                    aliasUnderlying[1] != 1 || aliasUnderlying[2] != 1 ||
                    aliasUnderlying[3] != 2) return 18;
                var aliasUnderlyingGeneric = (int[])Enum.GetValuesAsUnderlyingType<Aliases>();
                if (aliasUnderlyingGeneric.Length != 4 || aliasUnderlyingGeneric[0] != 0 ||
                    aliasUnderlyingGeneric[1] != 1 || aliasUnderlyingGeneric[2] != 1 ||
                    aliasUnderlyingGeneric[3] != 2) return 19;
                var signedNames = Enum.GetNames<SignedByte>();
                if (signedNames.Length != 3 || signedNames[0] != "Zero" ||
                    signedNames[1] != "One" || signedNames[2] != "Min") return 110;
                if (Enum.GetNames(typeof(SignedByte))[2] != "Min") return 111;
                var signedUnderlying = (sbyte[])Enum.GetValuesAsUnderlyingType<SignedByte>();
                if (signedUnderlying.Length != 3 || signedUnderlying[0] != 0 ||
                    signedUnderlying[1] != 1 || signedUnderlying[2] != -1) return 28;
                var unsignedNames = Enum.GetNames<UnsignedByte>();
                if (unsignedNames.Length != 3 || unsignedNames[0] != "Zero" ||
                    unsignedNames[1] != "One" || unsignedNames[2] != "Max") return 112;
                var unsignedTypeNames = Enum.GetNames(typeof(UnsignedByte));
                if (unsignedTypeNames.Length != 3 || unsignedTypeNames[0] != "Zero" ||
                    unsignedTypeNames[1] != "One" || unsignedTypeNames[2] != "Max") return 114;
                var unsignedByteUnderlying = (byte[])Enum.GetValuesAsUnderlyingType<UnsignedByte>();
                if (unsignedByteUnderlying.Length != 3 || unsignedByteUnderlying[0] != 0 ||
                    unsignedByteUnderlying[1] != 1 || unsignedByteUnderlying[2] != byte.MaxValue)
                    return 113;
                var unsignedByteTypeUnderlying =
                    (byte[])Enum.GetValuesAsUnderlyingType(typeof(UnsignedByte));
                if (unsignedByteTypeUnderlying.Length != 3 || unsignedByteTypeUnderlying[0] != 0 ||
                    unsignedByteTypeUnderlying[1] != 1 ||
                    unsignedByteTypeUnderlying[2] != byte.MaxValue) return 115;
                var unsignedUnderlying = (ulong[])Enum.GetValuesAsUnderlyingType<UnsignedLong>();
                if (unsignedUnderlying.Length != 3 || unsignedUnderlying[0] != 0UL ||
                    unsignedUnderlying[1] != 1UL ||
                    unsignedUnderlying[2] != ulong.MaxValue) return 29;
                var unsignedLongNames = Enum.GetNames<UnsignedLong>();
                if (unsignedLongNames.Length != 3 || unsignedLongNames[0] != "Zero" ||
                    unsignedLongNames[1] != "One" || unsignedLongNames[2] != "Max") return 116;

                // Underlying metadata is available through the generic type-code path and
                // through the Type overload for every representative width.
                if (Enum.GetUnderlyingType(typeof(SignedByte)) != typeof(sbyte)) return 20;
                if (Enum.GetUnderlyingType(typeof(UnsignedByte)) != typeof(byte)) return 21;
                if (Enum.GetUnderlyingType(typeof(SignedShort)) != typeof(short)) return 22;
                if (Enum.GetUnderlyingType(typeof(UnsignedShort)) != typeof(ushort)) return 23;
                if (Enum.GetUnderlyingType(typeof(SignedInt)) != typeof(int)) return 24;
                if (Enum.GetUnderlyingType(typeof(UnsignedInt)) != typeof(uint)) return 25;
                if (Enum.GetUnderlyingType(typeof(SignedLong)) != typeof(long)) return 26;
                if (Enum.GetUnderlyingType(typeof(UnsignedLong)) != typeof(ulong)) return 27;

                // Flags formatting and composition match desktop Enum formatting.
                var combined = Permission.Read | Permission.Write;
                if (!combined.HasFlag(Permission.Read) || combined.HasFlag(Permission.Execute))
                    return 30;
                var combinedText = combined.ToString();
                if (combinedText != "Read, Write")
                {
                    if (combinedText == null) return 131;
                    if (combinedText == "Read") return 132;
                    if (combinedText == "Write") return 133;
                    if (combinedText == "All") return 134;
                    return 135;
                }
                if (combined.ToString("F") != "Read, Write") return 32;
                if (combined.ToString("D") != "3") return 33;
                if (combined.ToString("X") != "03") return 34;
                if (Enum.Format(typeof(Permission), combined, "F") != "Read, Write") return 35;
                if (Enum.Format(typeof(Permission), combined, "D") != "3") return 36;
                if (Enum.Format(typeof(Permission), combined, "X") != "03") return 37;
                if (Permission.All.ToString("F") != "All") return 38;

                // Unnamed values use the underlying signedness for general, decimal,
                // and flags formats, while hexadecimal remains fixed-width.
                var unnamedSigned = (SignedInt)42;
                if (unnamedSigned.ToString() != "42") return 138;
                if (unnamedSigned.ToString("D") != "42") return 143;
                if (unnamedSigned.ToString("F") != "42") return 144;
                if (unnamedSigned.ToString("X") != "0000002A") return 145;
                var unnamedNegative = (SignedInt)(-42);
                if (unnamedNegative.ToString() != "-42" || unnamedNegative.ToString("D") != "-42" ||
                    unnamedNegative.ToString("F") != "-42" || unnamedNegative.ToString("X") != "FFFFFFD6")
                    return 139;
                var unnamedUnsigned = (UnsignedInt)42;
                if (unnamedUnsigned.ToString() != "42" || unnamedUnsigned.ToString("D") != "42" ||
                    unnamedUnsigned.ToString("F") != "42" || unnamedUnsigned.ToString("X") != "0000002A")
                    return 140;
                var unnamedUnsignedLong = (UnsignedLong)(ulong.MaxValue - 1);
                if (unnamedUnsignedLong.ToString() != "18446744073709551614" ||
                    unnamedUnsignedLong.ToString("D") != "18446744073709551614" ||
                    unnamedUnsignedLong.ToString("F") != "18446744073709551614" ||
                    unnamedUnsignedLong.ToString("X") != "FFFFFFFFFFFFFFFE")
                    return 141;
                var unnamedFlags = (Permission)8;
                if (unnamedFlags.ToString() != "8" || unnamedFlags.ToString("F") != "8" ||
                    Enum.Format(typeof(Permission), unnamedFlags, "F") != "8" ||
                    unnamedFlags.ToString("X") != "08")
                    return 142;

                // Parse/TryParse accept names, numeric values, whitespace, and flags with
                // the desktop case-sensitivity and exception rules.
                if (Enum.Parse<Aliases>("AliasOne") != Aliases.One) return 40;
                if (Enum.Parse<Permission>("  Read, Write  ") != combined) return 41;
                if (Enum.Parse<Permission>("3") != combined) return 42;
                if (Enum.Parse<Permission>("read, write", true) != combined) return 43;
                if (!Enum.TryParse<Permission>("read, write", true, out var parsed) ||
                    parsed != combined) return 44;
                if (Enum.TryParse<Permission>("read, write", out _)) return 45;
                if (!Enum.TryParse<UnsignedLong>("18446744073709551615", out var max) ||
                    max != UnsignedLong.Max) return 46;
                if (Enum.TryParse<Permission>("not-a-permission", out _)) return 47;

                try
                {
                    _ = Enum.Parse<Permission>("read, write");
                    return 48;
                }
                catch (ArgumentException)
                {
                }
                try
                {
                    _ = Enum.Parse<UnsignedByte>("256");
                    return 49;
                }
                catch (OverflowException)
                {
                }
                if (Enum.TryParse<UnsignedByte>("256", out _)) return 50;

                // IsDefined distinguishes aliases and named flag combinations from
                // arbitrary combinations.
                if (!Enum.IsDefined<Aliases>(Aliases.AliasOne)) return 52;
                if (!Enum.IsDefined(typeof(Aliases), Aliases.AliasOne)) return 53;
                if (!Enum.IsDefined<Permission>(Permission.All)) return 54;
                if (Enum.IsDefined<Permission>(combined)) return 55;
                if (Enum.IsDefined<Aliases>((Aliases)99)) return 56;
                // Every numeric ToObject overload preserves the enum's underlying type.
                if (!Enum.ToObject(typeof(SignedByte), (sbyte)-1).Equals(SignedByte.Min)) return 60;
                if (!Enum.ToObject(typeof(UnsignedByte), (byte)255).Equals(UnsignedByte.Max)) return 61;
                if (!Enum.ToObject(typeof(SignedShort), (short)-32768).Equals(SignedShort.Min)) return 62;
                if (!Enum.ToObject(typeof(UnsignedShort), (ushort)65535).Equals(UnsignedShort.Max)) return 63;
                if (!Enum.ToObject(typeof(SignedInt), int.MinValue).Equals(SignedInt.Min)) return 64;
                if (!Enum.ToObject(typeof(UnsignedInt), uint.MaxValue).Equals(UnsignedInt.Max)) return 65;
                if (!Enum.ToObject(typeof(SignedLong), long.MinValue).Equals(SignedLong.Min)) return 66;
                if (!Enum.ToObject(typeof(UnsignedLong), ulong.MaxValue).Equals(UnsignedLong.Max)) return 67;
                try
                {
                    _ = Enum.ToObject(typeof(Aliases), "not a number");
                    return 68;
                }
                catch (ArgumentException)
                {
                }
                try
                {
                    _ = Enum.ToObject(typeof(Aliases), null!);
                    return 69;
                }
                catch (ArgumentNullException)
                {
                }
                // Equality, hash consistency, ordering, and invalid comparisons.
                if (!Aliases.One.Equals(Aliases.AliasOne)) return 72;
                if (Aliases.One.Equals(Permission.Read)) return 73;
                if (Aliases.One.GetHashCode() != Aliases.AliasOne.GetHashCode()) return 74;
                if (Aliases.One.CompareTo(Aliases.AliasOne) != 0) return 75;
                if (Aliases.One.CompareTo(Aliases.Two) >= 0) return 76;
                if (Aliases.Two.CompareTo(Aliases.One) <= 0) return 77;
                if (Aliases.One.CompareTo(null) != 1) return 78;
                try
                {
                    _ = Aliases.One.CompareTo(Permission.Read);
                    return 79;
                }
                catch (ArgumentException)
                {
                }
                // IConvertible uses the enum's underlying numeric value and rejects
                // conversions that have no meaningful enum representation.
                IConvertible convertible = Aliases.One;
                if (convertible.GetTypeCode() != TypeCode.Int32) return 82;
                if (!convertible.ToBoolean(null)) return 83;
                if (convertible.ToChar(null) != (char)1) return 84;
                if (convertible.ToSByte(null) != 1) return 85;
                if (convertible.ToByte(null) != 1) return 86;
                if (convertible.ToInt16(null) != 1) return 87;
                if (convertible.ToUInt16(null) != 1) return 88;
                if (convertible.ToInt32(null) != 1) return 89;
                if (convertible.ToUInt32(null) != 1) return 90;
                if (convertible.ToInt64(null) != 1) return 91;
                if (convertible.ToUInt64(null) != 1) return 92;
                if (convertible.ToSingle(null) != 1) return 93;
                if (convertible.ToDouble(null) != 1) return 94;
                var decimalOneBits = decimal.GetBits(convertible.ToDecimal(null));
                if (decimalOneBits[0] != 1) return 105;
                if (decimalOneBits[1] != 0) return 106;
                if (decimalOneBits[2] != 0) return 107;
                if (decimalOneBits[3] != 0) return 108;
                var decimalUnlistedAliasBits = decimal.GetBits(
                    ((IConvertible)(Aliases)12).ToDecimal(null));
                if (decimalUnlistedAliasBits[0] != 12 || decimalUnlistedAliasBits[1] != 0 ||
                    decimalUnlistedAliasBits[2] != 0 || decimalUnlistedAliasBits[3] != 0) return 109;
                var decimalIntMinBits = decimal.GetBits(((IConvertible)SignedInt.Min).ToDecimal(null));
                if (decimalIntMinBits[0] != int.MinValue || decimalIntMinBits[1] != 0 ||
                    decimalIntMinBits[2] != 0 || decimalIntMinBits[3] != int.MinValue) return 99;
                var decimalLongMinBits = decimal.GetBits(((IConvertible)SignedLong.Min).ToDecimal(null));
                if (decimalLongMinBits[0] != 0 || decimalLongMinBits[1] != int.MinValue ||
                    decimalLongMinBits[2] != 0 || decimalLongMinBits[3] != int.MinValue) return 100;
                var decimalUnsignedLongBits = decimal.GetBits(((IConvertible)UnsignedLong.Max).ToDecimal(null));
                if (decimalUnsignedLongBits[0] != -1 || decimalUnsignedLongBits[1] != -1 ||
                    decimalUnsignedLongBits[2] != 0 || decimalUnsignedLongBits[3] != 0) return 101;
                if (convertible.ToString(null) != "One") return 96;
                if ((string)convertible.ToType(typeof(string), null) != "One") return 97;
                if ((int)convertible.ToType(typeof(int), null) != 1) return 102;
                try
                {
                    _ = convertible.ToType(null!, null);
                    return 103;
                }
                catch (ArgumentNullException)
                {
                }
                try
                {
                    _ = convertible.ToDateTime(null);
                    return 98;
                }
                catch (InvalidCastException)
                {
                }

                return input - input;
            }
        }
        """;
}
