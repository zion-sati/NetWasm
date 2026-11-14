using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AmbientValueCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TypedAmbientValuesAndDeferredConversionPreserveContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("AmbientFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "AmbientFixture.EntryPoint",
            "Run", [new("run", "AmbientFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.ComponentModel;
        namespace AmbientFixture;
        sealed class Payload
        {
            public readonly int Number;
            public Payload(int number){Number=number;}
            public override bool Equals(object value)=>value is Payload p && p.Number==Number;
            public override int GetHashCode()=>Number;
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var attrs=new AmbientValueAttribute[]{new AmbientValueAttribute('x'),new AmbientValueAttribute((byte)2),new AmbientValueAttribute((short)-3),new AmbientValueAttribute(4),new AmbientValueAttribute(5L),new AmbientValueAttribute(1.5f),new AmbientValueAttribute(2.5d),new AmbientValueAttribute(true),new AmbientValueAttribute("text"),new AmbientValueAttribute((object)null)};
                var values=new object[]{'x',(byte)2,(short)-3,4,5L,1.5f,2.5d,true,"text",null};
                for(var i=0;i<attrs.Length;i++)
                {
                    var equal=new AmbientValueAttribute(values[i]);
                    if(!object.Equals(attrs[i].Value,values[i]) || !attrs[i].Equals(attrs[i]) || !attrs[i].Equals(equal) || attrs[i].GetHashCode()!=equal.GetHashCode() || attrs[i].Equals(null) || attrs[i].Equals(new object()))return 1;
                }
                if(new AmbientValueAttribute(1).Equals(new AmbientValueAttribute(1L)) || new AmbientValueAttribute((string)null).Value!=null || !new AmbientValueAttribute((string)null).Equals(new AmbientValueAttribute((object)null)))return 2;
                var payload=new Payload(42);var attribute=new AmbientValueAttribute(payload);if(!ReferenceEquals(attribute.Value,payload) || !attribute.Equals(new AmbientValueAttribute(new Payload(42))) || attribute.Equals(new AmbientValueAttribute(new Payload(43))) || attribute.GetHashCode()!=42)return 3;
                var unavailable=new AmbientValueAttribute(typeof(int),"42");var boundaries=0;
                try{var unused=unavailable.Value;}catch(PlatformNotSupportedException){boundaries++;}
                try{unavailable.Equals(new AmbientValueAttribute(42));}catch(PlatformNotSupportedException){boundaries++;}
                try{unavailable.GetHashCode();}catch(PlatformNotSupportedException){boundaries++;}
                return boundaries==3?42:4;
            }
        }
        """;
}
