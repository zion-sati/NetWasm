using NetWasm.TestInfrastructure;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class DecimalCompilationTests
{
    [Fact]
    public void CompilerMatchesDesktopDecimalBoundaryCorpusExactly()
    {
        using var assets = TestAssets.Create();
        var cases = new List<(string Expression, Func<decimal> Evaluate)>
        {
            ("decimal.MaxValue / 10m", () => decimal.MaxValue / 10m),
            ("decimal.MaxValue % 10m", () => decimal.MaxValue % 10m),
            ("decimal.MaxValue * 0.1m", () => decimal.MaxValue * 0.1m),
            ("decimal.MinValue / -7m", () => decimal.MinValue / -7m),
            ("1m / 3m", () => 1m / 3m),
            ("2m / 3m", () => 2m / 3m),
            ("0.0000000000000000000000000001m / 2m",
                () => 0.0000000000000000000000000001m / 2m),
            ("-12345678901234567890.123456789m % 97.0001m",
                () => -12345678901234567890.123456789m % 97.0001m),
            ("decimal.Round(9999999999999999999999999999.5m, 0)",
                () => decimal.Round(9999999999999999999999999999.5m, 0)),
            ("decimal.Round(-2.5000000000000000000000000001m, 0, " +
             "MidpointRounding.ToEven)",
                () => decimal.Round(-2.5000000000000000000000000001m, 0,
                    MidpointRounding.ToEven)),
            ("Identity(decimal.MaxValue) + 1m", () => DecimalIdentity(decimal.MaxValue) + 1m),
            ("Identity(decimal.MaxValue) * 2m", () => DecimalIdentity(decimal.MaxValue) * 2m),
            ("1m / Identity(decimal.Zero)", () => 1m / DecimalIdentity(decimal.Zero)),
        };
        var random = new Random(0x4e657457);
        for (var index = 0; index < 12; index++)
        {
            var left = new decimal(
                unchecked((int)random.NextInt64()),
                unchecked((int)random.NextInt64()),
                unchecked((int)random.NextInt64()),
                random.Next(2) != 0,
                (byte)random.Next(29));
            var right = new decimal(
                unchecked((int)random.NextInt64()),
                unchecked((int)random.NextInt64()),
                unchecked((int)random.NextInt64()),
                random.Next(2) != 0,
                (byte)random.Next(29));
            var leftExpression = DecimalExpression(left);
            var rightExpression = DecimalExpression(right);
            cases.Add(($"{leftExpression} + {rightExpression}", () => left + right));
            cases.Add(($"{leftExpression} - {rightExpression}", () => left - right));
            cases.Add(($"{leftExpression} * {rightExpression}", () => left * right));
            cases.Add(($"{leftExpression} / {rightExpression}", () => left / right));
            cases.Add(($"{leftExpression} % {rightExpression}", () => left % right));
        }
        var statements = new System.Text.StringBuilder();
        for (var index = 0; index < cases.Count; index++)
        {
            try
            {
                var expected = cases[index].Evaluate();
                statements.Append("try { if (!SameBits(")
                    .Append(cases[index].Expression)
                    .Append(", ")
                    .Append(DecimalExpression(expected))
                    .Append(")) return ")
                    .Append(index + 1)
                    .Append("; } catch (OverflowException) { return ")
                    .Append(1000 + index + 1)
                    .Append("; } catch (DivideByZeroException) { return ")
                    .Append(2000 + index + 1)
                    .AppendLine("; }");
            }
            catch (OverflowException)
            {
                statements.Append("try { _ = ")
                    .Append(cases[index].Expression)
                    .Append("; return ")
                    .Append(index + 1)
                    .AppendLine("; } catch (OverflowException) { }");
            }
            catch (DivideByZeroException)
            {
                statements.Append("try { _ = ")
                    .Append(cases[index].Expression)
                    .Append("; return ")
                    .Append(index + 1)
                    .AppendLine("; } catch (DivideByZeroException) { }");
            }
        }
        var source = $$"""
            using System;

            namespace DecimalBoundaryFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    {{statements}}
                    return input - input;
                }

                private static bool SameBits(decimal left, decimal right)
                {
                    var leftBits = decimal.GetBits(left);
                    var rightBits = decimal.GetBits(right);
                    return leftBits[0] == rightBits[0] &&
                        leftBits[1] == rightBits[1] &&
                        leftBits[2] == rightBits[2] &&
                        leftBits[3] == rightBits[3];
                }

                private static decimal Identity(decimal value) => value;
            }
            """;
        var assembly = assets.CompileSource("DecimalBoundaryFixture", source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DecimalBoundaryFixture.EntryPoint",
            "Run",
            []));

        var actual = ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41);
        var failedCase = actual >= 2000
            ? actual - 2000
            : actual >= 1000 ? actual - 1000 : actual;
        Assert.InRange(failedCase, 0, cases.Count);
        Assert.True(
            actual == 0,
            failedCase == 0
                ? ""
                : $"case {failedCase}: {cases[failedCase - 1].Expression}; " +
                  $"expected={cases[failedCase - 1].Evaluate()}; result={actual}");
    }

    [Fact]
    public void CompilerExecutesDecimalLiteralsArithmeticComparisonAndConversions()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DecimalArithmeticFixture",
            """
            namespace DecimalArithmeticFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var left = 12.5m;
                    var right = 2.25m;
                    var result = 0;
                    if (left + right == 14.75m) result |= 1;
                    if (left - right == 10.25m) result |= 2;
                    if (left * right == 28.125m) result |= 4;
                    if (left / right > 5.55m && left / right < 5.56m) result |= 8;
                    if (left % right == 1.25m) result |= 16;
                    if ((int)decimal.Truncate(left) == 12) result |= 32;
                    if (decimal.Round(2.5m) == 2m && decimal.Round(3.5m) == 4m)
                        result |= 64;
                    return result + input - input;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DecimalArithmeticFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(127, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerProducesManagedDecimalExceptions()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DecimalExceptionFixture",
            """
            namespace DecimalExceptionFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var result = 0;
                    try { _ = decimal.MaxValue + 1m; }
                    catch (System.OverflowException) { result |= 1; }
                    try { _ = 1m / (input - input); }
                    catch (System.DivideByZeroException) { result |= 2; }
                    try { _ = (int)decimal.MaxValue; }
                    catch (System.OverflowException) { result |= 4; }
                    return result;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DecimalExceptionFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(7, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerMatchesSupportedDecimalBehaviorMatrix()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DecimalBehaviorFixture",
            """
            namespace DecimalBehaviorFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var result = 0;
                    var one = new decimal(10, 0, 0, false, 1);
                    if (one == 1m && one.GetHashCode() == 1m.GetHashCode()) result |= 1;
                    var negativeZero = new decimal(0, 0, 0, true, 4);
                    if (negativeZero == 0m && negativeZero.GetHashCode() == 0) result |= 2;
                    if (decimal.Round(2.5m, System.MidpointRounding.ToEven) == 2m &&
                        decimal.Round(2.5m, System.MidpointRounding.AwayFromZero) == 3m &&
                        decimal.Round(-2.1m, System.MidpointRounding.ToNegativeInfinity) == -3m &&
                        decimal.Round(-2.9m, System.MidpointRounding.ToPositiveInfinity) == -2m)
                        result |= 4;
                    if (decimal.Floor(-1.1m) == -2m && decimal.Ceiling(-1.1m) == -1m &&
                        decimal.Truncate(-1.9m) == -1m) result |= 8;
                    if (decimal.TryParse("-123.4500", out var parsed) &&
                        parsed == -123.45m && parsed.ToString() == "-123.4500") result |= 16;
                    if (decimal.Parse("  +1.25e2  ") == 125m &&
                        !decimal.TryParse("1.2.3", out _)) result |= 32;
                    if ((decimal)18446744073709551615UL == 18446744073709551615m &&
                        (ulong)18446744073709551615m == 18446744073709551615UL &&
                        (long)-9223372036854775808m == long.MinValue) result |= 64;
                    if ((decimal)0.5 == 0.5m && (double)12.25m == 12.25) result |= 128;
                    if (decimal.Abs(-3m) == 3m && decimal.Sign(-3m) == -1 &&
                        decimal.Min(2m, 3m) == 2m && decimal.Max(2m, 3m) == 3m &&
                        decimal.Clamp(5m, 1m, 4m) == 4m) result |= 256;
                    try { _ = decimal.Parse("not-a-number"); }
                    catch (System.FormatException) { result |= 512; }
                    try { _ = decimal.Clamp(1m, 2m, 0m); }
                    catch (System.ArgumentException) { result |= 1024; }
                    var bits = decimal.GetBits(new decimal(1, 2, 3, true, 4));
                    if (bits.Length == 4 && bits[0] == 1 && bits[1] == 2 && bits[2] == 3 &&
                        bits[3] == unchecked((int)0x80040000)) result |= 2048;
                    try { _ = decimal.Parse("1e1000"); }
                    catch (System.OverflowException) { result |= 4096; }
                    System.IComparable comparable = 2m;
                    System.IComparable<decimal> genericComparable = 2m;
                    if (comparable.CompareTo(null) == 1 && comparable.CompareTo(3m) < 0 &&
                        genericComparable.CompareTo(1m) > 0) result |= 8192;
                    try { _ = comparable.CompareTo("not-decimal"); }
                    catch (System.ArgumentException) { result |= 16384; }
                    return result + input - input;
                }
            }
            """);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DecimalBehaviorFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(32767, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Fact]
    public void CompilerMatchesDesktopDecimalSeedCorpus()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DecimalCorpusFixture",
            """
            namespace DecimalCorpusFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var hash = input;
                    for (var offset = 1; offset <= 128; offset++)
                    {
                        var seed = input + offset;
                        var negative = (seed & 1) != 0;
                        var left = new decimal(seed * 7919 + 17, 0, 0, negative, (byte)(seed % 5));
                        var right = new decimal(seed * 97 + 3, 0, 0, false, (byte)(seed % 3));
                        hash = (hash * 31) ^ (left + right).GetHashCode();
                        hash = (hash * 31) ^ (left - right).GetHashCode();
                        hash = (hash * 31) ^ (left * right).GetHashCode();
                        hash = (hash * 31) ^ (left / right).GetHashCode();
                        hash = (hash * 31) ^ (left % right).GetHashCode();
                        hash = (hash * 31) ^ decimal.Round(left / right, 4).GetHashCode();
                    }
                    return hash;
                }
            }
            """);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DecimalCorpusFixture.EntryPoint",
            "Run",
            []));

        foreach (var seed in new[] { 1, 29, 101 })
        {
            Assert.Equal(DesktopCorpus(seed), ExecuteWithNode(
                compilation.ApplicationModule,
                assets.Directory,
                seed));
        }
    }

    [Fact]
    public void CompilerExecutesDecimalInManagedLanguageShapes()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DecimalManagedShapesFixture",
            """
            namespace DecimalManagedShapesFixture;

            public delegate decimal Transform(decimal value);

            public sealed class Holder<T>
            {
                public T Value = default!;
            }

            public static class EntryPoint
            {
                private static decimal Increment(decimal value) => value + 1m;

                public static int Run(int input)
                {
                    var holder = new Holder<decimal> { Value = 12.5m };
                    var values = new decimal[] { holder.Value, 2.5m };
                    Transform transform = Increment;
                    object boxed = transform(values[0] + values[1]);
                    var value = boxed is decimal parsed ? parsed : 0m;
                    var result = value == 16m ? 1 : 0;
                    try
                    {
                        _ = value / (input - input);
                    }
                    catch (System.DivideByZeroException)
                    {
                        result |= 2;
                    }
                    for (var index = 0; index < 64; index++)
                    {
                        _ = new decimal[] { value, index };
                    }
                    return result;
                }
            }
            """);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DecimalManagedShapesFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(3, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    private static int DesktopCorpus(int input)
    {
        var hash = input;
        for (var offset = 1; offset <= 128; offset++)
        {
            var seed = input + offset;
            var negative = (seed & 1) != 0;
            var left = new decimal(seed * 7919 + 17, 0, 0, negative, (byte)(seed % 5));
            var right = new decimal(seed * 97 + 3, 0, 0, false, (byte)(seed % 3));
            hash = (hash * 31) ^ (left + right).GetHashCode();
            hash = (hash * 31) ^ (left - right).GetHashCode();
            hash = (hash * 31) ^ (left * right).GetHashCode();
            hash = (hash * 31) ^ (left / right).GetHashCode();
            hash = (hash * 31) ^ (left % right).GetHashCode();
            hash = (hash * 31) ^ decimal.Round(left / right, 4).GetHashCode();
        }
        return hash;
    }

    private static string DecimalExpression(decimal value)
    {
        var bits = decimal.GetBits(value);
        var flags = (uint)bits[3];
        var negative = (flags & 0x80000000U) != 0 ? "true" : "false";
        var scale = (byte)(flags >> 16);
        return $"new decimal({bits[0]}, {bits[1]}, {bits[2]}, {negative}, {scale})";
    }

    private static decimal DecimalIdentity(decimal value) => value;

    [Fact]
    public void CompilerTrimsUnusedDecimalImplementationAndSupportsBothTargets()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DecimalTrimmingFixture",
            """
            namespace DecimalTrimmingFixture;
            public static class EntryPoint
            {
                public static int Run(int input) => input + 1;
            }
            """);

        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var compilation = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "DecimalTrimmingFixture.EntryPoint",
                "Run",
                [],
                target));

            Assert.DoesNotContain(compilation.Program.MethodInstances.Values,
                method => method.CanonicalName.Contains(
                    "System.Decimal", StringComparison.Ordinal));
            Assert.True(compilation.ApplicationModule.AsSpan().IndexOf("System.Decimal"u8) < 0);
        }
    }
}
