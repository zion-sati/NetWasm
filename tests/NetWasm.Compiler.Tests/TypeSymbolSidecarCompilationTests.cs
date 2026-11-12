using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Issue 225: a program carries no type names. Without names its types read as type#<id>; with
// NetWasmTypeNames it loads them from its type-symbols sidecar at startup and forms them as .NET does,
// with <Assembly name unavailable> wherever .NET writes an assembly name.
public sealed class TypeSymbolSidecarCompilationTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        namespace Sample.Names;
        public sealed class Order { }
        public static class EntryPoint
        {
            private static int Check(bool condition, int bit) => condition ? bit : 0;
            public static int Run(int expectNames)
            {
                var order = typeof(Order);
                var list = new List<int>().GetType();
                var text = new Order().ToString();
                if (expectNames == 0)
                {
                    return Check(order.Name.StartsWith("type#"), 1) |
                        Check(order.Name == order.FullName && order.Name == text, 2) |
                        Check(order.Namespace == null, 4) |
                        Check(order.AssemblyQualifiedName == order.Name + ", <Assembly name unavailable>", 8) |
                        Check(list.Name != order.Name, 16);
                }
                return Check(order.Name == "Order", 1) |
                    Check(order.FullName == "Sample.Names.Order" && text == "Sample.Names.Order", 2) |
                    Check(order.Namespace == "Sample.Names", 4) |
                    Check(list.FullName == "System.Collections.Generic.List`1[[System.Int32, <Assembly name unavailable>]]", 8) |
                    Check(list.ToString() == "System.Collections.Generic.List`1[System.Int32]" &&
                        list.AssemblyQualifiedName == list.FullName + ", <Assembly name unavailable>", 16);
            }
        }
        """;

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void TypesReadAsTheirIdsWithoutNames(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(31, executor.Execute(new CompilationScenario(
            "TypeIds", Source, "Sample.Names.EntryPoint", optimize, target, 0, [])));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void NamesComeFromTheTypeSymbolsSidecar(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(31, executor.Execute(new CompilationScenario(
            "TypeNames", Source, "Sample.Names.EntryPoint", optimize, target, 1, [])
        {
            // Reading the sidecar takes wasi:filesystem, which the platform world provides.
            Exports = [new("run", "Sample.Names.EntryPoint", "Run")],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/platform@1.0.0",
            EmitTypeNames = true,
        }));
    }
}
