using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ComponentFilterMetadataCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void FilterMetadataAndCallerOverridesPreserveContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("FilterFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "FilterFixture.EntryPoint",
            "Run", [new("run", "FilterFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.ComponentModel;
        namespace FilterFixture;
        sealed class Installer : InstallerTypeAttribute { public Installer():base("unused"){} public override Type InstallerType=>typeof(object); }
        sealed class Supplied : ExpandableObjectConverter
        {
            public readonly PropertyDescriptorCollection Values=new PropertyDescriptorCollection(new PropertyDescriptor[0]);
            public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context,object value,Attribute[] attrs)=>Values;
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var first=new ToolboxItemFilterAttribute("group");var same=new ToolboxItemFilterAttribute("group",ToolboxItemFilterType.Allow);var require=new ToolboxItemFilterAttribute("group",ToolboxItemFilterType.Require);
                if(first.FilterString!="group" || first.FilterType!=ToolboxItemFilterType.Allow || !first.Equals(first) || !first.Equals(same) || first.Equals(require) || first.Equals(null) || first.Equals(new object()))return 1;
                if(first.GetHashCode()!=same.GetHashCode() || !first.Match(require) || first.Match(new ToolboxItemFilterAttribute("other")) || first.Match(null) || first.Match(new object()))return 2;
                if(!first.TypeId.Equals(require.TypeId) || !ReferenceEquals(first.TypeId,first.TypeId) || first.TypeId.Equals(new ToolboxItemFilterAttribute("other").TypeId))return 3;
                if(new ToolboxItemFilterAttribute(null).FilterString!="" || first.ToString()!="group,Allow" || require.ToString()!="group,Require")return 4;
                var names=new string[]{"Allow","Custom","Prevent","Require"};for(var i=0;i<4;i++)if(new ToolboxItemFilterAttribute("g",(ToolboxItemFilterType)i).ToString()!="g,"+names[i])return 5;
                if(new ToolboxItemFilterAttribute("g",(ToolboxItemFilterType)123).ToString()!="g,")return 6;
                var installer=new InstallerTypeAttribute("Missing.Installer");var other=new InstallerTypeAttribute("Missing.Installer");
                if(!installer.Equals(installer) || !installer.Equals(other) || installer.GetHashCode()!=other.GetHashCode() || installer.Equals(new InstallerTypeAttribute("Other")) || installer.Equals(null) || !new InstallerTypeAttribute((string)null).Equals(new InstallerTypeAttribute((string)null)))return 7;
                InstallerTypeAttribute suppliedType=new Installer();if(suppliedType.InstallerType!=typeof(object))return 8;
                var converter=new ExpandableObjectConverter();var supplied=new Supplied();TypeConverter polymorphic=supplied;
                if(!converter.GetPropertiesSupported(null) || !ReferenceEquals(polymorphic.GetProperties(null,new object(),null),supplied.Values))return 9;
                var validation=0;try{new InstallerTypeAttribute((Type)null);}catch(ArgumentNullException e){if(e.ParamName=="installerType")validation++;}if(validation!=1)return 10;
                // A type's assembly-qualified name is its type-table name (issue 225), so the attribute takes a type;
                // looking an installer up by name is what NetWasm does not do.
                if(!new InstallerTypeAttribute(typeof(object)).Equals(new InstallerTypeAttribute(typeof(object))))return 12;
                var boundaries=0;
                try{var value=installer.InstallerType;}catch(PlatformNotSupportedException){boundaries++;}
                try{converter.GetProperties(null,new object(),null);}catch(PlatformNotSupportedException){boundaries++;}
                return boundaries==2?42:11;
            }
        }
        """;
}
