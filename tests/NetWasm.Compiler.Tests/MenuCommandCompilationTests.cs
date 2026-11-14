using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class MenuCommandCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CommandStatusAndInvocationPreserveContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("MenuFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "MenuFixture.EntryPoint",
            "Run", [new("run", "MenuFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.ComponentModel.Design;
        using System.Runtime.Serialization;
        namespace MenuFixture;
        sealed class Serialized : CheckoutException { public Serialized():base(null,default(StreamingContext)){} }
        sealed class Custom : MenuCommand
        {
            public int Invoked, Changed;
            public Custom():base(null,null){}
            public override void Invoke(){Invoked++;}
            protected override void OnCommandChanged(EventArgs e){Changed++;base.OnCommandChanged(e);}
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var command=new MenuCommand(null,null);if(command.CommandID!=null || command.OleStatus!=3 || command.Checked || !command.Enabled || !command.Supported || !command.Visible)return 1;
                var notifications=0;var valid=true;EventHandler changed=(sender,e)=>{notifications++;valid&=ReferenceEquals(sender,command)&&ReferenceEquals(e,EventArgs.Empty);};command.CommandChanged+=changed;
                command.Checked=true;command.Checked=true;command.Enabled=false;command.Supported=false;command.Visible=false;
                if(notifications!=4 || !valid || command.OleStatus!=20 || command.ToString()!=" : |Checked")return 2;
                command.CommandChanged-=changed;command.Checked=false;if(notifications!=4)return 3;
                if(!ReferenceEquals(command.Properties,command.Properties))return 4;command.Properties["key"]=42;if((int)command.Properties["key"]!=42)return 5;
                command.Invoke();command.Invoke(new object());
                var calls=0;MenuCommand invoked=null;var id=new CommandID(Guid.Empty,42);invoked=new MenuCommand((sender,e)=>{calls++;valid&=ReferenceEquals(sender,invoked)&&ReferenceEquals(e,EventArgs.Empty);},id);invoked.Invoke();invoked.Invoke(42);if(calls!=2 || !valid || invoked.CommandID!=id)return 6;
                new MenuCommand((s,e)=>throw CheckoutException.Canceled,null).Invoke();
                var exception=new CheckoutException("failed",17);if(exception.ErrorCode!=17 || exception.Message!="failed")return 7;
                try{new MenuCommand((s,e)=>throw exception,null).Invoke();return 8;}catch(CheckoutException e){if(!ReferenceEquals(e,exception))return 9;}
                var equalCode=new CheckoutException("cancel",CheckoutException.Canceled.ErrorCode);try{new MenuCommand((s,e)=>throw equalCode,null).Invoke();return 10;}catch(CheckoutException e){if(!ReferenceEquals(e,equalCode))return 11;}
                var inner=new InvalidOperationException();var outer=new CheckoutException("outer",inner);if(outer.InnerException!=inner || new CheckoutException("text").Message!="text" || new CheckoutException().ErrorCode!=unchecked((int)0x80004005))return 12;
                try{new Serialized();return 13;}catch(PlatformNotSupportedException){}
                var custom=new Custom();custom.Invoke(new object());custom.Checked=true;if(custom.Invoked!=1 || custom.Changed!=1)return 14;
                return 42;
            }
        }
        """;
}
