using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class EnumConverterCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EnumParsingAndSuppliedValuesPreserveContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("EnumFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EnumFixture.EntryPoint",
            "Run", [new("run", "EnumFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EnumAttributeMetadataBoundariesAreExplicit(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("EnumFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EnumFixture.EntryPoint",
            "RunBoundary", [new("run", "EnumFixture.EntryPoint", "RunBoundary")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.ComponentModel;
        using System.Collections;
        namespace EnumFixture;
        enum Plain { Zero, One, Two }
        [Flags] enum Bits { None=0, A=1, B=2 }
        [Flags] enum Wide : ulong { Low=1, High=0x8000000000000000UL }
        sealed class Supplied : EnumConverter
        {
            public Supplied(Type type) : base(type) {}
            public Type Target=>EnumType;
            public void Supply(object[] values)=>Values=new StandardValuesCollection(values);
            public int Compare(object a,object b)=>Comparer.Compare(a,b);
        }
        public static class EntryPoint
        {
            public static int RunBoundary(int input)
            {
                var plain=new EnumConverter(typeof(Plain)); var bits=new EnumConverter(typeof(Bits)); var boundaries=0;
                try{plain.ConvertTo(null,null,Plain.One,typeof(string));}catch(PlatformNotSupportedException){boundaries++;}
                try{bits.ConvertTo(null,null,Bits.A|Bits.B,typeof(Enum[]));}catch(PlatformNotSupportedException){boundaries++;}
                try{plain.GetStandardValuesExclusive(null);}catch(PlatformNotSupportedException){boundaries++;}
                try{plain.ConvertTo(null,null,Plain.One,typeof(System.ComponentModel.Design.Serialization.InstanceDescriptor));}catch(PlatformNotSupportedException){boundaries++;}
                return boundaries==4?42:100+boundaries;
            }
            public static int Run(int input)
            {
                var plain=new Supplied(typeof(Plain)); var bits=new EnumConverter(typeof(Bits)); var wide=new EnumConverter(typeof(Wide));
                if(plain.Target!=typeof(Plain) || !plain.CanConvertFrom(null,typeof(string)) || !plain.CanConvertFrom(null,typeof(Enum[])) || !plain.CanConvertTo(null,typeof(Enum[])) || !plain.GetStandardValuesSupported(null))return 1;
                if((Plain)plain.ConvertFrom(null,null,"one")!=Plain.One || (Bits)bits.ConvertFrom(null,null,"A, B")!=(Bits.A|Bits.B) || (Wide)wide.ConvertFrom(null,null,"High, Low")!=(Wide.High|Wide.Low))return 2;
                if((Bits)bits.ConvertFrom(null,null,new Enum[]{Bits.A,Bits.B})!=(Bits.A|Bits.B) || (Wide)wide.ConvertFrom(null,null,new Enum[]{Wide.High,Wide.Low})!=(Wide.High|Wide.Low))return 3;
                if(!plain.IsValid(null,Plain.One) || plain.IsValid(null,(Plain)123))return 4;
                plain.Supply(new object[]{Plain.Two,Plain.One}); var standard=plain.GetStandardValues(null);
                if(standard.Count!=2 || (Plain)standard[0]!=Plain.Two || !ReferenceEquals(standard,plain.GetStandardValues(null)) || plain.Compare(Plain.One,Plain.Two)>=0)return 5;
                var validation=0;
                try{new EnumConverter(typeof(int));}catch(ArgumentException){validation++;}
                try{plain.ConvertFrom(null,null,"unknown");}catch(FormatException e){if(e.InnerException is ArgumentException)validation++;}
                try{bits.GetStandardValues(null);}catch(PlatformNotSupportedException){validation++;}
                return validation==3?42:6;
            }
        }
        """;
}
