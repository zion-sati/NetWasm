using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ThreadContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ThreadReactorContractsAndHostRejections(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ThreadFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ThreadFixture.EntryPoint",
            "Run", [new("run", "ThreadFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Threading;
        using System.Globalization;
        using System.Security.Principal;
        #pragma warning disable SYSLIB0006, SYSLIB0003, SYSLIB0054, CS0618
        namespace ThreadFixture;
        public static class EntryPoint
        {
         public static int Run(int input)
         {
          var result = Portable(input); if(result != 42) return result;
          var t = Thread.CurrentThread; var invoked = false;
          var actions = new Action[] {
           () => new Thread(() => invoked = true), () => new Thread(() => invoked = true, 0),
           () => new Thread(_ => invoked = true), () => new Thread(_ => invoked = true, 0),
           () => t.Start(), () => t.Start(null), () => t.Join(), () => t.Join(0), () => t.Join(TimeSpan.Zero),
           () => Thread.Sleep(Timeout.Infinite),
           () => t.Interrupt(), () => t.Abort(), () => t.Abort(null), () => Thread.ResetAbort(), () => t.Suspend(), () => t.Resume(),
           () => t.DisableComObjectEagerCleanup(), () => t.GetCompressedStack(), () => t.SetCompressedStack(null!),
           () => t.IsBackground = true, () => t.Priority = ThreadPriority.Normal, () => { _ = t.Priority; },
           () => t.SetApartmentState(ApartmentState.MTA), () => t.TrySetApartmentState(ApartmentState.Unknown), () => t.ApartmentState = ApartmentState.STA
          };
          var rejected = 0; foreach(var action in actions) { try { action(); return 70; } catch(PlatformNotSupportedException e) { if(!e.Message.Contains("Thread")) return 71; rejected++; } }
          // A finite sleep blocks on the platform clock (ThreadSleepBlocksWithoutRunningTheEventLoopInWasmtime); a zero
          // sleep returns at once, there is no other thread to yield to, and spinning returns.
          Thread.Sleep(0); Thread.Sleep(TimeSpan.Zero); Thread.SpinWait(1);
          if(Thread.Yield()) return 73;
          if(rejected != actions.Length || invoked || t.ManagedThreadId != 1 || Thread.GetCurrentProcessorId() != 0 || t.IsBackground || t.GetApartmentState() != ApartmentState.Unknown) return 72;
          return input + 1;
         }
         public static int Portable(int input)
         {
          var t = Thread.CurrentThread;
          if(!ReferenceEquals(t, Thread.CurrentThread) || t.ManagedThreadId != Environment.CurrentManagedThreadId || t.GetHashCode() != t.ManagedThreadId || !t.IsAlive || t.IsThreadPoolThread) return 1;
          if(t.ThreadState != ThreadState.Running || !ReferenceEquals(Thread.GetDomain(), AppDomain.CurrentDomain) || Thread.GetDomainID() != 1) return 2;
          var name=t.Name; t.Name="qualification"; if(t.Name!="qualification") return 3; t.Name=name;
          if(!ReferenceEquals(t.CurrentCulture,CultureInfo.CurrentCulture) || !ReferenceEquals(t.CurrentUICulture,CultureInfo.CurrentUICulture)) return 4;
          t.CurrentCulture=CultureInfo.InvariantCulture; t.CurrentUICulture=CultureInfo.InvariantCulture;
          if(t.ExecutionContext==null) return 5;
          using(ExecutionContext.SuppressFlow()) { if(t.ExecutionContext!=null) return 6; }
          var old=Thread.CurrentPrincipal; var first=new Principal(); var second=new Principal();
          Thread.CurrentPrincipal=first; var captured=ExecutionContext.Capture()!; Thread.CurrentPrincipal=second;
          var matched=false; ExecutionContext.Run(captured, _ => matched=ReferenceEquals(Thread.CurrentPrincipal,first),null);
          if(!matched || !ReferenceEquals(Thread.CurrentPrincipal,second)) return 7; Thread.CurrentPrincipal=old;
          var slot=Thread.AllocateDataSlot(); var marker=new object(); if(Thread.GetData(slot)!=null) return 8;
          Thread.SetData(slot,marker); if(!ReferenceEquals(Thread.GetData(slot),marker)) return 9;
          const string key="NetWasm.Qualification.ThreadSlot"; Thread.FreeNamedDataSlot(key);
          var named=Thread.AllocateNamedDataSlot(key); Thread.SetData(named,marker);
          if(!ReferenceEquals(named,Thread.GetNamedDataSlot(key))) return 10;
          try { Thread.AllocateNamedDataSlot(key); return 11; } catch(ArgumentException) { }
          Thread.FreeNamedDataSlot(key); var replacement=Thread.GetNamedDataSlot(key);
          if(ReferenceEquals(named,replacement) || Thread.GetData(replacement)!=null || !ReferenceEquals(Thread.GetData(named),marker)) return 12;
          Thread.FreeNamedDataSlot(key);
          try { Thread.GetData(null!); return 13; } catch(ArgumentNullException) { }
          try { Thread.SetData(null!,null); return 14; } catch(ArgumentNullException) { }
          try { Thread.AllocateNamedDataSlot(null!); return 15; } catch(ArgumentNullException) { }
          try { new Thread((ThreadStart)null!, -1); return 16; } catch(ArgumentNullException) { }
          try { new Thread((ParameterizedThreadStart)null!, -1); return 17; } catch(ArgumentNullException) { }
          try { new Thread(() => {}, -1); return 18; } catch(ArgumentOutOfRangeException) { }
          try { new Thread(_ => {}, -1); return 19; } catch(ArgumentOutOfRangeException) { }
          try { Thread.Sleep(-2); return 20; } catch(ArgumentOutOfRangeException) { }
          try { t.Join(TimeSpan.FromDays(30)); return 21; } catch(ArgumentOutOfRangeException) { }
          try { t.TrySetApartmentState((ApartmentState)99); return 22; } catch(ArgumentOutOfRangeException) { }
          Thread.BeginCriticalRegion(); Thread.EndCriticalRegion(); Thread.BeginThreadAffinity(); Thread.EndThreadAffinity(); Thread.MemoryBarrier(); Thread.SpinWait(0); Thread.SpinWait(-1);
          byte value0=default; Thread.VolatileWrite(ref value0,7); if(Thread.VolatileRead(ref value0) != 7) return 35;
          sbyte value1=default; Thread.VolatileWrite(ref value1,-7); if(Thread.VolatileRead(ref value1) != -7) return 36;
          short value2=default; Thread.VolatileWrite(ref value2,-300); if(Thread.VolatileRead(ref value2) != -300) return 37;
          ushort value3=default; Thread.VolatileWrite(ref value3,300); if(Thread.VolatileRead(ref value3) != 300) return 38;
          int value4=default; Thread.VolatileWrite(ref value4,-1234); if(Thread.VolatileRead(ref value4) != -1234) return 39;
          uint value5=default; Thread.VolatileWrite(ref value5,1234); if(Thread.VolatileRead(ref value5) != 1234) return 40;
          long value6=default; Thread.VolatileWrite(ref value6,-123456); if(Thread.VolatileRead(ref value6) != -123456) return 41;
          ulong value7=default; Thread.VolatileWrite(ref value7,123456); if(Thread.VolatileRead(ref value7) != 123456) return 42;
          float value8=default; Thread.VolatileWrite(ref value8,1.5f); if(Thread.VolatileRead(ref value8) != 1.5f) return 43;
          double value9=default; Thread.VolatileWrite(ref value9,2.5); if(Thread.VolatileRead(ref value9) != 2.5) return 44;
          IntPtr value10=default; Thread.VolatileWrite(ref value10,new IntPtr(17)); if(Thread.VolatileRead(ref value10) != new IntPtr(17)) return 45;
          UIntPtr value11=default; Thread.VolatileWrite(ref value11,new UIntPtr(18)); if(Thread.VolatileRead(ref value11) != new UIntPtr(18)) return 46;
          object? value12=default; Thread.VolatileWrite(ref value12,marker); if(Thread.VolatileRead(ref value12) != marker) return 47;
          return input+1;
         }
        }
        public sealed class Principal : IPrincipal { public IIdentity? Identity => null; public bool IsInRole(string role)=>false; }
        """;
}
