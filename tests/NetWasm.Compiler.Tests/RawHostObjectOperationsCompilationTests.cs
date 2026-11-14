using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class RawHostObjectOperationsCompilationTests
{
    [Fact]
    public void RawHostPreservesCloneAndArrayStoreSemantics()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("RawObjectFixture", """
            using System;
            namespace RawObjectFixture;
            public sealed class Item
            {
                public int Number;
                public object Reference;
                public Item Copy() => (Item)MemberwiseClone();
            }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var marker = new object();
                    var item = new Item { Number = 42, Reference = marker };
                    var copy = item.Copy(); copy.Number = 43;
                    if (ReferenceEquals(copy, item) || item.Number != 42 || !ReferenceEquals(copy.Reference, marker)) return 1;
                    Array integers = new int[2]; integers.SetValue(42, 0); integers.SetValue(null, 1);
                    if ((int)integers.GetValue(0) != 42 || (int)integers.GetValue(1) != 0) return 2;
                    Array nullable = new int?[2]; nullable.SetValue(42, 0); nullable.SetValue(null, 1);
                    if ((int)nullable.GetValue(0) != 42 || nullable.GetValue(1) != null) return 3;
                    Array strings = new string[1]; strings.SetValue("text", 0);
                    if ((string)strings.GetValue(0) != "text") return 4;
                    try { strings.SetValue(new object(), 0); return 5; } catch (InvalidCastException) { }
                    return input + 1;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "RawObjectFixture.EntryPoint", "Run", []));
        Assert.Equal(42, CompilerTestSupport.ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }
}
