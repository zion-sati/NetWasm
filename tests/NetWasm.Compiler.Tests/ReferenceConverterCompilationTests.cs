using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ReferenceConverterCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CallerReferenceServicesAndContainersPreserveConversion(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReferenceFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ReferenceFixture.EntryPoint",
            "Run", [new("run", "ReferenceFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.ComponentModel;
        using System.ComponentModel.Design;
        using System.Runtime.InteropServices;
        namespace ReferenceFixture;
        sealed class Context : ITypeDescriptorContext, IReferenceService
        {
            public IContainer Container {get;set;}
            public object Instance=>null;
            public PropertyDescriptor PropertyDescriptor=>null;
            public bool Service;
            public Component Item;
            public Type Requested;
            public object GetService(Type t)=>Service && t==typeof(IReferenceService)?this:null;
            public bool OnComponentChanging()=>true;
            public void OnComponentChanged(){}
            public IComponent GetComponent(object value)=>value as IComponent;
            public object GetReference(string name)=>name=="service"?Item:null;
            public string GetName(object value)=>ReferenceEquals(value,Item)?"service":null;
            public object[] GetReferences()=>new object[]{Item};
            public object[] GetReferences(Type type){Requested=type;return GetReferences();}
        }
        sealed class Filtered : ReferenceConverter
        {
            public Filtered():base(typeof(Component)){}
            protected override bool IsValueAllowed(ITypeDescriptorContext context,object value)=>((IComponent)value).Site.Name!="Z";
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var container=new Container(); var a=new Component(); var z=new Component();container.Add(z,"Z");container.Add(a,"A");
                var context=new Context{Container=container,Item=a}; var converter=new ReferenceConverter(typeof(Component));
                if(!converter.CanConvertFrom(context,typeof(string)) || converter.CanConvertFrom(null,typeof(string)) || converter.ConvertFrom(context,null,"  A ")!=a || converter.ConvertFrom(context,null,"missing")!=null || converter.ConvertFrom(context,null,"(none)")!=null || converter.ConvertFrom(null,null,"A")!=null)return 1;
                if((string)converter.ConvertTo(context,null,a,typeof(string))!="A" || (string)converter.ConvertTo(null,null,new object(),typeof(string))!="" || (string)converter.ConvertTo(null,null,null,typeof(string))!="(none)")return 2;
                var values=converter.GetStandardValues(context); if(values.Count!=3 || values[0]!=null || values[1]!=a || values[2]!=z || converter.GetStandardValues(null).Count!=0 || !converter.GetStandardValuesExclusive(context) || !converter.GetStandardValuesSupported(context))return 3;
                values=new Filtered().GetStandardValues(context);if(values.Count!=2 || values[1]!=a)return 4;
                context.Service=true;
                if(converter.ConvertFrom(context,null,"service")!=a || converter.ConvertFrom(context,null,"Z")!=z || (string)converter.ConvertTo(context,null,a,typeof(string))!="service" || (string)converter.ConvertTo(context,null,z,typeof(string))!="Z")return 5;
                values=converter.GetStandardValues(context);if(values.Count!=2 || values[1]!=a || context.Requested!=typeof(Component))return 6;
                IReferenceService service=context; if(service.GetComponent(a)!=a || service.GetReferences().Length!=1 || service.GetName(new object())!=null)return 7;
                if(Marshal.IsComObject(a) || Marshal.IsComObject(new object()))return 8;
                var errors=0;
                try{Marshal.IsComObject(null);}catch(ArgumentNullException e){if(e.ParamName=="o")errors++;}
                try{converter.ConvertTo(context,null,a,null);}catch(ArgumentNullException){errors++;}
                var componentConverter=new ComponentConverter(typeof(Component));if(!componentConverter.GetPropertiesSupported(null) || (string)componentConverter.ConvertTo(null,null,a,typeof(string))!="A")return 9;
                try{componentConverter.GetProperties(null,a,null);}catch(PlatformNotSupportedException){errors++;}
                container.Dispose();return errors==3?42:10;
            }
        }
        """;
}
