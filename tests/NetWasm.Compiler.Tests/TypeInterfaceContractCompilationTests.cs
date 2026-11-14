using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TypeInterfaceContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void InterfaceDiscoveryAnswersFromTheTypeTable(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TypeInterfaceFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TypeInterfaceFixture.EntryPoint",
            "Run", [new("run", "TypeInterfaceFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace TypeInterfaceFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var type = typeof(Widget);
                var calls = 0;
                TypeFilter filter = (candidate, criteria) => { calls++; return candidate == (Type)criteria; };
                if (!filter(typeof(IWidget), typeof(IWidget))) return 1;
                calls = 0;
                // The type table lists a type's interfaces, so FindInterfaces runs the filter on each; lookups by name
                // need the type names, and interface maps need method metadata.
                var interfaces = type.GetInterfaces();
                if (interfaces.Length != 1 || interfaces[0] != typeof(IWidget)) return 4;
                var found = type.FindInterfaces(filter, typeof(IWidget));
                if (found.Length != 1 || found[0] != typeof(IWidget) || calls != 1) return 5;
                calls = 0;
                var caught = 0;
                try { type.GetInterface("IWidget"); } catch (PlatformNotSupportedException) { caught++; }
                try { type.GetInterface("iwidget", true); } catch (PlatformNotSupportedException) { caught++; }
                try { type.GetInterfaceMap(typeof(IWidget)); } catch (PlatformNotSupportedException) { caught++; }
                try { type.GetInterface(null); } catch (ArgumentNullException e) { if (e.ParamName == "name") caught++; }
                try { type.FindInterfaces(null, null); } catch (ArgumentNullException e) { if (e.ParamName == "filter") caught++; }
                try { type.GetInterfaceMap(null); } catch (ArgumentNullException e) { if (e.ParamName == "interfaceType") caught++; }
                var map = new InterfaceMapping { TargetType = type, InterfaceType = typeof(IWidget), TargetMethods = new MethodInfo[0], InterfaceMethods = new MethodInfo[0] };
                var copy = map;
                map.TargetType = typeof(object);
                if (copy.TargetType != type || copy.InterfaceType != typeof(IWidget) || copy.TargetMethods.Length != 0 || copy.InterfaceMethods.Length != 0) return 2;
                IWidget widget = new Widget();
                return caught == 6 && calls == 0 && widget.Value() == 42 && typeof(IWidget).IsAssignableFrom(type) ? input + 1 : 3;
            }
            private interface IWidget { int Value(); }
            private class Widget : IWidget { public int Value() => 42; }
        }
        """;
}
