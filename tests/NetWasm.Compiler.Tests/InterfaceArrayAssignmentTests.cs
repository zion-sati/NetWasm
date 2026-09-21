using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class InterfaceArrayAssignmentTests
{
    [Fact]
    public void CompilerExecutesObjectToInterfaceCastAfterArrayLookup()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "InterfaceCastCompilerFixture",
            """
            namespace InterfaceCastCompilerFixture;

            interface IItem
            {
                int Value { get; }
            }

            sealed class Item : IItem
            {
                public int Value => 42;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    object[] values = [new Item()];
                    var items = new IItem[values.Length];
                    items[0] = (IItem)values[0];
                    return items[0].Value;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "InterfaceCastCompilerFixture.EntryPoint",
            "Run",
            []));

        Assert.Contains(result.Program.TypeTestSites.Values, site =>
            site.TargetType.FullName == "InterfaceCastCompilerFixture.IItem" &&
            site.MatchingTypes.Any(type =>
                type.FullName == "InterfaceCastCompilerFixture.Item"));
        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 0));
    }

    [Fact]
    public void CompilerRejectsIncompatibleValueStoredThroughCovariantArrayReference()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "InterfaceArrayMismatchCompilerFixture",
            """
            namespace InterfaceArrayMismatchCompilerFixture;

            interface IItem
            {
            }

            sealed class Other
            {
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    object[] items = new IItem[1];
                    try
                    {
                        items[0] = new Other();
                        return -1;
                    }
                    catch (System.ArrayTypeMismatchException)
                    {
                        return 42;
                    }
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "InterfaceArrayMismatchCompilerFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 0));
    }

    [Fact]
    public void CompilerBuildsAssignmentMetadataForClosedGenericInterfaces()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GenericInterfaceArrayCompilerFixture",
            """
            namespace GenericInterfaceArrayCompilerFixture;

            interface IValue<out T>
            {
                T Get();
            }

            sealed class Value<T>(T value) : IValue<T>
            {
                public T Get() => value;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    object[] values = new IValue<object>[1];
                    values[0] = new Value<string>("forty-two");
                    return ((IValue<object>)values[0]).Get().ToString()!.Length + 33;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "GenericInterfaceArrayCompilerFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 0));
    }
}
