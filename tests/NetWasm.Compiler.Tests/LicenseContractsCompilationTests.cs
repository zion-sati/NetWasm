using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class LicenseContractsCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CallerLicenseContractsAndExceptionsPreserveBehavior(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("LicenseFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "LicenseFixture.EntryPoint",
            "Run", [new("run", "LicenseFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.ComponentModel;
        using System.Reflection;
        using System.Runtime.Serialization;
        #pragma warning disable SYSLIB0051
        namespace LicenseFixture;
        sealed class Token : License
        {
            public int Releases;
            public override string LicenseKey=>"key";
            public override void Dispose()=>Releases++;
        }
        sealed class Context : LicenseContext
        {
            public Type SavedType;
            public string Key;
            public override LicenseUsageMode UsageMode=>LicenseUsageMode.Designtime;
            public override void SetSavedLicenseKey(Type type,string key){SavedType=type;Key=key;}
            public override string GetSavedLicenseKey(Type type,Assembly assembly)=>type==SavedType?Key:null;
            public override object GetService(Type type)=>type==typeof(string)?Key:null;
        }
        sealed class Provider : LicenseProvider
        {
            public override License GetLicense(LicenseContext context,Type type,object instance,bool allowExceptions)
            {
                if(context.GetSavedLicenseKey(type,null)=="key")return new Token();
                if(allowExceptions)throw new LicenseException(type,instance,"denied");
                return null;
            }
        }
        sealed class Legacy : LicenseException{public Legacy():base(null,default(StreamingContext)){} }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var plain=new LicenseContext();plain.SetSavedLicenseKey(typeof(object),"unused");
                if(plain.UsageMode!=LicenseUsageMode.Runtime || plain.GetSavedLicenseKey(typeof(object),null)!=null || plain.GetService(typeof(string))!=null)return 1;
                LicenseContext context=new Context();context.SetSavedLicenseKey(typeof(object),"key");IServiceProvider services=context;
                if(context.UsageMode!=LicenseUsageMode.Designtime || context.GetSavedLicenseKey(typeof(object),null)!="key" || (string)services.GetService(typeof(string))!="key" || context.GetSavedLicenseKey(typeof(int),null)!=null)return 2;
                LicenseProvider provider=new Provider();var token=(Token)provider.GetLicense(context,typeof(object),new object(),true);
                if(token.LicenseKey!="key" || provider.GetLicense(plain,typeof(object),null,false)!=null)return 3;
                ((IDisposable)token).Dispose();if(token.Releases!=1)return 4;
                var denied=0;try{provider.GetLicense(plain,typeof(object),null,true);}catch(LicenseException e){if(e.LicensedType==typeof(object) && e.Message=="denied")denied++;}if(denied!=1)return 5;
                var inner=new Exception("cause");var error=new LicenseException(typeof(object),token,"message",inner);
                if(error.LicensedType!=typeof(object) || error.Message!="message" || error.InnerException!=inner || error.HResult!=unchecked((int)0x80131901))return 6;
                if(new LicenseException(typeof(object)).LicensedType!=typeof(object) || !new LicenseException(typeof(object),token).Message.Contains(typeof(object).FullName) || new LicenseException(null).LicensedType!=null)return 7;
                var attribute=new LicenseProviderAttribute(typeof(Provider));var equal=new LicenseProviderAttribute(typeof(Provider));
                if(attribute.LicenseProvider!=typeof(Provider) || !attribute.Equals(equal) || attribute.GetHashCode()!=equal.GetHashCode() || attribute.Equals(LicenseProviderAttribute.Default) || attribute.Equals(null) || !attribute.TypeId.Equals(equal.TypeId))return 8;
                if(LicenseProviderAttribute.Default.LicenseProvider!=null || !new LicenseProviderAttribute((string)null).Equals(LicenseProviderAttribute.Default) || new LicenseProviderAttribute((Type)null).LicenseProvider!=null)return 9;
                var boundaries=0;
                try{var unused=new LicenseProviderAttribute("Missing.Provider").LicenseProvider;}catch(PlatformNotSupportedException){boundaries++;}
                try{error.GetObjectData(null,default(StreamingContext));}catch(PlatformNotSupportedException){boundaries++;}
                try{new Legacy();}catch(PlatformNotSupportedException){boundaries++;}
                return boundaries==3?42:10;
            }
        }
        """;
}
