using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NamedComponentMetadataCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void NamedMetadataPreservesValuesAndReflectionBoundaries(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("NameMetadataFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "NameMetadataFixture.EntryPoint",
            "Run", [new("run", "NameMetadataFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.ComponentModel;
        namespace NameMetadataFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var ev=new DefaultEventAttribute("Changed"); var prop=new DefaultPropertyAttribute("Value");
                if(ev.Name!="Changed" || prop.Name!="Value" || !new DefaultEventAttribute(null).Equals(DefaultEventAttribute.Default) || !new DefaultPropertyAttribute(null).Equals(DefaultPropertyAttribute.Default)) return 1;
                if(!ev.Equals(new DefaultEventAttribute("Changed")) || ev.Equals(new DefaultEventAttribute("Other")) || ev.Equals(prop) || ev.Equals(null) || ev.GetHashCode()!=new DefaultEventAttribute("Changed").GetHashCode()) return 2;
                if(!prop.Equals(new DefaultPropertyAttribute("Value")) || prop.Equals(new DefaultPropertyAttribute("Other")) || prop.Equals(ev) || prop.Equals(null) || prop.GetHashCode()!=new DefaultPropertyAttribute("Value").GetHashCode()) return 3;
                if(new InitializationEventAttribute("Ready").EventName!="Ready" || new InitializationEventAttribute(null).EventName!=null) return 4;
                var provider=new AttributeProviderAttribute("Provider"); var propertyProvider=new AttributeProviderAttribute("Provider","Value");
                if(provider.TypeName!="Provider" || provider.PropertyName!=null || propertyProvider.PropertyName!="Value" || propertyProvider.TypeName!="Provider" || new TypeDescriptionProviderAttribute("Provider").TypeName!="Provider") return 5;
                var supplied=new ProvidePropertyAttribute("Extra","Receiver"); var equal=new ProvidePropertyAttribute("Extra","Receiver");
                if(supplied.PropertyName!="Extra" || supplied.ReceiverTypeName!="Receiver" || !supplied.Equals(equal) || supplied.GetHashCode()!=equal.GetHashCode() || !supplied.Equals(supplied) || supplied.Equals(null) || supplied.Equals(new ProvidePropertyAttribute("Other","Receiver")) || supplied.Equals(new ProvidePropertyAttribute("Extra","Other"))) return 6;
                if(!supplied.TypeId.Equals(new ProvidePropertyAttribute("Extra","Other").TypeId) || supplied.TypeId.Equals(new ProvidePropertyAttribute("Other","Receiver").TypeId)) return 7;
                var nullable=new ProvidePropertyAttribute(null,(string)null); if(nullable.PropertyName!=null || nullable.ReceiverTypeName!=null || !nullable.Equals(new ProvidePropertyAttribute(null,(string)null))) return 8;
                var validation=0;
                try {new AttributeProviderAttribute((string)null);} catch(ArgumentNullException e){if(e.ParamName=="typeName") validation++;}
                try {new AttributeProviderAttribute("Type",null);} catch(ArgumentNullException e){if(e.ParamName=="propertyName") validation++;}
                try {new AttributeProviderAttribute((Type)null);} catch(ArgumentNullException e){if(e.ParamName=="type") validation++;}
                try {new TypeDescriptionProviderAttribute((string)null);} catch(ArgumentNullException e){if(e.ParamName=="typeName") validation++;}
                try {new TypeDescriptionProviderAttribute((Type)null);} catch(ArgumentNullException e){if(e.ParamName=="type") validation++;}
                try {new ProvidePropertyAttribute("Extra",(Type)null);} catch(ArgumentNullException e){if(e.ParamName=="receiverType") validation++;}
                if(validation!=6) return 9;
                // A type's name is its assembly-qualified name, as upstream builds it.
                var qualified=typeof(object).AssemblyQualifiedName;
                if(new AttributeProviderAttribute(typeof(object)).TypeName!=qualified) return 10;
                if(new TypeDescriptionProviderAttribute(typeof(object)).TypeName!=qualified) return 11;
                if(new ProvidePropertyAttribute("Extra",typeof(object)).ReceiverTypeName!=qualified) return 12;
                return 42;
            }
        }
        """;
}
