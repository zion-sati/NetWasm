using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class OverlappedContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void OverlappedManagedStateAndNativeRejections(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("OverlappedFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "OverlappedFixture.EntryPoint",
            "Run", [new("run", "OverlappedFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Threading;
        using System.Runtime.InteropServices;
        #pragma warning disable CS0618
        namespace OverlappedFixture;
        public static class EntryPoint
        {
            public static unsafe int Run(int input)
            {
                var result = Portable(input); if(result != 42) return result;
                var calls = 0; IOCompletionCallback callback = (_,_,_) => calls++;
                var descriptor = new Overlapped(3,5,new IntPtr(7),null);
                var rejected = 0;
                try { descriptor.Pack(null); } catch(PlatformNotSupportedException) { rejected++; }
                try { descriptor.Pack(callback,new byte[4]); } catch(PlatformNotSupportedException) { rejected++; }
                try { descriptor.UnsafePack(null); } catch(PlatformNotSupportedException) { rejected++; }
                try { descriptor.UnsafePack(callback,new object()); } catch(PlatformNotSupportedException) { rejected++; }
                try { new PreAllocatedOverlapped(callback,null,null); } catch(PlatformNotSupportedException) { rejected++; }
                try { PreAllocatedOverlapped.UnsafeCreate(callback,null,null); } catch(PlatformNotSupportedException) { rejected++; }
                using var handle = new BorrowedHandle(new IntPtr(7));
                try { ThreadPoolBoundHandle.BindHandle(handle); } catch(PlatformNotSupportedException) { rejected++; }
                var native = new NativeOverlapped { OffsetLow = 11 };
                try { Overlapped.Unpack(&native); } catch(PlatformNotSupportedException) { rejected++; }
                try { Overlapped.Free(&native); } catch(PlatformNotSupportedException) { rejected++; }
                try { ThreadPoolBoundHandle.GetNativeOverlappedState(&native); } catch(PlatformNotSupportedException) { rejected++; }
                if(rejected != 10 || calls != 0 || native.OffsetLow != 11 || handle.IsClosed || descriptor.OffsetLow != 3 || descriptor.OffsetHigh != 5 || descriptor.EventHandleIntPtr != new IntPtr(7)) return 30;
                descriptor.OffsetLow=9; if(descriptor.OffsetLow!=9) return 31;
                return input+1;
            }
            public static unsafe int Portable(int input)
            {
                var empty = new Overlapped();
                if(empty.AsyncResult != null || empty.OffsetLow!=0 || empty.OffsetHigh!=0 || empty.EventHandleIntPtr!=IntPtr.Zero) return 1;
                var ar = new Result(); var descriptor = new Overlapped(1,2,new IntPtr(3),ar);
                if(!ReferenceEquals(descriptor.AsyncResult,ar) || descriptor.OffsetLow!=1 || descriptor.OffsetHigh!=2 || descriptor.EventHandleIntPtr!=new IntPtr(3)) return 2;
                descriptor.AsyncResult=null!; descriptor.OffsetLow=-7; descriptor.OffsetHigh=int.MaxValue; descriptor.EventHandle=-1;
                if(descriptor.AsyncResult!=null || descriptor.OffsetLow!=-7 || descriptor.OffsetHigh!=int.MaxValue || descriptor.EventHandleIntPtr!=new IntPtr(-1) || descriptor.EventHandle!=-1) return 3;
                var legacy = new Overlapped(4,5,6,ar); if(legacy.EventHandle!=6) return 4;
                descriptor.EventHandleIntPtr=new IntPtr(99); if(descriptor.EventHandle!=99) return 5;
                var native = new NativeOverlapped();
                IOCompletionCallback callback=(error,bytes,record)=>record->OffsetLow=(int)(error+bytes);
                callback(2,3,&native); if(native.OffsetLow!=5) return 6;
                try { Overlapped.Unpack(null); return 7; } catch(ArgumentNullException e) { if(e.ParamName!="nativeOverlappedPtr") return 8; }
                try { Overlapped.Free(null); return 9; } catch(ArgumentNullException) { }
                try { ThreadPoolBoundHandle.GetNativeOverlappedState(null); return 10; } catch(ArgumentNullException e) { if(e.ParamName!="overlapped") return 11; }
                try { new PreAllocatedOverlapped(null!,null,null); return 12; } catch(ArgumentNullException e) { if(e.ParamName!="callback") return 13; }
                try { PreAllocatedOverlapped.UnsafeCreate(null!,null,null); return 14; } catch(ArgumentNullException) { }
                try { ThreadPoolBoundHandle.BindHandle(null!); return 15; } catch(ArgumentNullException e) { if(e.ParamName!="handle") return 16; }
                using var invalid=new BorrowedHandle(IntPtr.Zero);
                try { ThreadPoolBoundHandle.BindHandle(invalid); return 17; } catch(ArgumentException e) { if(e.ParamName!="handle") return 18; }
                var closed=new BorrowedHandle(new IntPtr(7)); closed.Dispose();
                try { ThreadPoolBoundHandle.BindHandle(closed); return 19; } catch(ArgumentException) { }
                return input+1;
            }
        }
        public sealed class BorrowedHandle : SafeHandle
        {
            public BorrowedHandle(IntPtr value) : base(IntPtr.Zero,false) { SetHandle(value); }
            public override bool IsInvalid => handle == IntPtr.Zero;
            protected override bool ReleaseHandle() => true;
        }
        public sealed class Result : IAsyncResult
        {
            public object? AsyncState => null;
            public WaitHandle AsyncWaitHandle => throw new PlatformNotSupportedException();
            public bool CompletedSynchronously => true;
            public bool IsCompleted => true;
        }
        """;
}
