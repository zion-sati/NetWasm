using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ModuleMetadataContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ModuleFiltersAndLookupForwardingWork(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ModuleMetadataFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ModuleMetadataFixture.EntryPoint",
            "Run", [new("run", "ModuleMetadataFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            // The checks read type names, which a program carries only in its type-symbols sidecar (issue 225).
            EmitTypeNames: true));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd,
            System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, timeZoneAssetPath: null,
            typeSymbols: result.TypeSymbols!.Bytes));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace ModuleMetadataFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (Portable(input) != 42) return 20;
                Module m = new Empty();
                var pe = PortableExecutableKinds.ILOnly; var machine = ImageFileMachine.I386;
                Action[] calls = {
                    () => m.GetMethod("X"), () => m.GetMethod("X", Type.EmptyTypes),
                    () => m.GetMethod("X", BindingFlags.Public, null, CallingConventions.Any, Type.EmptyTypes, null),
                    () => m.GetMethods(), () => m.GetMethods(BindingFlags.Public), () => m.GetField("X"), () => m.GetField("X", BindingFlags.Public),
                    () => m.GetFields(), () => m.GetFields(BindingFlags.Public), () => m.GetType("X"), () => m.GetType("X", true), () => m.GetType("X", true, true),
                    () => m.FindTypes(null, null), () => { _ = m.MetadataToken; },
                    () => m.ResolveField(1), () => m.ResolveField(1, null, null), () => m.ResolveMember(1), () => m.ResolveMember(1, null, null),
                    () => m.ResolveMethod(1), () => m.ResolveMethod(1, null, null), () => m.ResolveSignature(1), () => m.ResolveString(1),
                    () => m.ResolveType(1), () => m.ResolveType(1, null, null), () => m.GetPEKind(out pe, out machine)
                };
                var rejected=0;
                foreach (var call in calls) { try { call(); } catch (PlatformNotSupportedException) { rejected++; } }
                if (rejected != calls.Length || pe != PortableExecutableKinds.ILOnly || machine != ImageFileMachine.I386) return 21;
                return input+1;
            }
            public static int Portable(int input)
            {
                if (!Module.FilterTypeName(typeof(int), "Int32") || !Module.FilterTypeName(typeof(int), "Int*") || Module.FilterTypeName(typeof(int), "int*")) return 1;
                if (!Module.FilterTypeNameIgnoreCase(typeof(int), "int32") || !Module.FilterTypeName(typeof(int), "*")) return 2;
                try { Module.FilterTypeName(typeof(int), new object()); return 3; } catch (InvalidFilterCriteriaException) { }
                var m = new Custom();
                if (m.FindTypes(Module.FilterTypeName, "Int*").Length != 1 || m.FindTypes(null, null).Length != 2 || m.FindTypes((t,c) => false, null).Length != 0) return 4;
                try { m.GetMethod(null!); return 5; } catch (ArgumentNullException e) { if (e.ParamName != "name") return 6; }
                try { m.GetMethod("X", null!); return 7; } catch (ArgumentNullException e) { if (e.ParamName != "types") return 8; }
                try { m.GetMethod("X", new Type[] { null! }); return 9; } catch (ArgumentNullException) { }
                if (m.Calls != 0) return 10;
                m.GetMethod("X");
                var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
                if (m.Calls != 1 || m.Flags != flags || m.Types != null || m.Convention != CallingConventions.Any) return 11;
                var types = new[] { typeof(int) }; m.GetMethod("X", types);
                if (!ReferenceEquals(m.Types, types)) return 12;
                m.GetMethod("X", BindingFlags.NonPublic, null, CallingConventions.VarArgs, types, null);
                if (m.Flags != BindingFlags.NonPublic || m.Convention != CallingConventions.VarArgs) return 13;
                if (m.GetType("X") != typeof(int) || m.Throw || m.Ignore) return 14;
                m.GetType("X", true); if (m.Throw || !m.Ignore) return 15;
                return input+1;
            }
            private sealed class Empty : Module { }
            private sealed class Custom : Module
            {
                public int Calls; public BindingFlags Flags; public Type[]? Types; public CallingConventions Convention;
                public bool Throw, Ignore;
                public override Type[] GetTypes() => new[] { typeof(int), typeof(string) };
                protected override MethodInfo? GetMethodImpl(string name, BindingFlags flags, Binder? binder, CallingConventions convention, Type[]? types, ParameterModifier[]? modifiers)
                { Calls++; Flags=flags; Types=types; Convention=convention; return null; }
                public override Type GetType(string name, bool throwing, bool ignore) { Throw=throwing; Ignore=ignore; return typeof(int); }
            }
        }
        """;
}
