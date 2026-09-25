using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class NumericCompilationTests
{
    [Fact]
    public void CompilerExecutesIntegerBitwiseShiftDivisionAndRemainderOperations()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "NumericOperationsFixture",
            """
            namespace NumericOperationsFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var left = input + 22;
                    var right = input - 38;
                    var signed = (left & 63) | 2;
                    signed ^= 3;
                    signed = (signed << 2) >> 1;
                    var unsigned = (uint)left;
                    var quotient = unsigned / (uint)right;
                    var remainder = unsigned % (uint)right;
                    var complement = ~input;
                    var negated = -input;
                    return signed + (int)quotient + (int)remainder
                        + (complement ^ negated);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NumericOperationsFixture.EntryPoint",
            "Run",
            []));
        var operations = result.Program.Methods.Values
            .SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operation)
            .ToHashSet();

        Assert.Contains(CilOperation.BitwiseAnd, operations);
        Assert.Contains(CilOperation.BitwiseOr, operations);
        Assert.Contains(CilOperation.BitwiseXor, operations);
        Assert.Contains(CilOperation.ShiftLeft, operations);
        Assert.Contains(CilOperation.ShiftRightSigned, operations);
        Assert.Contains(CilOperation.DivideUnsigned, operations);
        Assert.Contains(CilOperation.RemainderUnsigned, operations);
        Assert.Contains(CilOperation.Negate, operations);
        Assert.Contains(CilOperation.OnesComplement, operations);
        Assert.Equal(142, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesInt64SignedDivisionAndRemainder()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "Int64DivisionFixture",
            """
            namespace Int64DivisionFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = (long)input * 4_294_967_296L + 17;
                    var divisor = (long)input + 2;
                    return (int)(value / divisor) + (int)(value % divisor);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "Int64DivisionFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(unchecked((int)(((long)41 * 4_294_967_296L + 17) / 43)) +
                     (int)(((long)41 * 4_294_967_296L + 17) % 43),
            ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerProducesManagedIntegerArithmeticExceptions()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "IntegerExceptionFixture",
            """
            namespace IntegerExceptionFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var result = 0;
                    try
                    {
                        _ = input / (input - input);
                    }
                    catch (System.DivideByZeroException)
                    {
                        result |= 1;
                    }

                    try
                    {
                        _ = int.MinValue / (input - 42);
                    }
                    catch (System.OverflowException)
                    {
                        result |= 2;
                    }

                    try
                    {
                        _ = checked(input + int.MaxValue);
                    }
                    catch (System.OverflowException)
                    {
                        result |= 4;
                    }

                    try
                    {
                        _ = checked(uint.MaxValue + (uint)input);
                    }
                    catch (System.OverflowException)
                    {
                        result |= 8;
                    }

                    try
                    {
                        _ = checked(0U - (uint)input);
                    }
                    catch (System.OverflowException)
                    {
                        result |= 16;
                    }

                    return result;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "IntegerExceptionFixture.EntryPoint",
            "Run",
            []));

        Assert.Contains(ManagedExceptionKind.DivideByZero, result.Program.ImplicitExceptions);
        Assert.Contains(ManagedExceptionKind.Overflow, result.Program.ImplicitExceptions);
        Assert.Equal(31, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesFloatingPointDivisionRemainderAndNegationWithoutExceptions()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "FloatingPointFixture",
            """
            namespace FloatingPointFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var left = (double)input + 0.5;
                    var divided = left / 2.0;
                    var remainder = left % 4.0;
                    var infinity = left / 0.0;
                    var nan = 0.0 / 0.0;
                    var singleNan = 0.0f / 0.0f;
                    return (int)(divided + remainder - -1.0)
                        + (infinity > 0.0 ? 1 : 0)
                        + (nan != nan ? 1 : 0)
                        + (!(nan >= 0.0) ? 2 : 0)
                        + (!(nan <= 0.0) ? 4 : 0)
                        + (!(singleNan >= 0.0f) ? 8 : 0)
                        + (!(singleNan <= 0.0f) ? 16 : 0);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "FloatingPointFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(55, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesNarrowAndCheckedNumericConversions()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "NumericConversionFixture",
            """
            namespace NumericConversionFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var result = (int)(byte)(input + 256);
                    result += (byte)checked(input);
                    result += (ushort)checked(input);
                    result += (int)checked((uint)input);
                    result += (int)checked((long)(uint)input);
                    try
                    {
                        _ = checked((byte)(input + 256));
                    }
                    catch (System.OverflowException)
                    {
                        result += 1;
                    }
                    try
                    {
                        _ = checked((uint)-input);
                    }
                    catch (System.OverflowException)
                    {
                        result += 2;
                    }
                    try
                    {
                        _ = checked((int)(double)int.MaxValue + input);
                    }
                    catch (System.OverflowException)
                    {
                        result += 4;
                    }
                    return result;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NumericConversionFixture.EntryPoint",
            "Run",
            []));

        Assert.Contains(ManagedExceptionKind.Overflow, result.Program.ImplicitExceptions);
        Assert.Equal(212, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CompilerExecutesCompleteSafeCheckedConversionMatrix(
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "CheckedConversionMatrixFixture",
            """
            namespace CheckedConversionMatrixFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var signed = (long)input;
                    var unsigned = (ulong)(uint)input;
                    var result = checked((sbyte)signed) + checked((byte)signed);
                    result += checked((short)signed) + checked((ushort)signed);
                    result += checked((int)signed) + (int)checked((uint)signed);
                    result += (int)checked((long)signed) + (int)checked((ulong)signed);
                    result += checked((sbyte)unsigned) + checked((byte)unsigned);
                    result += checked((short)unsigned) + checked((ushort)unsigned);
                    result += checked((int)unsigned) + (int)checked((uint)unsigned);
                    result += (int)checked((long)unsigned) + (int)checked((ulong)unsigned);
                    result += (int)checked((nint)signed) + (int)checked((nuint)signed);
                    result += (int)checked((nint)unsigned) + (int)checked((nuint)unsigned);
                    result += (int)checked((long)((double)input + 0.75));

                    var single = -(float)input - 0.5f;
                    result += (int)(single % 4.0f);
                    return result;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "CheckedConversionMatrixFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(860, ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            41,
            target,
            result.StaticDataEnd));
    }
}
