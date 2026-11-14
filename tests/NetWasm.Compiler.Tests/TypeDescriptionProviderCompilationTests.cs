using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TypeDescriptionProviderCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CallerProvidersPreserveForwardingAndDefaultContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ProviderFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ProviderFixture.EntryPoint",
            "Run", [new("run", "ProviderFixture.EntryPoint", "Run")], Target: target,
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
        namespace ProviderFixture;
        sealed class Constructed { public int Value=42; public Constructed(){} }
        sealed class Descriptor : CustomTypeDescriptor { public override string GetComponentName()=>"component"; }
        sealed class Extender : IExtenderProvider { public bool CanExtend(object value)=>true; }
        sealed class Parent : TypeDescriptionProvider
        {
            public readonly object Created=new object();
            public readonly IDictionary Cache=new Hashtable();
            public readonly ICustomTypeDescriptor Descriptor=new Descriptor();
            public readonly IExtenderProvider[] Extenders=new IExtenderProvider[]{new Extender()};
            public Type SeenType; public object SeenInstance; public Type[] SeenArgs; public object[] SeenValues;
            public override object CreateInstance(IServiceProvider provider,Type type,Type[] args,object[] values){SeenType=type;SeenArgs=args;SeenValues=values;return Created;}
            public override IDictionary GetCache(object instance){SeenInstance=instance;return Cache;}
            public override ICustomTypeDescriptor GetTypeDescriptor(Type type,object instance){SeenType=type;SeenInstance=instance;return Descriptor;}
            public override ICustomTypeDescriptor GetExtendedTypeDescriptor(object instance){SeenInstance=instance;return Descriptor;}
            public override Type GetReflectionType(Type type,object instance){SeenType=type;SeenInstance=instance;return typeof(string);}
            public override Type GetRuntimeType(Type type)=>typeof(int);
            public override bool IsSupportedType(Type type)=>type!=typeof(int);
            protected override IExtenderProvider[] GetExtenderProviders(object instance){SeenInstance=instance;return Extenders;}
        }
        sealed class Child : TypeDescriptionProvider
        {
            public Child(){}
            public Child(TypeDescriptionProvider parent):base(parent){}
            public IExtenderProvider[] Extenders(object value)=>GetExtenderProviders(value);
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var empty=new Child();var instance=new object();
                var descriptor=empty.GetTypeDescriptor(typeof(object));
                if(descriptor.GetProperties().Count!=0 || descriptor.GetEvents().Count!=0 || descriptor.GetAttributes().Count!=0 || descriptor.GetComponentName()!=null || empty.GetFullComponentName(instance)!=null)return 1;
                if(!ReferenceEquals(descriptor,empty.GetTypeDescriptor(instance)) || !ReferenceEquals(descriptor,empty.GetExtendedTypeDescriptor(null)) || ReferenceEquals(descriptor,new Child().GetTypeDescriptor(instance)))return 2;
                if(empty.GetCache(null)!=null || empty.Extenders(instance).Length!=0 || empty.GetReflectionType(typeof(object))!=typeof(object) || empty.GetReflectionType(instance)!=typeof(object) || empty.GetReflectionType((Type)null)!=null || !empty.IsSupportedType(typeof(object)))return 3;
                var parent=new Parent();var child=new Child(parent);var types=new Type[]{typeof(int)};var values=new object[]{42};
                if(child.CreateInstance(null,typeof(object),types,values)!=parent.Created || parent.SeenType!=typeof(object) || !ReferenceEquals(parent.SeenArgs,types) || !ReferenceEquals(parent.SeenValues,values))return 4;
                if(child.GetCache(instance)!=parent.Cache || parent.SeenInstance!=instance || child.GetTypeDescriptor(instance)!=parent.Descriptor || parent.SeenType!=typeof(object) || child.GetExtendedTypeDescriptor(instance)!=parent.Descriptor || child.GetFullComponentName(instance)!="component")return 5;
                if(child.GetReflectionType(instance)!=typeof(string) || parent.SeenInstance!=instance || child.GetReflectionType(typeof(int))!=typeof(string) || parent.SeenType!=typeof(int) || parent.SeenInstance!=null || child.GetRuntimeType(null)!=typeof(int))return 6;
                if(child.IsSupportedType(typeof(int)) || !child.IsSupportedType(typeof(string)) || !ReferenceEquals(child.Extenders(instance),parent.Extenders))return 7;
                var validation=0;
                try{empty.GetTypeDescriptor((object)null);}catch(ArgumentNullException e){if(e.ParamName=="instance")validation++;}
                try{empty.GetReflectionType((object)null);}catch(ArgumentNullException e){if(e.ParamName=="instance")validation++;}
                try{empty.Extenders(null);}catch(ArgumentNullException e){if(e.ParamName=="instance")validation++;}
                try{empty.IsSupportedType(null);}catch(ArgumentNullException e){if(e.ParamName=="type")validation++;}
                try{empty.GetRuntimeType(null);}catch(ArgumentNullException e){if(e.ParamName=="reflectionType")validation++;}
                try{empty.CreateInstance(null,null,null,null);}catch(ArgumentNullException e){if(e.ParamName=="objectType")validation++;}
                if(validation!=6)return 8;
                if(Activator.CreateInstance<int>()!=0 || Activator.CreateInstance<Constructed>().Value!=42)return 10;
                var activationNull=0;try{Activator.CreateInstance((Type)null,new object[0]);}catch(ArgumentNullException e){if(e.ParamName=="type")activationNull++;}if(activationNull!=1)return 11;
                // A runtime type is its own runtime type, as on .NET 11: the type table answers Type.Assembly.
                if(!ReferenceEquals(empty.GetRuntimeType(typeof(object)),typeof(object)))return 12;
                var boundaries=0;
                try{empty.CreateInstance(null,typeof(object),null,null);}catch(PlatformNotSupportedException){boundaries++;}
                return boundaries==1?42:9;
            }
        }
        """;
}
