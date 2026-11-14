using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class CollectionTypeConverterCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CollectionConvertersPreserveFormattingAndMetadataBoundaries(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("CategoryFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "CategoryFixture.EntryPoint",
            "Run", [new("run", "CategoryFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.ComponentModel;
        namespace CategoryFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var collections = new CollectionConverter(); var arrays = new ArrayConverter();
                var list = new ArrayList(); list.Add(42);
                if (collections.ConvertToString(list) != "(Collection)" || collections.ConvertToString(new int[0]) != "(Collection)") return 1;
                if (collections.ConvertToString(null) != "" || collections.ConvertToString("text") != "text") return 2;
                if (collections.GetProperties(list).Count != 0 || collections.GetPropertiesSupported() || collections.GetPropertiesSupported(null)) return 3;
                if (!arrays.GetPropertiesSupported() || arrays.GetProperties(null) != null || arrays.GetProperties(new object()).Count != 0) return 4;
                // The array type's name, which is its type-table name unless type names are on (issue 225).
                if (arrays.ConvertToString(new int[2]) != typeof(int[]).Name + " Array" || arrays.ConvertToString(new string[0]) != typeof(string[]).Name + " Array") return 5;
                if (arrays.ConvertToString(list) != "(Collection)" || arrays.ConvertToString(null) != "") return 6;
                // An array's elements are its properties, as on .NET 11: the type table answers its element type.
                var elements = arrays.GetProperties(new int[2]);
                if (elements.Count != 2 || elements[0].Name != "[0]" || elements[1].Name != "[1]" ||
                    arrays.GetProperties(new int[0]).Count != 0) return 7;
                var caught = 0;
                try { collections.ConvertTo(list, null); } catch (ArgumentNullException) { caught++; }
                try { arrays.ConvertTo(new int[0], typeof(int)); } catch (NotSupportedException) { caught++; }
                return caught == 2 ? input + 1 : 100 + caught;
            }
        }
        """;
}
