using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class PropertyTabCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CallerTabTypesScopesAndValidationPreserveContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TabFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TabFixture.EntryPoint",
            "Run", [new("run", "TabFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.ComponentModel;
        namespace TabFixture;
        sealed class Tabs : PropertyTabAttribute
        {
            public void Types(Type[] types, PropertyTabScope[] scopes) => InitializeArrays(types, scopes);
            public void Names(string[] names, PropertyTabScope[] scopes) => InitializeArrays(names, scopes);
            public string[] NamesCopy => TabClassNames;
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var empty = new PropertyTabAttribute();
                if (empty.TabClasses.Length != 0 || empty.TabScopes.Length != 0) return 1;
                var a = new PropertyTabAttribute(typeof(int));
                var b = new PropertyTabAttribute(typeof(int), PropertyTabScope.Component);
                if (!a.Equals(b) || !a.Equals((object)b) || a.Equals(null) || a.Equals(new object()) || a.GetHashCode() != b.GetHashCode()) return 2;
                if (a.TabClasses[0] != typeof(int) || a.TabScopes[0] != PropertyTabScope.Component) return 3;
                if (a.Equals(new PropertyTabAttribute(typeof(string))) || a.Equals(new PropertyTabAttribute(typeof(int),PropertyTabScope.Document))) return 4;
                var types = new Type[]{typeof(int),typeof(string)};
                var scopes = new PropertyTabScope[]{PropertyTabScope.Document,PropertyTabScope.Component};
                var tabs = new Tabs(); tabs.Types(types, scopes);
                types[0]=typeof(object); scopes[0]=PropertyTabScope.Component;
                if(tabs.TabClasses[0]!=typeof(int) || tabs.TabScopes[0]!=PropertyTabScope.Document)return 5;
                tabs.Types(new Type[]{typeof(string)},null);
                if(tabs.TabScopes.Length!=1 || tabs.TabScopes[0]!=PropertyTabScope.Component)return 6;
                try{new PropertyTabAttribute(typeof(int),PropertyTabScope.Global);return 7;}catch(ArgumentException e){if(e.ParamName!="tabScope")return 8;}
                try{new PropertyTabAttribute("ignored",PropertyTabScope.Static);return 9;}catch(ArgumentException e){if(e.ParamName!="tabScope")return 10;}
                try{tabs.Types(new Type[]{typeof(int)},new PropertyTabScope[0]);return 11;}catch(ArgumentException){}
                try{tabs.Names(new string[]{"name"},new PropertyTabScope[0]);return 12;}catch(ArgumentException){}
                try{tabs.Types(new Type[]{typeof(int)},new PropertyTabScope[]{PropertyTabScope.Global});return 13;}catch(ArgumentException){}
                var names=new string[]{"not-a-type"};var named=new Tabs();named.Names(names,new PropertyTabScope[]{PropertyTabScope.Document});names[0]="changed";
                var copy=named.NamesCopy;if(copy[0]!="not-a-type")return 14;copy[0]="changed";if(named.NamesCopy[0]!="not-a-type")return 15;
                for(var i=0;i<2;i++)try{var ignored=named.TabClasses;return 16;}catch(PlatformNotSupportedException e){if(string.IsNullOrEmpty(e.Message))return 17;}
                if(!named.Equals(named))return 18;
                var nullType=new PropertyTabAttribute((Type)null);if(nullType.TabClasses[0]!=null)return 19;
                var future=new PropertyTabAttribute(typeof(int),(PropertyTabScope)99);if((int)future.TabScopes[0]!=99)return 20;
                return 42;
            }
        }
        """;
}
