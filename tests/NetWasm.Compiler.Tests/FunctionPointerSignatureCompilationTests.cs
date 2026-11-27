// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Calls through function pointers whose signatures name types the stack treats specially: an enum declared in another
// assembly (on the stack it is its underlying integer, whether the signature names it directly or through a type
// argument), and a call made while a marshaller's pinned reference is live in a method with an out parameter.
public sealed class FunctionPointerSignatureCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void AnEnumFromAnotherAssemblyCrossesAFunctionPointerCallAsItsUnderlyingInteger(bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource("FunctionPointerEnum.Library", EnumLibrarySource);
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "FunctionPointerEnum.Application",
            EnumApplicationSource,
            "FunctionPointerEnum.Application.EntryPoint",
            optimize,
            target,
            1,
            ImmutableArray.Create(library))
        {
            AllowUnsafe = true,
        });

        // Green (1) through the generic setter, back through the generic getter and the direct pointer: 1 + 1 + 40.
        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void AFunctionPointerCallUnderAPinnedMarshallerReferenceCompilesInAMethodWithAnOutParameter(bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "PinnedMarshallerCall.Application",
            PinnedMarshallerApplicationSource,
            "PinnedMarshallerCall.Application.EntryPoint",
            optimize,
            target,
            1,
            ImmutableArray<string>.Empty)
        {
            AllowUnsafe = true,
        });

        // "A" is 65; the out parameter stays null; a null string pins a null pointer (-1).
        Assert.Equal(65 * 10 - 1, result);
    }

    private const string EnumLibrarySource = """
        namespace FunctionPointerEnum.Library;

        public enum Color { Red, Green }
        """;

    private const string EnumApplicationSource = """
        using FunctionPointerEnum.Library;

        namespace FunctionPointerEnum.Application;

        public sealed unsafe class Setter<TValue>
        {
            private readonly delegate* managed<TValue, void> _set;
            public Setter(delegate* managed<TValue, void> set) => _set = set;
            public void Invoke(TValue value) => _set(value);
        }

        public sealed unsafe class Getter<TValue>
        {
            private readonly delegate* managed<TValue> _get;
            public Getter(delegate* managed<TValue> get) => _get = get;
            public TValue Invoke() => _get();
        }

        public static unsafe class EntryPoint
        {
            private static Color s_color;

            private static void Store(Color color) => s_color = color;

            private static Color Load() => s_color;

            private static int Weigh(Color color) => (int)color + 40;

            public static int Run(int input)
            {
                new Setter<Color>(&Store).Invoke((Color)input);
                var loaded = new Getter<Color>(&Load).Invoke();
                delegate*<Color, int> weigh = &Weigh;
                return (int)loaded + weigh(loaded);
            }
        }
        """;

    private const string PinnedMarshallerApplicationSource = """
        using System.Runtime.InteropServices.Marshalling;

        namespace PinnedMarshallerCall.Application;

        public static unsafe class EntryPoint
        {
            private static int Native(ushort* text, ushort** result)
            {
                if (result != null)
                {
                    *result = null;
                }
                return text == null ? -1 : *text;
            }

            private static int Call(delegate*<ushort*, ushort**, int> target, string text, out string copied)
            {
                copied = default;
                int read;
                fixed (void* pinned = &Utf16StringMarshaller.GetPinnableReference(text))
                {
                    read = target((ushort*)pinned, null);
                }
                return read;
            }

            public static int Run(int input)
            {
                var first = Call(&Native, input == 1 ? "A" : "B", out var copied);
                var second = Call(&Native, null, out _);
                return first * 10 + second + (copied is null ? 0 : 1000);
            }
        }
        """;
}
