using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ThreadPoolContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ThreadPoolRunsWorkOnOneThread(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "ThreadPoolFixture", Source, "ThreadPoolFixture.EntryPoint", false, target, 41, [])
        {
            AllowUnsafe = true,
            Optimization = FinalWasmOptimization.None,
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/platform@1.0.0",
            ObserveMethodName = "Observe",
        }));
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using System.Runtime.InteropServices;
        #pragma warning disable CS0618
        namespace ThreadPoolFixture;
        public static class EntryPoint
        {
         private static Task<int>? _operation;
         private static readonly AsyncLocal<int> Flow = new();
         public static int Run(int input) { _operation = Exercise(input); return 0; }
         public static int Observe(int input) => _operation is { IsCompletedSuccessfully: true } ? _operation.Result : 99;
         private sealed class WorkItem(List<string> order, TaskCompletionSource done) : IThreadPoolWorkItem
         {
          public void Execute() { order.Add("e" + Flow.Value); done.SetResult(); }
         }
         // One thread, as upstream's ThreadPool.Wasi.cs: work runs in order once the current work returns, and
         // only the safe overloads flow the ExecutionContext.
         public static async Task<int> Exercise(int input)
         {
          var portable=Portable(input); if(portable!=42) return portable;
          ThreadPool.GetMinThreads(out var workers,out var completion); if(workers!=1 || completion!=1) return 20;
          ThreadPool.GetMaxThreads(out workers,out completion); if(workers!=1 || completion!=1) return 21;
          ThreadPool.GetAvailableThreads(out workers,out completion); if(workers!=1 || completion!=1) return 22;
          if(!ThreadPool.SetMinThreads(1,1) || ThreadPool.SetMinThreads(2,1) || !ThreadPool.SetMaxThreads(1,1) || ThreadPool.SetMaxThreads(1,4)) return 23;
          if(ThreadPool.ThreadCount!=1 || ThreadPool.CompletedWorkItemCount!=0 || ThreadPool.PendingWorkItemCount!=0) return 24;
          var order=new List<string>(); var done=new TaskCompletionSource();
          Flow.Value=7;
          if(!ThreadPool.QueueUserWorkItem(s=>order.Add("a"+s+Flow.Value),1)) return 25;
          if(!ThreadPool.QueueUserWorkItem<int>(s=>order.Add("b"+s+Flow.Value),2,false)) return 26;
          if(!ThreadPool.UnsafeQueueUserWorkItem(s=>order.Add("c"+s+Flow.Value),3)) return 27;
          if(!ThreadPool.UnsafeQueueUserWorkItem<int>(s=>order.Add("d"+s+Flow.Value),4,true)) return 28;
          if(!ThreadPool.UnsafeQueueUserWorkItem(new WorkItem(order,done),false)) return 29;
          if(order.Count!=0 || ThreadPool.PendingWorkItemCount!=5) return 30;
          await done.Task;
          if(string.Join(",",order)!="a17,b27,c30,d40,e0" || ThreadPool.PendingWorkItemCount!=0) return 31;
          WaitOrTimerCallback waited=(_,_)=>order.Add("waited");
          var actions=new Action[] {
           ()=>ThreadPool.BindHandle(IntPtr.Zero), ()=>ThreadPool.BindHandle((SafeHandle)null!),
           ()=>ThreadPool.RegisterWaitForSingleObject(null!,waited,null,-1,true),
           ()=>ThreadPool.RegisterWaitForSingleObject(null!,waited,null,-1L,true),
           ()=>ThreadPool.RegisterWaitForSingleObject(null!,waited,null,uint.MaxValue,true),
           ()=>ThreadPool.RegisterWaitForSingleObject(null!,waited,null,TimeSpan.FromTicks(-19999),true),
           ()=>ThreadPool.UnsafeRegisterWaitForSingleObject(null!,waited,null,-1,true),
           ()=>ThreadPool.UnsafeRegisterWaitForSingleObject(null!,waited,null,-1L,true),
           ()=>ThreadPool.UnsafeRegisterWaitForSingleObject(null!,waited,null,uint.MaxValue,true),
           ()=>ThreadPool.UnsafeRegisterWaitForSingleObject(null!,waited,null,TimeSpan.FromTicks(-19999),true),
          };
          foreach(var action in actions) { try { action(); return 32; } catch(PlatformNotSupportedException e) { if(!e.Message.Contains("ThreadPool")) return 33; } }
          if(order.Count!=5) return 34;
          return Native(input);
         }
         private static unsafe int Native(int input)
         {
          var native = new NativeOverlapped { InternalLow = new IntPtr(17) };
          if(sizeof(NativeOverlapped) != 3 * IntPtr.Size + 8) return 35;
          if((byte*)&native.OffsetLow - (byte*)&native != 2 * IntPtr.Size || (byte*)&native.EventHandle - (byte*)&native != 2 * IntPtr.Size + 8) return 36;
          try { ThreadPool.UnsafeQueueNativeOverlapped(null); return 37; } catch(ArgumentNullException e) { if(e.ParamName != "overlapped") return 38; }
          try { ThreadPool.UnsafeQueueNativeOverlapped(&native); return 39; } catch(PlatformNotSupportedException) { }
          if(native.InternalLow != new IntPtr(17)) return 40;
          return input+1;
         }
         public static int Portable(int input)
         {
          var work=new NativeOverlapped { InternalLow=new IntPtr(7), InternalHigh=new IntPtr(9), OffsetLow=11, OffsetHigh=13, EventHandle=new IntPtr(17) };
          var copy=work; if(copy.InternalLow!=new IntPtr(7) || copy.InternalHigh!=new IntPtr(9) || copy.OffsetLow!=11 || copy.OffsetHigh!=13 || copy.EventHandle!=new IntPtr(17)) return 1;
          try { ThreadPool.QueueUserWorkItem(null!); return 2; } catch(ArgumentNullException e) { if(e.ParamName!="callBack") return 3; }
          try { ThreadPool.QueueUserWorkItem(null!,null); return 4; } catch(ArgumentNullException) { }
          try { ThreadPool.QueueUserWorkItem<int>(null!,1,false); return 5; } catch(ArgumentNullException) { }
          try { ThreadPool.UnsafeQueueUserWorkItem(null!,null); return 6; } catch(ArgumentNullException) { }
          try { ThreadPool.RegisterWaitForSingleObject(null!,null!,null,-2,true); return 80; } catch(ArgumentOutOfRangeException e) { if(e.ParamName!="millisecondsTimeOutInterval") return 60; }
          try { ThreadPool.RegisterWaitForSingleObject(null!,null!,null,-2L,true); return 81; } catch(ArgumentOutOfRangeException e) { if(e.ParamName!="millisecondsTimeOutInterval") return 61; }
          try { ThreadPool.RegisterWaitForSingleObject(null!,null!,null,(long)int.MaxValue+1,true); return 82; } catch(ArgumentOutOfRangeException e) { if(e.ParamName!="millisecondsTimeOutInterval") return 62; }
          try { ThreadPool.RegisterWaitForSingleObject(null!,null!,null,(uint)int.MaxValue+1,true); return 83; } catch(ArgumentOutOfRangeException e) { if(e.ParamName!="millisecondsTimeOutInterval") return 63; }
          try { ThreadPool.RegisterWaitForSingleObject(null!,null!,null,TimeSpan.FromDays(30),true); return 84; } catch(ArgumentOutOfRangeException e) { if(e.ParamName!="timeout") return 64; }
          try { ThreadPool.RegisterWaitForSingleObject(null!,null!,null,TimeSpan.FromMilliseconds(-2),true); return 85; } catch(ArgumentOutOfRangeException e) { if(e.ParamName!="timeout") return 65; }
          try { ThreadPool.UnsafeRegisterWaitForSingleObject(null!,null!,null,-2,true); return 86; } catch(ArgumentOutOfRangeException e) { if(e.ParamName!="millisecondsTimeOutInterval") return 66; }
          try { ThreadPool.UnsafeRegisterWaitForSingleObject(null!,null!,null,-2L,true); return 87; } catch(ArgumentOutOfRangeException e) { if(e.ParamName!="millisecondsTimeOutInterval") return 67; }
          try { ThreadPool.UnsafeRegisterWaitForSingleObject(null!,null!,null,(long)int.MaxValue+1,true); return 88; } catch(ArgumentOutOfRangeException e) { if(e.ParamName!="millisecondsTimeOutInterval") return 68; }
          try { ThreadPool.UnsafeRegisterWaitForSingleObject(null!,null!,null,(uint)int.MaxValue+1,true); return 89; } catch(ArgumentOutOfRangeException e) { if(e.ParamName!="millisecondsTimeOutInterval") return 69; }
          try { ThreadPool.UnsafeRegisterWaitForSingleObject(null!,null!,null,TimeSpan.FromDays(30),true); return 90; } catch(ArgumentOutOfRangeException e) { if(e.ParamName!="timeout") return 70; }
          try { ThreadPool.UnsafeRegisterWaitForSingleObject(null!,null!,null,TimeSpan.FromMilliseconds(-2),true); return 91; } catch(ArgumentOutOfRangeException e) { if(e.ParamName!="timeout") return 71; }
          return input+1;
         }
        }
        """;
}
