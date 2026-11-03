using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ClosedGenericBranchCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CharacterSpansDoNotRetainNonCharacterFormatting(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            namespace CharacterSpanFixture;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Span<char> text = stackalloc char[2];
                    text[0] = '4';
                    text[1] = '2';
                    ReadOnlySpan<char> readOnly = text;
                    return text.ToString() == "42" && readOnly.ToString() == "42" ? 42 : -1;
                }
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedSource("CharacterSpanFixture", source)
            : assets.CompileSource("CharacterSpanFixture", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "CharacterSpanFixture.EntryPoint", "Run", [], Target: target));
        var methods = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values);

        // Only the branch for other element types formats, through an interpolated string.
        Assert.DoesNotContain(methods, body =>
            body.Method.DeclaringType.FullName == "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler");
        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void PrunesClosedBranchesWithoutRemovingObservableEffects(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Runtime.CompilerServices;
            namespace ClosedBranchFixture;
            public sealed class Box<T> { }
            public static class State { public static int Value; }
            public static class Pure
            {
                public static bool Same(object left, object right) => left == right;
                public static bool Forward(object left, object right) => Same(left, right);
            }
            public static class Initialized
            {
                static Initialized() { State.Value = 29; }
                public static bool Same(object left, object right) => left == right;
            }
            public static class NotInlined
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                public static bool Same(object left, object right) => left == right;
            }
            public static class EntryPoint
            {
                public static int OnlyDead() => -100;
                public static int Pick<T>() => typeof(T) == typeof(int) ? 11 : OnlyDead();
                public static int Reject<T>() => typeof(T) != typeof(string) ? 13 : OnlyDead();
                public static int Nested<T>() => typeof(Box<T>) == typeof(Box<int>) ? 19 : OnlyDead();
                public static int Rank<T>() => typeof(T[,]) != typeof(T[]) ? 23 : OnlyDead();
                public static int Forward<T>() => Pure.Forward(typeof(T[]), typeof(int[])) ? 17 : OnlyDead();
                public static int Guarded<T>()
                {
                    try { return typeof(T) == typeof(int) ? 31 : OnlyDead(); }
                    finally { State.Value++; }
                }
                public static int Run(int input)
                {
                    if (!Initialized.Same(typeof(int), typeof(int))) return -1;
                    if (State.Value != 29) return -2;
                    if (!NotInlined.Same(typeof(int), typeof(int))) return -3;
                    var result = Pick<int>() + Reject<int>() + Nested<int>() + Rank<int>() + Guarded<int>();
                    if (State.Value != 30) return -4;
                    var selected = input == 0 ? typeof(int) : typeof(string);
                    result += Forward<int>();
                    return result + (selected == typeof(int) ? 1 : 2);
                }
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedSource("ClosedBranchFixture", source)
            : assets.CompileSource("ClosedBranchFixture", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "ClosedBranchFixture.EntryPoint", "Run", [], Target: target));
        var methods = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values).Select(body => body.Method).ToArray();

        Assert.DoesNotContain(methods, method => method.Definition.Name == "OnlyDead");
        Assert.Contains(methods, method => method.DeclaringType.FullName == "ClosedBranchFixture.NotInlined" && method.Definition.Name == "Same");
        Assert.Contains(methods, method => method.DeclaringType.FullName == "ClosedBranchFixture.Initialized" && method.Definition.Name == "Same");
        Assert.Equal(115, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));
        Assert.Equal(116, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 1, target, result.StaticDataEnd));
    }
}
