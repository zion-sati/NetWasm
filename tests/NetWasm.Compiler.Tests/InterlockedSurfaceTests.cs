using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.TestInfrastructure;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class InterlockedSurfaceTests
{
    [Fact]
    public void DeclaresStandard21SurfaceAndIntegerBitwiseOperations()
    {
        using var assets = TestAssets.Create();
        var compilation = CSharpCompilation.Create("InterlockedSurface",
            references: [MetadataReference.CreateFromFile(assets.CoreLib)]);
        var type = compilation.GetTypeByMetadataName("System.Threading.Interlocked");
        Assert.NotNull(type);
        var methods = type.GetMembers().OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.Ordinary &&
                method.DeclaredAccessibility == Accessibility.Public).ToArray();
        Assert.Equal(ExpectedMembers.Order(), methods.Select(method => method.GetDocumentationCommentId()).Order());
        Assert.All(methods, method =>
        {
            Assert.True(method.IsStatic);
            Assert.Equal(Accessibility.Public, method.DeclaredAccessibility);
            if (method.Parameters.Length == 0)
            {
                Assert.True(method.ReturnsVoid);
                return;
            }
            Assert.Equal(RefKind.Ref, method.Parameters[0].RefKind);
            Assert.True(SymbolEqualityComparer.Default.Equals(method.ReturnType, method.Parameters[0].Type));
            // As upstream: CompareExchange and Exchange take any type and reject structs other than
            // primitives and enums when called; And and Or take integer primitives and enums.
            if (method.IsGenericMethod)
            {
                var parameter = Assert.Single(method.TypeParameters);
                Assert.Equal(method.Name is "And" or "Or", parameter.HasValueTypeConstraint);
                Assert.False(parameter.HasReferenceTypeConstraint);
            }
        });
    }

    [Fact]
    public void IncrementDoesNotRetainUnusedInterlockedOperations()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("InterlockedRetention", """
            using System.Threading;
            public static class EntryPoint
            {
                public static int Run(int input) => Interlocked.Increment(ref input);
            }
            """);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EntryPoint", "Run", []));
        var methods = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Select(body => body.Method)
            .Where(method => method.DeclaringType.FullName == "System.Threading.Interlocked");
        Assert.Equal(IncrementMembers,
            methods.Select(method => method.Definition.Name).Distinct().Order());
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // Upstream's generic And and Or combine enums of every integer width in place and return the
    // previous value; a floating-point type is not supported.
    [Fact]
    public void GenericAndAndOrCombineEnumsOfEveryWidth()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("InterlockedGenericBitwise", """
            using System;
            using System.Threading;
            public enum Narrow : byte { A = 1, B = 2, C = 4 }
            public enum Short : ushort { A = 0x100, B = 0x200 }
            [Flags] public enum Flags { A = 1, B = 2, C = 4 }
            public enum Wide : long { A = 1L << 40, B = 1L << 41 }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var narrow = Narrow.A;
                    var wide = Wide.A;
                    var shortValue = Short.A;
                    var flags = Flags.A | Flags.B;
                    var ok = Interlocked.Or(ref narrow, Narrow.C) == Narrow.A && narrow == (Narrow.A | Narrow.C)
                        && Interlocked.Or(ref shortValue, Short.B) == Short.A && shortValue == (Short.A | Short.B)
                        && Interlocked.And(ref flags, Flags.B | Flags.C) == (Flags.A | Flags.B) && flags == Flags.B
                        && Interlocked.Or(ref wide, Wide.B) == Wide.A && wide == (Wide.A | Wide.B)
                        && Interlocked.And(ref narrow, Narrow.C) == (Narrow.A | Narrow.C) && narrow == Narrow.C;
                    var single = 1f;
                    try
                    {
                        Interlocked.Or(ref single, 2f);
                        ok = false;
                    }
                    catch (NotSupportedException)
                    {
                    }
                    return ok ? input + 1 : -1;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EntryPoint", "Run", []));
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // Upstream's generic Exchange and CompareExchange take enums and primitives as well as
    // references: a value compares by its bits, and another struct is not supported.
    [Fact]
    public void GenericExchangeAndCompareExchangeTakeEnumsAndPrimitives()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("InterlockedGenericExchange", """
            using System;
            using System.Threading;
            public enum Narrow : byte { A = 1, B = 2 }
            public enum Short : short { A = 0x100, B = 0x200 }
            public enum State { Idle, Running }
            public enum Wide : long { A = 1L << 40, B = 1L << 41 }
            public struct Pair { public int Left, Right; }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var flag = false;
                    var narrow = Narrow.A;
                    var shortValue = Short.A;
                    var state = State.Idle;
                    var wide = Wide.A;
                    var text = "old";
                    var ok = !Interlocked.Exchange(ref flag, true) && flag && Interlocked.Exchange(ref flag, true)
                        && Interlocked.CompareExchange(ref narrow, Narrow.B, Narrow.A) == Narrow.A && narrow == Narrow.B
                        && Interlocked.CompareExchange(ref narrow, Narrow.A, Narrow.A) == Narrow.B && narrow == Narrow.B
                        && Interlocked.Exchange(ref shortValue, Short.B) == Short.A && shortValue == Short.B
                        && Interlocked.CompareExchange(ref state, State.Running, State.Idle) == State.Idle && state == State.Running
                        && Interlocked.CompareExchange(ref wide, Wide.B, Wide.B) == Wide.A && wide == Wide.A
                        && Interlocked.CompareExchange(ref wide, Wide.B, Wide.A) == Wide.A && wide == Wide.B
                        && Interlocked.CompareExchange(ref text, "new", "other") == "old" && text == "old"
                        && Interlocked.Exchange(ref text, "new") == "old" && text == "new";
                    var pair = new Pair();
                    try
                    {
                        Interlocked.Exchange(ref pair, new Pair { Left = 1 });
                        ok = false;
                    }
                    catch (NotSupportedException)
                    {
                    }

                    try
                    {
                        Interlocked.CompareExchange(ref pair, new Pair { Left = 1 }, pair);
                        ok = false;
                    }
                    catch (NotSupportedException)
                    {
                    }

                    return ok && pair.Left == 0 ? input + 1 : -1;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EntryPoint", "Run", []));
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    private static readonly string[] IncrementMembers = ["Add", "Increment", "ValidateLocation"];

    // Frozen from NETStandard.Library.Ref 2.1.0; And/Or are explicitly newer additions.
    private static readonly string[] ExpectedMembers =
    [
        "M:System.Threading.Interlocked.Add(System.Int32@,System.Int32)",
        "M:System.Threading.Interlocked.Add(System.Int64@,System.Int64)",
        "M:System.Threading.Interlocked.And(System.Int32@,System.Int32)",
        "M:System.Threading.Interlocked.And(System.Int64@,System.Int64)",
        "M:System.Threading.Interlocked.And(System.UInt32@,System.UInt32)",
        "M:System.Threading.Interlocked.And(System.UInt64@,System.UInt64)",
        "M:System.Threading.Interlocked.CompareExchange(System.Double@,System.Double,System.Double)",
        "M:System.Threading.Interlocked.CompareExchange(System.Int32@,System.Int32,System.Int32)",
        "M:System.Threading.Interlocked.CompareExchange(System.Int64@,System.Int64,System.Int64)",
        "M:System.Threading.Interlocked.CompareExchange(System.IntPtr@,System.IntPtr,System.IntPtr)",
        "M:System.Threading.Interlocked.CompareExchange(System.Object@,System.Object,System.Object)",
        "M:System.Threading.Interlocked.CompareExchange(System.Single@,System.Single,System.Single)",
        "M:System.Threading.Interlocked.CompareExchange``1(``0@,``0,``0)",
        "M:System.Threading.Interlocked.Decrement(System.Int32@)",
        "M:System.Threading.Interlocked.Decrement(System.Int64@)",
        "M:System.Threading.Interlocked.Exchange(System.Double@,System.Double)",
        "M:System.Threading.Interlocked.Exchange(System.Int32@,System.Int32)",
        "M:System.Threading.Interlocked.Exchange(System.Int64@,System.Int64)",
        "M:System.Threading.Interlocked.Exchange(System.IntPtr@,System.IntPtr)",
        "M:System.Threading.Interlocked.Exchange(System.Object@,System.Object)",
        "M:System.Threading.Interlocked.Exchange(System.Single@,System.Single)",
        "M:System.Threading.Interlocked.Exchange``1(``0@,``0)",
        "M:System.Threading.Interlocked.Increment(System.Int32@)",
        "M:System.Threading.Interlocked.Increment(System.Int64@)",
        "M:System.Threading.Interlocked.MemoryBarrier",
        "M:System.Threading.Interlocked.MemoryBarrierProcessWide",
        "M:System.Threading.Interlocked.Or(System.Int32@,System.Int32)",
        "M:System.Threading.Interlocked.Or(System.Int64@,System.Int64)",
        "M:System.Threading.Interlocked.Or(System.UInt32@,System.UInt32)",
        "M:System.Threading.Interlocked.Or(System.UInt64@,System.UInt64)",
        "M:System.Threading.Interlocked.Or``1(``0@,``0)",
        "M:System.Threading.Interlocked.And``1(``0@,``0)",
        "M:System.Threading.Interlocked.Read(System.Int64@)",
        "M:System.Threading.Interlocked.Add(System.UInt32@,System.UInt32)",
        "M:System.Threading.Interlocked.Decrement(System.UInt32@)",
        "M:System.Threading.Interlocked.Increment(System.UInt32@)",
        "M:System.Threading.Interlocked.Add(System.UInt64@,System.UInt64)",
        "M:System.Threading.Interlocked.Decrement(System.UInt64@)",
        "M:System.Threading.Interlocked.Increment(System.UInt64@)",
        "M:System.Threading.Interlocked.Exchange(System.Byte@,System.Byte)",
        "M:System.Threading.Interlocked.CompareExchange(System.Byte@,System.Byte,System.Byte)",
        "M:System.Threading.Interlocked.Exchange(System.SByte@,System.SByte)",
        "M:System.Threading.Interlocked.CompareExchange(System.SByte@,System.SByte,System.SByte)",
        "M:System.Threading.Interlocked.Exchange(System.Int16@,System.Int16)",
        "M:System.Threading.Interlocked.CompareExchange(System.Int16@,System.Int16,System.Int16)",
        "M:System.Threading.Interlocked.Exchange(System.UInt16@,System.UInt16)",
        "M:System.Threading.Interlocked.CompareExchange(System.UInt16@,System.UInt16,System.UInt16)",
        "M:System.Threading.Interlocked.Exchange(System.UInt32@,System.UInt32)",
        "M:System.Threading.Interlocked.CompareExchange(System.UInt32@,System.UInt32,System.UInt32)",
        "M:System.Threading.Interlocked.Exchange(System.UInt64@,System.UInt64)",
        "M:System.Threading.Interlocked.CompareExchange(System.UInt64@,System.UInt64,System.UInt64)",
        "M:System.Threading.Interlocked.Exchange(System.UIntPtr@,System.UIntPtr)",
        "M:System.Threading.Interlocked.CompareExchange(System.UIntPtr@,System.UIntPtr,System.UIntPtr)",
        "M:System.Threading.Interlocked.Read(System.UInt64@)",
    ];
}
