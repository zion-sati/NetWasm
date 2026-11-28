// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Units;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// An attribute query on a Type known only at run time (a parameter, a lambda's argument, a GetType() result, an array's
// or a span's element, a value read through a ref) answers from
// the program's run-time types that carry the attribute, through every query form: the instance methods, Attribute's
// statics and the generic extensions, with inheritance constant or chosen at run time. A type without the attribute
// answers false, null or an empty array, and a null target throws as on .NET.
public sealed class RuntimeTypeAttributeQueryCompilationTests
{
    private const string Source = """
        using System;
        using System.Collections.Concurrent;
        using System.Reflection;

        namespace RuntimeTypeAttributes;

        [AttributeUsage(AttributeTargets.Class, Inherited = true)]
        public sealed class MarkAttribute(string name) : Attribute
        {
            public string Name { get; } = name;
        }

        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class LocalAttribute : Attribute;

        [Mark("base"), Local]
        public class Base;

        public class Derived : Base;

        [Mark("other")]
        public sealed class Other;

        public sealed class Plain;

        public sealed class Holder
        {
            public Type Held = typeof(Plain);
        }

        public static class EntryPoint
        {
            private static readonly ConcurrentDictionary<Type, bool> Marked = new();

            private static Type s_current = typeof(Plain);

            private static readonly Type[] s_types = [typeof(Base), typeof(Derived), typeof(Other), typeof(Plain)];

            private static int CountByRef(ref Type type) => type.GetCustomAttributes(typeof(MarkAttribute), true).Length;

            private static bool IsMarked(Type type) =>
                Marked.GetOrAdd(type, static type => type.GetCustomAttributes(typeof(MarkAttribute), true).Length > 0);

            private static bool Defined(Type type, bool inherit) => Attribute.IsDefined(type, typeof(MarkAttribute), inherit);

            private static string? NameOf(Type type) => type.GetCustomAttribute<MarkAttribute>()?.Name;

            private static int LocalCount(Type type, bool inherit) =>
                type.GetCustomAttributes(typeof(LocalAttribute), inherit).Length;

            private static Type Pick(int index) => index switch
            {
                0 => typeof(Base),
                1 => typeof(Derived),
                2 => typeof(Other),
                _ => typeof(Plain),
            };

            public static int Run(int input)
            {
                if (!IsMarked(Pick(0)) || !IsMarked(Pick(1)) || !IsMarked(Pick(2)) || IsMarked(Pick(3))) return 1;
                if (Defined(Pick(1), false) || !Defined(Pick(1), true) || Defined(Pick(3), true)) return 2;
                if (NameOf(Pick(2)) != "other" || NameOf(new Derived().GetType()) != "base" || NameOf(Pick(3)) is not null) return 3;
                var inherit = input > 0;
                if (LocalCount(Pick(0), inherit) != 1 || LocalCount(Pick(1), inherit) != 0) return 4;
                try { NameOf(input > 100 ? Pick(0) : null!); return 5; }
                catch (ArgumentNullException) { }
                try { LocalCount(input > 100 ? Pick(0) : null!, true); return 6; }
                catch (NullReferenceException) { }
                // A type a mutable field holds, static or instance, is known only at run time too.
                s_current = Pick(2);
                if (s_current.GetCustomAttribute<MarkAttribute>()?.Name != "other") return 7;
                var holder = new Holder { Held = Pick(1) };
                if (!Attribute.IsDefined(holder.Held, typeof(MarkAttribute), true)) return 8;
                // An element of a Type[] or of a span of Types, and a Type read through a ref, are Types too.
                if (s_types[input % 4 == 1 ? 2 : 3].GetCustomAttribute<MarkAttribute>()?.Name != "other") return 9;
                ReadOnlySpan<Type> span = s_types;
                if (!Attribute.IsDefined(span[input % 4], typeof(MarkAttribute), true) || Attribute.IsDefined(span[3], typeof(MarkAttribute), true)) return 10;
                ref Type first = ref s_types[input % 4 - 1];
                if (first.GetCustomAttributes(typeof(LocalAttribute), false).Length != 1) return 11;
                if (CountByRef(ref s_current) != 1 || CountByRef(ref holder.Held) != 1) return 12;
                var marked = 0;
                foreach (var type in s_types) marked += type.IsDefined(typeof(MarkAttribute), false) ? 1 : 0;
                if (marked != 2) return 13;
                return input + 1;
            }
        }
        """;

    private const string DevLoopBase = """
        using System;
        namespace DevLoop;
        [AttributeUsage(AttributeTargets.Class)]
        public sealed class MarkAttribute : Attribute;
        [Mark] public sealed class Marked;
        [Mark] public sealed class Other;
        public static class EntryPoint
        {
            private static bool IsMarked(Type type) => type.IsDefined(typeof(MarkAttribute), false);
            public static int Run(int input) => IsMarked(typeof(Marked)) ? input + 1 : 0;
        }
        """;

    // The dev loop's compiled units and early cutoff give what a full compile gives: an edit to the querying method
    // itself, and an edit that makes another attributed type a run-time type of the program, which the query's cases
    // must then include.
    [Fact]
    public void DevLoopCompilesAnswerAsAFullCompileWould()
    {
        using var assets = TestAssets.Create();
        var memory = new UnitStoreMemory();
        var queryEdit = DevLoopBase.Replace("typeof(MarkAttribute), false", "typeof(MarkAttribute), true", StringComparison.Ordinal);
        var typeEdit = DevLoopBase.Replace("IsMarked(typeof(Marked)) ?", "IsMarked(typeof(Marked)) && IsMarked(typeof(Other)) ?",
            StringComparison.Ordinal);
        foreach (var (step, source) in new[] { ("cold", DevLoopBase), ("query", queryEdit), ("back", DevLoopBase),
                     ("type", typeEdit), ("again", DevLoopBase) })
        {
            var options = new CompilerOptions(assets.CompileOptimizedSource("DevLoop", source), [assets.CoreLib],
                "DevLoop.EntryPoint", "Run", [], Optimization: FinalWasmOptimization.O0);
            var plain = ModuleCompilationResult.Of(NetWasmCompiler.Compile(options));
            ModuleCompilationResult module;
            using (var services = new ServiceCollection().AddNetWasmCompiler().UseUnitMemory(memory).BuildServiceProvider())
            {
                module = services.GetRequiredService<INetWasmModuleCompiler>().Compile(options with
                {
                    IntermediateOutputPath = "devloop",
                    CompiledUnits = true,
                });
            }
            Assert.True(plain.ApplicationModule.AsSpan().SequenceEqual(module.ApplicationModule),
                $"{step}: the module differs ({memory.InputReport("devloop")})");
        }
    }

    // Debug and Release IL: Release keeps the values on the stack where Debug stores them in locals typed Type.
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    public void ARunTimeTypesAttributesAreRead(WasmTarget target, bool optimizedIl)
    {
        using var assets = TestAssets.Create();
        var name = $"RuntimeTypeAttributes{target}{(optimizedIl ? "Release" : "")}";
        var assembly = optimizedIl ? assets.CompileOptimizedSource(name, Source) : assets.CompileSource(name, Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "RuntimeTypeAttributes.EntryPoint", "Run",
            [new("run", "RuntimeTypeAttributes.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }
}
