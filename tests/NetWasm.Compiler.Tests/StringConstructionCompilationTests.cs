using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class StringConstructionCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesCharacterArraySliceConstruction(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "StringConstructionFixture",
            Source,
            "StringConstructionFixture.EntryPoint",
            optimize,
            target,
            0,
            ImmutableArray<string>.Empty));

        Assert.Equal(31, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesCharacterSpanConstruction(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "StringSpanConstructionFixture",
            SpanSource,
            "StringSpanConstructionFixture.EntryPoint",
            optimize,
            target,
            0,
            ImmutableArray<string>.Empty));

        Assert.Equal(31, observed);
    }

    // Every string constructor, the pointer ones included, runs upstream's Ctor method as .NET
    // does: the same strings, the empty string for no characters, and the same exceptions and
    // parameter names for bad arguments. The checksum is .NET 11's.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void EveryConstructorMatchesDotNet(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "StringConstructors",
            ConstructorsSource,
            "StringConstructors.EntryPoint",
            optimize,
            target,
            41,
            [])
        {
            AllowUnsafe = true,
        });

        Assert.Equal(-1076435302, observed);
    }

    private const string ConstructorsSource = """
        using System;
        using System.Text;

        namespace StringConstructors;

        public static class EntryPoint
        {
            public static unsafe int Run(int input)
            {
                var checksum = 17;
                void Add(string value)
                {
                    checksum = unchecked(checksum * 31 + value.Length);
                    foreach (var character in value) checksum = unchecked(checksum * 31 + character);
                }
                void Error(Action action)
                {
                    try
                    {
                        action();
                        checksum = unchecked(checksum * 31 + 1);
                    }
                    catch (ArgumentException exception)
                    {
                        // The exception's name length, from its type: a program carries no type names (issue 225).
                        checksum = unchecked(checksum * 31 + (exception is ArgumentOutOfRangeException ? 27
                            : exception is ArgumentNullException ? 21 : 17));
                        foreach (var character in exception.ParamName ?? "") checksum = unchecked(checksum * 31 + character);
                    }
                    catch (OverflowException)
                    {
                        checksum = unchecked(checksum * 31 + 7);
                    }
                }

                char[] letters = ['h', 'é', 'l', 'l', 'o', '\0', '!'];
                Add(new string('x', input % 5));
                Add(new string('\0', 3));
                Add(new string(letters));
                Add(new string((char[]?)null));
                Add(new string(letters, 1, 4));
                Add(new string(letters.AsSpan(2, 3)));
                Add(new string(ReadOnlySpan<char>.Empty));
                if (!ReferenceEquals(new string('x', 0), string.Empty) || !ReferenceEquals(new string(Array.Empty<char>()), string.Empty))
                    return -1;
                fixed (char* chars = letters)
                {
                    Add(new string(chars));
                    Add(new string(chars, 2, 4));
                    Add(new string((char*)null));
                    Add(new string((char*)null, 0, 0));
                }
                byte[] utf8 = [0x63, 0x61, 0x66, 0xC3, 0xA9, 0x00, 0x7A];
                fixed (byte* bytes = utf8)
                {
                    var signed = (sbyte*)bytes;
                    Add(new string(signed));
                    Add(new string(signed, 1, 4));
                    Add(new string(signed, 0, 5, Encoding.UTF8));
                    Add(new string(signed, 0, 3, Encoding.ASCII));
                    Add(new string(signed, 0, 5, null!));
                    Add(new string((sbyte*)null));
                    Add(new string((sbyte*)null, 0, 0));
                }
                Error(() => _ = new string('x', -1));
                Error(() => _ = new string('x', int.MaxValue));
                Error(() => _ = new string(letters, -1, 1));
                Error(() => _ = new string(letters, 0, -1));
                Error(() => _ = new string(letters, 5, 3));
                Error(() => _ = new string((char[])null!, 0, 0));
                Error(() =>
                {
                    fixed (char* chars = letters) _ = new string(chars, -1, 1);
                });
                Error(() =>
                {
                    fixed (char* chars = letters) _ = new string(chars, 0, -1);
                });
                Error(() => _ = new string((char*)null, 0, 1));
                Error(() => _ = new string((sbyte*)null, 0, 1));
                Error(() => _ = new string((sbyte*)null, 0, 1, Encoding.UTF8));
                Error(() =>
                {
                    fixed (byte* bytes = utf8) _ = new string((sbyte*)bytes, -1, 1);
                });
                return checksum;
            }
        }
        """;

    private const string Source = """
        using System;

        namespace StringConstructionFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var characters = new[] { 'a', 'b', 'c', 'd' };
                var observed = new string(characters, 1, 2) == "bc" ? 1 : 0;

                try
                {
                    _ = new string(characters, -1, 1);
                }
                catch (ArgumentOutOfRangeException)
                {
                    observed |= 2;
                }

                try
                {
                    _ = new string(characters, 1, -1);
                }
                catch (ArgumentOutOfRangeException)
                {
                    observed |= 4;
                }

                try
                {
                    _ = new string(characters, 3, 2);
                }
                catch (ArgumentOutOfRangeException)
                {
                    observed |= 8;
                }

                try
                {
                    _ = new string((char[])null!, 0, 0);
                }
                catch (ArgumentNullException)
                {
                    observed |= 16;
                }

                return observed + input - input;
            }
        }
        """;

    private const string SpanSource = """
        using System;

        namespace StringSpanConstructionFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var observed = 0;
                var empty = new string(default(ReadOnlySpan<char>));
                if (empty.Length == 0)
                {
                    observed |= 1;
                }

                var source = new[] { 'x', 'a', '\0', '\ud83d', '\ude00', 'z' };
                var text = new string(new ReadOnlySpan<char>(source, 1, 4));
                source[1] = 'q';
                if (text.Length == 4 && text[0] == 'a')
                {
                    observed |= 2;
                }
                if (text[1] == '\0')
                {
                    observed |= 4;
                }
                if (text[2] == '\ud83d' && text[3] == '\ude00')
                {
                    observed |= 8;
                }

                Span<char> stack = stackalloc char[3];
                stack[0] = 'n';
                stack[1] = 'e';
                stack[2] = 't';
                if (new string(stack) == "net")
                {
                    observed |= 16;
                }

                return observed + input - input;
            }
        }
        """;
}
