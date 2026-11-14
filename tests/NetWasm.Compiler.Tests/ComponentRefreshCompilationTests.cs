using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ComponentRefreshCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RefreshPayloadsAndExceptionsPreserveContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("RefreshFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "RefreshFixture.EntryPoint",
            "Run", [new("run", "RefreshFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.ComponentModel;
        using System.Runtime.Serialization;
        #pragma warning disable SYSLIB0051
        namespace RefreshFixture;
        sealed class LegacyWarning : WarningException { public LegacyWarning() : base(null,default(StreamingContext)) {} }
        sealed class LegacyAsync : InvalidAsynchronousStateException { public LegacyAsync() : base(null,default(StreamingContext)) {} }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var item=new object(); var args=new RefreshEventArgs(item);
                if(!ReferenceEquals(args.ComponentChanged,item) || args.TypeChanged!=typeof(object)) return 1;
                var type=new RefreshEventArgs(typeof(string)); if(type.ComponentChanged!=null || type.TypeChanged!=typeof(string)) return 2;
                if(new RefreshEventArgs((object)null).TypeChanged!=null || new RefreshEventArgs((Type)null).ComponentChanged!=null) return 3;
                var notifications=0; RefreshEventHandler handler=e=>{if(ReferenceEquals(e,args)) notifications++;}; handler(args); if(notifications!=1) return 4;
                for(var i=-1;i<4;i++)
                {
                    var value=new RefreshPropertiesAttribute((RefreshProperties)i); var same=new RefreshPropertiesAttribute((RefreshProperties)i);
                    if((int)value.RefreshProperties!=i || !value.Equals(same) || value.GetHashCode()!=same.GetHashCode() || value.Equals(null) || value.Equals(new object()) || value.Equals(new RefreshPropertiesAttribute((RefreshProperties)(i+1))) || value.IsDefaultAttribute()!=(i==0)) return 5;
                }
                if(RefreshPropertiesAttribute.All.RefreshProperties!=RefreshProperties.All || RefreshPropertiesAttribute.Repaint.RefreshProperties!=RefreshProperties.Repaint || RefreshPropertiesAttribute.Default.RefreshProperties!=RefreshProperties.None) return 6;
                var inner=new Exception("cause"); var warning=new WarningException("warning",inner); if(warning.Message!="warning" || !ReferenceEquals(warning.InnerException,inner) || warning.HelpUrl!=null || warning.HelpTopic!=null || !(warning is SystemException)) return 7;
                var help=new WarningException("warning","https://example.invalid/help","topic"); if(help.HelpUrl!="https://example.invalid/help" || help.HelpTopic!="topic" || help.Message!="warning") return 8;
                if(new WarningException().HelpUrl!=null || new WarningException("warning").HelpTopic!=null || new WarningException("warning","help").HelpTopic!=null) return 9;
                var asyncError=new InvalidAsynchronousStateException("state",inner); if(asyncError.Message!="state" || !ReferenceEquals(asyncError.InnerException,inner) || asyncError.ParamName!=null || !(asyncError is ArgumentException)) return 10;
                if(new InvalidAsynchronousStateException("state").Message!="state" || new InvalidAsynchronousStateException().InnerException!=null) return 11;
                var caught=0; try{throw warning;}catch(WarningException e){if(ReferenceEquals(e,warning)) caught++;} try{throw asyncError;}catch(ArgumentException e){if(ReferenceEquals(e,asyncError)) caught++;} if(caught!=2)return 12;
                var unsupported=0;
                try{help.GetObjectData(null,default(StreamingContext));}catch(PlatformNotSupportedException){unsupported++;}
                try{new LegacyWarning();}catch(PlatformNotSupportedException){unsupported++;}
                try{new LegacyAsync();}catch(PlatformNotSupportedException){unsupported++;}
                return unsupported==3?42:13;
            }
        }
        """;
}
