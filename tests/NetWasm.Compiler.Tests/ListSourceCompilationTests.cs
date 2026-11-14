using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ListSourceCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SuppliedListsAndEditorMetadataPreserveContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ListSourceFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ListSourceFixture.EntryPoint",
            "Run", [new("run", "ListSourceFixture.EntryPoint", "Run")], Target: target,
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
        namespace ListSourceFixture;
        sealed class Source : IListSource
        {
            public readonly ArrayList Values = new ArrayList();
            public bool ContainsListCollection => false;
            public IList GetList() => Values;
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var source=new Source(); IListSource contract=source;
                if(contract.ContainsListCollection || !ReferenceEquals(contract.GetList(),source.Values)) return 1;
                contract.GetList().Add(42); if((int)source.Values[0]!=42) return 2;
                var yes=new MergablePropertyAttribute(true); var no=new MergablePropertyAttribute(false);
                if(!yes.AllowMerge || no.AllowMerge || !yes.Equals(MergablePropertyAttribute.Yes) || !no.Equals(MergablePropertyAttribute.No) || !yes.IsDefaultAttribute() || no.IsDefaultAttribute()) return 3;
                if(yes.GetHashCode()!=MergablePropertyAttribute.Yes.GetHashCode() || yes.Equals(null) || yes.Equals(no) || !ReferenceEquals(MergablePropertyAttribute.Default,MergablePropertyAttribute.Yes)) return 4;
                var empty=new EditorAttribute(); if(empty.EditorTypeName!="" || empty.EditorBaseTypeName!="") return 5;
                var first=new EditorAttribute("Editor","Base, Assembly"); var same=new EditorAttribute("Editor","Base, Assembly"); var other=new EditorAttribute("Other","Base, Elsewhere");
                if(!first.Equals(first) || !first.Equals(same) || first.Equals(other) || first.Equals(null) || first.GetHashCode()!=same.GetHashCode()) return 6;
                if(!first.TypeId.Equals(other.TypeId) || !ReferenceEquals(first.TypeId,first.TypeId) || first.GetHashCode()!=same.GetHashCode()) return 7;
                if(!new EditorAttribute("Editor",(string)null).TypeId.Equals(empty.TypeId)) return 8;
                var validations=0;
                try { new EditorAttribute((string)null,"Base"); } catch(ArgumentNullException e) { if(e.ParamName=="typeName") validations++; }
                try { new EditorAttribute("Editor",(Type)null); } catch(ArgumentNullException e) { if(e.ParamName=="baseType") validations++; }
                try { new EditorAttribute((Type)null,typeof(object)); } catch(ArgumentNullException e) { if(e.ParamName=="type") validations++; }
                try { new EditorAttribute(typeof(object),(Type)null); } catch(ArgumentNullException e) { if(e.ParamName=="baseType") validations++; }
                if(validations!=4) return 9;
                // A type's assembly-qualified name is its type-table name (issue 225), so the attribute takes types.
                if(new EditorAttribute("Editor",typeof(object)).EditorBaseTypeName!.Length==0 ||
                   new EditorAttribute(typeof(string),typeof(object)).EditorTypeName.Length==0) return 10;
                return 42;
            }
        }
        """;
}
