using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TypeLookupContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RuntimeMemberLookupHasCatchableFallbacks(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TypeLookupFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TypeLookupFixture.EntryPoint",
            "Run", [new("run", "TypeLookupFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace TypeLookupFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var caught = 0;
                try { _ = typeof(Target).TypeInitializer; } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetConstructor(Type.EmptyTypes); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetConstructor(BindingFlags.Public, Type.EmptyTypes); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetConstructor(BindingFlags.Public, null, Type.EmptyTypes, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetConstructor(BindingFlags.Public, null, CallingConventions.Any, Type.EmptyTypes, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetConstructors(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetConstructors(BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetEvent("Member"); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetEvent("Member", BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetEvents(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetEvents(BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetField("Member"); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetField("Member", BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetFields(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetFields(BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMember("Member"); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMember("Member", BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMember("Member", MemberTypes.All, BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMembers(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMembers(BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMethod("Member"); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMethod("Member", BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMethod("Member", BindingFlags.Public, Type.EmptyTypes); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMethod("Member", Type.EmptyTypes); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMethod("Member", Type.EmptyTypes, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMethod("Member", BindingFlags.Public, null, Type.EmptyTypes, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMethod("Member", BindingFlags.Public, null, CallingConventions.Any, Type.EmptyTypes, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMethod("Member", 0, Type.EmptyTypes); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMethod("Member", 0, Type.EmptyTypes, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMethod("Member", 0, BindingFlags.Public, Type.EmptyTypes); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMethod("Member", 0, BindingFlags.Public, null, Type.EmptyTypes, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMethod("Member", 0, BindingFlags.Public, null, CallingConventions.Any, Type.EmptyTypes, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMethods(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetMethods(BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetNestedType("Member"); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetNestedType("Member", BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                // The type table lists a type's nested types; Target has none.
                if (typeof(Target).GetNestedTypes().Length != 0 || typeof(Target).GetNestedTypes(BindingFlags.Public).Length != 0) return 4;
                try { _ = typeof(Target).GetProperty("Member"); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetProperty("Member", BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetProperty("Member", typeof(int)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetProperty("Member", Type.EmptyTypes); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetProperty("Member", typeof(int), Type.EmptyTypes); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetProperty("Member", typeof(int), Type.EmptyTypes, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetProperty("Member", BindingFlags.Public, null, typeof(int), Type.EmptyTypes, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetProperties(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetProperties(BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetDefaultMembers(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).GetConstructor((Type[])null); } catch (ArgumentNullException e) { if (e.ParamName == "types") caught++; }
                try { _ = typeof(Target).GetConstructor(new Type[] { null }); } catch (ArgumentNullException e) { if (e.ParamName == "types") caught++; }
                try { _ = typeof(Target).GetMethod(null); } catch (ArgumentNullException e) { if (e.ParamName == "name") caught++; }
                try { _ = typeof(Target).GetMethod("Member", (Type[])null); } catch (ArgumentNullException e) { if (e.ParamName == "types") caught++; }
                try { _ = typeof(Target).GetMethod("Member", new Type[] { null }); } catch (ArgumentNullException e) { if (e.ParamName == "types") caught++; }
                try { _ = typeof(Target).GetMethod(null, 0, Type.EmptyTypes); } catch (ArgumentNullException e) { if (e.ParamName == "name") caught++; }
                try { _ = typeof(Target).GetMethod("Member", 0, (Type[])null); } catch (ArgumentNullException e) { if (e.ParamName == "types") caught++; }
                try { _ = typeof(Target).GetMethod("Member", 0, new Type[] { null }); } catch (ArgumentNullException e) { if (e.ParamName == "types") caught++; }
                try { _ = typeof(Target).GetProperty(null); } catch (ArgumentNullException e) { if (e.ParamName == "name") caught++; }
                try { _ = typeof(Target).GetProperty(null, typeof(int)); } catch (ArgumentNullException e) { if (e.ParamName == "name") caught++; }
                try { _ = typeof(Target).GetProperty("Member", (Type[])null); } catch (ArgumentNullException e) { if (e.ParamName == "types") caught++; }
                try { _ = typeof(Target).GetMethod("Member", -1, Type.EmptyTypes); } catch (ArgumentOutOfRangeException e) { if (e.ParamName == "genericParameterCount") caught++; }
                object metadata = typeof(Target);
                if (metadata is not IReflect reflect || reflect.UnderlyingSystemType != typeof(Target)) return 2;
                if (!typeof(IReflect).IsAssignableFrom(typeof(Type))) return 3;
                try { _ = reflect.GetMethod("Member", BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = reflect.GetMethod("Member", BindingFlags.Public, null, Type.EmptyTypes, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = reflect.GetMethods(BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = reflect.GetField("Member", BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = reflect.GetFields(BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = reflect.GetProperty("Member", BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = reflect.GetProperty("Member", BindingFlags.Public, null, typeof(int), Type.EmptyTypes, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = reflect.GetProperties(BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = reflect.GetMember("Member", BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = reflect.GetMembers(BindingFlags.Public); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = reflect.InvokeMember("Member", BindingFlags.Public, null, null, null, null, null, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).InvokeMember("Member", BindingFlags.Public, null, null, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = typeof(Target).InvokeMember("Member", BindingFlags.Public, null, null, null, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                return caught == 71 ? input + 1 : 1;
            }
            private sealed class Target { }
        }
        """;
}
