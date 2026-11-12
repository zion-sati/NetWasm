using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class UnsafeAccessorCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void LibraryAccessorsPreserveBodiesAndSourceFrames(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSourceWithPortablePdb("AccessorLibrary", """
            using System;
            using System.Runtime.CompilerServices;
            namespace AccessorLibrary;
            public sealed class Owner
            {
                private int value;
                private Owner(int value) => this.value = value;
                private int Read() => value;
                private void Fail()
                {
            #line 303 "Safe/AccessorTarget.cs"
                    throw new InvalidOperationException("accessor target");
            #line default
                }
            }
            public static class Accessors
            {
                [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
                public static extern Owner Create(int value);
                [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Read")]
                public static extern int Read(Owner owner);
                [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Fail")]
                public static extern void Fail(Owner owner);
                [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "value")]
                public static extern ref int Field(Owner owner);

                // An annotation on physical IL must never replace that body.
                [UnsafeAccessor((UnsafeAccessorKind)99)]
                public static int Physical(int value) => value;
                [UnsafeAccessor((UnsafeAccessorKind)99)]
                public static extern void UnusedInvalid();
            }
            """, optimized);
        var assembly = assets.CompileSourceWithPortablePdb("AccessorApplication", """
            using System;
            using AccessorLibrary;
            namespace AccessorApplication;
            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    var owner = Accessors.Create(1);
                    Accessors.Field(owner) = value;
                    if (Accessors.Read(owner) != value || Accessors.Physical(value) != value) return 1;
                    try
                    {
            #line 101 "Safe/AccessorCaller.cs"
                        Accessors.Fail(owner);
            #line default
                    }
                    catch (InvalidOperationException error)
                    {
                        var trace = error.StackTrace;
                        if (trace is null || !trace.Contains("AccessorTarget.cs:line 303")) return 2;
                        if (!trace.Contains("AccessorCaller.cs:line 101")) return 3;
                        if (!trace.Contains("Accessors::Fail")) return 4;
                        return value;
                    }
                    return 5;
                }
            }
            """, optimized, library);
        var options = new CompilerOptions(assembly, [assets.CoreLib, library],
            "AccessorApplication.EntryPoint", "Run", [], Target: target, EmitStackTrace: true,
            ProjectDirectory: assets.Directory);

        {
            var result = NetWasmCompiler.Compile(options);
            var accessors = result.Program.MethodInstances.Values.Where(method =>
                method.DeclaringType.FullName == "AccessorLibrary.Accessors").ToArray();
            Assert.Equal(5, accessors.Length);
            Assert.DoesNotContain(accessors, method => method.Definition.Name == "UnusedInvalid");
            Assert.True(Assert.Single(accessors, method => method.Definition.Name == "Physical").Definition.HasBody);
            Assert.All(accessors.Where(method => method.Definition.Name != "Physical"), method =>
            {
                Assert.False(method.Definition.HasBody);
                Assert.True(method.Definition.HasManagedBody);
                Assert.NotNull(method.Definition.UnsafeAccessor);
            });
            var symbols = string.Join('\n', NetWasm.Compiler.Tests.Symbols.SymbolSidecarContents.Read(result.MethodSymbols!.Bytes).Texts);
            Assert.Contains("AccessorLibrary.Accessors::Fail", symbols, StringComparison.Ordinal);
            Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory,
                42, target, result.StaticDataEnd, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty,
                timeZoneAssetPath: null, methodSymbols: result.MethodSymbols!.Bytes));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnusedFieldAccessorDisablesReadonlyTypeProofs(bool generic)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource("AttributeLibrary", $$"""
            using System;
            using System.Reflection;
            namespace AttributeLibrary;
            public sealed class MarkerAttribute : Attribute;
            [Marker] public sealed class Marked;
            public sealed class Owner{{(generic ? "<T>" : "")}}
            {
                private readonly Type selected = typeof({{(generic ? "T" : "Marked")}});
                public int Read() => selected.GetCustomAttribute<MarkerAttribute>() is null ? 1 : 42;
            }
            """);
        var ownerType = generic ? "Owner<Marked>" : "Owner";
        string CompileEntry(bool expose) => assets.CompileSource("AttributeApplication", $$"""
            using System;
            using System.Runtime.CompilerServices;
            using AttributeLibrary;
            namespace AttributeApplication;
            public static class EntryPoint
            {
                public static int Run(int value) => new {{ownerType}}().Read();
                {{(expose ? "[UnsafeAccessor(UnsafeAccessorKind.Field, Name = \"selected\")] private static extern ref Type Expose(" + ownerType + " owner);" : "")}}
            }
            """, library);
        var assembly = CompileEntry(false);
        var options = new CompilerOptions(assembly, [assets.CoreLib, library],
            "AttributeApplication.EntryPoint", "Run", []);
        var result = NetWasmCompiler.Compile(options);
        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory,
            42, WasmTarget.Wasm32, result.StaticDataEnd));

        // The same path now names an entry assembly that exposes the field.
        CompileEntry(true);
        var error = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
        Assert.Contains("Read", error.Diagnostic.Method);
    }
}
