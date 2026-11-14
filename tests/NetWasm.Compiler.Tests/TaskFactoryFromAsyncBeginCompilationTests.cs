using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TaskFactoryFromAsyncBeginCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CallbackAdaptersMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "TaskFactoryApmFixture", Source, "TaskFactoryApmFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace TaskFactoryApmFixture;
        public sealed class Result : IAsyncResult
        {
            public object? AsyncState { get; set; }
            public WaitHandle AsyncWaitHandle => throw new Exception("Wait handle must not be read");
            public bool CompletedSynchronously { get; set; }
            public bool IsCompleted { get; set; }
        }
        public static class EntryPoint
        {
            private static IAsyncResult Begin(AsyncCallback callback, object? state)
            {
                var result = new Result { AsyncState = state, CompletedSynchronously = true, IsCompleted = true };
                callback(result);
                return result;
            }
            public static int Run(int input)
            {
                var factory = new TaskFactory(new CancellationToken(true));
                var generic = new TaskFactory<int>(new CancellationToken(true));
                var marker = new object();
                var calls = 0;
                var immediate = generic.FromAsync(Begin, r => { calls++; return 42; }, marker);
                if (calls != 1 || immediate.Result != 42 || immediate.AsyncState != marker) return 1;
                AsyncCallback? complete = null;
                var pendingResult = new Result { AsyncState = marker };
                var pending = generic.FromAsync((cb, state) => { complete = cb; return pendingResult; }, r => { calls++; return ReferenceEquals(r, pendingResult) ? 42 : 0; }, marker);
                if (pending.IsCompleted || calls != 1) return 2;
                pendingResult.IsCompleted = true; complete!(pendingResult);
                if (calls != 2 || pending.Result != 42) return 3;
                var callbackBeforeReturn = generic.FromAsync((cb, state) => { var r = new Result { IsCompleted = true }; cb(r); return r; }, r => { calls++; return 42; }, null);
                if (callbackBeforeReturn.Result != 42 || calls != 3) return 4;
                var failure = new InvalidOperationException("end");
                var fault = generic.FromAsync(Begin, r => throw failure, null);
                if (!fault.IsFaulted || !ReferenceEquals(fault.Exception!.InnerException, failure)) return 5;
                var token = new CancellationToken(true);
                var cancellation = new OperationCanceledException(token);
                var canceled = generic.FromAsync(Begin, r => throw cancellation, null);
                if (!canceled.IsCanceled) return 6;
                try { canceled.GetAwaiter().GetResult(); return 7; } catch (OperationCanceledException ex) { if (!ReferenceEquals(ex, cancellation) || ex.CancellationToken != token) return 8; }
                try { generic.FromAsync((cb, state) => throw failure, r => 0, null); return 9; } catch (InvalidOperationException ex) { if (!ReferenceEquals(ex, failure)) return 10; }
                try { generic.FromAsync(null!, null!, null, (TaskCreationOptions)(-1)); return 11; } catch (ArgumentNullException ex) { if (ex.ParamName != "beginMethod") return 12; }
                try { generic.FromAsync(Begin, null!, null, (TaskCreationOptions)(-1)); return 13; } catch (ArgumentNullException ex) { if (ex.ParamName != "endMethod") return 14; }
                foreach (var option in new[] { TaskCreationOptions.DenyChildAttach, TaskCreationOptions.HideScheduler, TaskCreationOptions.LongRunning, TaskCreationOptions.PreferFairness, TaskCreationOptions.RunContinuationsAsynchronously, (TaskCreationOptions)(-1) })
                {
                    try { generic.FromAsync(Begin, r => 42, null, option); return 15; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "creationOptions") return 16; }
                }
                return Overloads(factory, generic, marker) ? input + 1 : 17;
            }
            private static bool Overloads(TaskFactory factory, TaskFactory<int> generic, object marker)
            {
                var calls = 0;
                Func<AsyncCallback, object?, IAsyncResult> begin0 = Begin;
                Func<int, AsyncCallback, object?, IAsyncResult> begin1 = (a, cb, state) => a == 42 ? Begin(cb, state) : throw new Exception();
                Func<int, int, AsyncCallback, object?, IAsyncResult> begin2 = (a, b, cb, state) => a == 42 && b == 43 ? Begin(cb, state) : throw new Exception();
                Func<int, int, int, AsyncCallback, object?, IAsyncResult> begin3 = (a, b, c, cb, state) => a == 42 && b == 43 && c == 44 ? Begin(cb, state) : throw new Exception();
                Action<IAsyncResult> endAction = r => { if (r.AsyncState == marker) calls++; };
                Func<IAsyncResult, int> endFunction = r => r.AsyncState == marker ? 42 : 0;
                var result0 = factory.FromAsync(begin0, endAction, marker);
                var result1 = factory.FromAsync(begin0, endAction, marker, TaskCreationOptions.None);
                var result2 = factory.FromAsync(begin1, endAction, 42, marker);
                var result3 = factory.FromAsync(begin1, endAction, 42, marker, TaskCreationOptions.None);
                var result4 = factory.FromAsync(begin2, endAction, 42, 43, marker);
                var result5 = factory.FromAsync(begin2, endAction, 42, 43, marker, TaskCreationOptions.None);
                var result6 = factory.FromAsync(begin3, endAction, 42, 43, 44, marker);
                var result7 = factory.FromAsync(begin3, endAction, 42, 43, 44, marker, TaskCreationOptions.None);
                var result8 = factory.FromAsync(begin0, endFunction, marker);
                var result9 = factory.FromAsync(begin0, endFunction, marker, TaskCreationOptions.None);
                var result10 = factory.FromAsync(begin1, endFunction, 42, marker);
                var result11 = factory.FromAsync(begin1, endFunction, 42, marker, TaskCreationOptions.None);
                var result12 = factory.FromAsync(begin2, endFunction, 42, 43, marker);
                var result13 = factory.FromAsync(begin2, endFunction, 42, 43, marker, TaskCreationOptions.None);
                var result14 = factory.FromAsync(begin3, endFunction, 42, 43, 44, marker);
                var result15 = factory.FromAsync(begin3, endFunction, 42, 43, 44, marker, TaskCreationOptions.None);
                var result16 = generic.FromAsync(begin0, endFunction, marker);
                var result17 = generic.FromAsync(begin0, endFunction, marker, TaskCreationOptions.None);
                var result18 = generic.FromAsync(begin1, endFunction, 42, marker);
                var result19 = generic.FromAsync(begin1, endFunction, 42, marker, TaskCreationOptions.None);
                var result20 = generic.FromAsync(begin2, endFunction, 42, 43, marker);
                var result21 = generic.FromAsync(begin2, endFunction, 42, 43, marker, TaskCreationOptions.None);
                var result22 = generic.FromAsync(begin3, endFunction, 42, 43, 44, marker);
                var result23 = generic.FromAsync(begin3, endFunction, 42, 43, 44, marker, TaskCreationOptions.None);
                if (calls != 8) return false;
                if (!result0.IsCompletedSuccessfully) return false;
                if (result0.AsyncState != marker || result0.CreationOptions != TaskCreationOptions.None) return false;
                if (!result1.IsCompletedSuccessfully) return false;
                if (result1.AsyncState != marker || result1.CreationOptions != TaskCreationOptions.None) return false;
                if (!result2.IsCompletedSuccessfully) return false;
                if (result2.AsyncState != marker || result2.CreationOptions != TaskCreationOptions.None) return false;
                if (!result3.IsCompletedSuccessfully) return false;
                if (result3.AsyncState != marker || result3.CreationOptions != TaskCreationOptions.None) return false;
                if (!result4.IsCompletedSuccessfully) return false;
                if (result4.AsyncState != marker || result4.CreationOptions != TaskCreationOptions.None) return false;
                if (!result5.IsCompletedSuccessfully) return false;
                if (result5.AsyncState != marker || result5.CreationOptions != TaskCreationOptions.None) return false;
                if (!result6.IsCompletedSuccessfully) return false;
                if (result6.AsyncState != marker || result6.CreationOptions != TaskCreationOptions.None) return false;
                if (!result7.IsCompletedSuccessfully) return false;
                if (result7.AsyncState != marker || result7.CreationOptions != TaskCreationOptions.None) return false;
                if (result8.Result != 42) return false;
                if (result8.AsyncState != marker || result8.CreationOptions != TaskCreationOptions.None) return false;
                if (result9.Result != 42) return false;
                if (result9.AsyncState != marker || result9.CreationOptions != TaskCreationOptions.None) return false;
                if (result10.Result != 42) return false;
                if (result10.AsyncState != marker || result10.CreationOptions != TaskCreationOptions.None) return false;
                if (result11.Result != 42) return false;
                if (result11.AsyncState != marker || result11.CreationOptions != TaskCreationOptions.None) return false;
                if (result12.Result != 42) return false;
                if (result12.AsyncState != marker || result12.CreationOptions != TaskCreationOptions.None) return false;
                if (result13.Result != 42) return false;
                if (result13.AsyncState != marker || result13.CreationOptions != TaskCreationOptions.None) return false;
                if (result14.Result != 42) return false;
                if (result14.AsyncState != marker || result14.CreationOptions != TaskCreationOptions.None) return false;
                if (result15.Result != 42) return false;
                if (result15.AsyncState != marker || result15.CreationOptions != TaskCreationOptions.None) return false;
                if (result16.Result != 42) return false;
                if (result16.AsyncState != marker || result16.CreationOptions != TaskCreationOptions.None) return false;
                if (result17.Result != 42) return false;
                if (result17.AsyncState != marker || result17.CreationOptions != TaskCreationOptions.None) return false;
                if (result18.Result != 42) return false;
                if (result18.AsyncState != marker || result18.CreationOptions != TaskCreationOptions.None) return false;
                if (result19.Result != 42) return false;
                if (result19.AsyncState != marker || result19.CreationOptions != TaskCreationOptions.None) return false;
                if (result20.Result != 42) return false;
                if (result20.AsyncState != marker || result20.CreationOptions != TaskCreationOptions.None) return false;
                if (result21.Result != 42) return false;
                if (result21.AsyncState != marker || result21.CreationOptions != TaskCreationOptions.None) return false;
                if (result22.Result != 42) return false;
                if (result22.AsyncState != marker || result22.CreationOptions != TaskCreationOptions.None) return false;
                if (result23.Result != 42) return false;
                if (result23.AsyncState != marker || result23.CreationOptions != TaskCreationOptions.None) return false;
                return true;
            }
        }
        """;
}
