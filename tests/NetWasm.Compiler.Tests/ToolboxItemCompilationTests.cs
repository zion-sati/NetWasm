using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ToolboxItemCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ToolboxMetadataPreservesValuesAndResolutionBoundary(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ToolboxFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ToolboxFixture.EntryPoint",
            "Run", [new("run", "ToolboxFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.ComponentModel;
        namespace ToolboxFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var none=new ToolboxItemAttribute(false);var otherNone=new ToolboxItemAttribute(false);
                if(none.ToolboxItemType!=null || none.ToolboxItemTypeName!="" || none.IsDefaultAttribute() || !none.Equals(ToolboxItemAttribute.None) || none.GetHashCode()!=otherNone.GetHashCode())return 1;
                var def=new ToolboxItemAttribute(true);
                if(!def.IsDefaultAttribute() || !def.Equals(ToolboxItemAttribute.Default) || def.ToolboxItemTypeName.Length==0)return 2;
                var named=new ToolboxItemAttribute("example");var equal=new ToolboxItemAttribute("example");
                if(named.ToolboxItemTypeName!="example" || named.IsDefaultAttribute() || !named.Equals(equal) || named.GetHashCode()!=equal.GetHashCode() || named.Equals(null) || named.Equals(new object()) || named.Equals(new ToolboxItemAttribute("different")))return 3;
                if(!named.Equals(named) || !new ToolboxItemAttribute("").Equals(none) || new ToolboxItemAttribute("").GetHashCode()!=none.GetHashCode())return 4;
                try{new ToolboxItemAttribute((string)null);return 5;}catch(ArgumentNullException e){if(e.ParamName!="toolboxItemTypeName")return 6;}
                try{new ToolboxItemAttribute((Type)null);return 7;}catch(ArgumentNullException e){if(e.ParamName!="toolboxItemType")return 8;}
                // A type's assembly-qualified name is its type-table name (issue 225); looking a type up by name is not.
                if(new ToolboxItemAttribute(typeof(int)).ToolboxItemTypeName.Length==0)return 9;
                for(var i=0;i<2;i++)try{var ignored=named.ToolboxItemType;return 10;}catch(PlatformNotSupportedException e){if(string.IsNullOrEmpty(e.Message))return 11;}
                try{var ignored=def.ToolboxItemType;return 12;}catch(PlatformNotSupportedException){}
                return 42;
            }
        }
        """;
}
