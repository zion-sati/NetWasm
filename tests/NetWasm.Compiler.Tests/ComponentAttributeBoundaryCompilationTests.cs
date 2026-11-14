using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ComponentAttributeBoundaryCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TypeBasedMetadataConstructorsRecordTheTypesName(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ComponentAttributeBoundaryFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ComponentAttributeBoundaryFixture.EntryPoint",
            "Run", [new("run", "ComponentAttributeBoundaryFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.ComponentModel;
        namespace ComponentAttributeBoundaryFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                // The runtime type table answers AssemblyQualifiedName, so the type-based constructors record it.
                var name = typeof(int).AssemblyQualifiedName;
                var other = typeof(string).AssemblyQualifiedName;
                if (string.IsNullOrEmpty(name) || name == other) return 2;
                if (new TypeConverterAttribute(typeof(int)).ConverterTypeName != name) return 3;
                if (new DesignerAttribute(typeof(int)).DesignerTypeName != name) return 4;
                var named = new DesignerAttribute("name", typeof(int));
                if (named.DesignerTypeName != "name" || named.DesignerBaseTypeName != name) return 5;
                var pair = new DesignerAttribute(typeof(int), typeof(string));
                if (pair.DesignerTypeName != name || pair.DesignerBaseTypeName != other) return 6;
                var caught = 0;
                try { new TypeConverterAttribute((Type)null); } catch (ArgumentNullException e) { if (e.ParamName == "type") caught++; }
                try { new DesignerAttribute((Type)null); } catch (ArgumentNullException e) { if (e.ParamName == "designerType") caught++; }
                try { new DesignerAttribute("name", (Type)null); } catch (ArgumentNullException e) { if (e.ParamName == "designerBaseType") caught++; }
                try { new DesignerAttribute(typeof(int), null); } catch (ArgumentNullException e) { if (e.ParamName == "designerBaseType") caught++; }
                try { new DesignerAttribute((Type)null, typeof(int)); } catch (ArgumentNullException e) { if (e.ParamName == "designerType") caught++; }
                return caught == 5 ? input + 1 : 1;
            }
        }
        """;
}
