using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class MixedNativeArithmeticCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CheckedNativeArithmeticPreservesValuesAndOverflow(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var observed = executor.Execute(new CompilationScenario(
            "CheckedNativeArithmetic", ArithmeticSource,
            "CheckedNativeArithmetic.Program", optimize, target, 7, []));

        Assert.Equal(0, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void VariableLengthStackBuffersPreserveIndependentContents(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var observed = executor.Execute(new CompilationScenario(
            "VariableStackBuffers", BufferSource,
            "VariableStackBuffers.Program", optimize, target, 17, []));

        Assert.Equal(0, observed);
    }

    private const string ArithmeticSource = """
        namespace CheckedNativeArithmetic;

        public static class Program
        {
            public static int Run(int input)
            {
                var native = (nint)input;
                var narrow = input - 10;
                if (checked(native + narrow) != 4 || checked(narrow + native) != 4 ||
                    checked(native - narrow) != 10 || checked(narrow - native) != -10 ||
                    checked(native * narrow) != -21 || checked(narrow * native) != -21)
                    return 1;

                var unsignedNative = (nuint)(uint)input;
                var unsignedNarrow = (uint)(input - 4);
                if (checked(unsignedNative + unsignedNarrow) != 10 ||
                    checked(unsignedNarrow + unsignedNative) != 10 ||
                    checked(unsignedNative - unsignedNarrow) != 4 ||
                    checked(unsignedNative * unsignedNarrow) != 21 ||
                    checked(unsignedNarrow * unsignedNative) != 21)
                    return 2;

                if (native / narrow != -2 || narrow / native != 0 ||
                    native % narrow != 1 || narrow % native != -3 ||
                    unsignedNative / unsignedNarrow != 2 ||
                    unsignedNarrow / unsignedNative != 0 ||
                    unsignedNative % unsignedNarrow != 1 ||
                    unsignedNarrow % unsignedNative != 3)
                    return 3;

                var caught = 0;
                try { _ = checked(nint.MaxValue + input); }
                catch (System.OverflowException) { caught++; }
                try { _ = checked(nint.MinValue - input); }
                catch (System.OverflowException) { caught++; }
                try { _ = checked(nint.MaxValue * input); }
                catch (System.OverflowException) { caught++; }
                try { _ = checked(nuint.MaxValue + (uint)input); }
                catch (System.OverflowException) { caught++; }
                try { _ = checked(unsignedNarrow - unsignedNative); }
                catch (System.OverflowException) { caught++; }
                try { _ = checked(nuint.MaxValue * (uint)input); }
                catch (System.OverflowException) { caught++; }
                try { _ = native / (input - input); }
                catch (System.DivideByZeroException) { caught++; }
                try { _ = native % (input - input); }
                catch (System.DivideByZeroException) { caught++; }
                return caught == 8 ? 0 : 4;
            }
        }
        """;

    private const string BufferSource = """
        namespace VariableStackBuffers;

        public static class Program
        {
            public static int Run(int input)
            {
                for (var length = 1; length <= input; length++)
                {
                    var status = Verify(length);
                    if (status != 0) return status;
                }
                return 0;
            }

            private static int Verify(int length)
            {
                System.Span<char> characters = stackalloc char[length];
                System.Span<long> numbers = stackalloc long[length];
                for (var index = 0; index < length; index++)
                {
                    characters[index] = (char)('A' + index);
                    if (characters[index] != (char)('A' + index)) return 4;
                }
                for (var index = 0; index < length; index++)
                    if (characters[index] != (char)('A' + index)) return 3;
                for (var index = 0; index < length; index++)
                    numbers[index] = 1000L + index;
                for (var index = 0; index < length; index++)
                {
                    if (characters[index] != (char)('A' + index)) return 1;
                    if (numbers[index] != 1000L + index) return 2;
                }
                return 0;
            }
        }
        """;
}
