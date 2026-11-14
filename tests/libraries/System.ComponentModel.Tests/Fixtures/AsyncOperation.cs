// From the compiler harness's AsyncOperationCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
using System.Threading;
namespace CategoryFixtureAsyncOperation;
public sealed class Context : SynchronizationContext
{
    public int Starts; public int Completions; public int Posts;
    public bool FailPost;
    public override void OperationStarted() { Starts++; }
    public override void OperationCompleted() { Completions++; }
    public override void Post(SendOrPostCallback callback, object state) { Posts++; if (FailPost) throw new NotSupportedException(); callback(state); }
}
public static class EntryPoint
{
    public static int Run(int input)
    {
        var original = SynchronizationContext.Current;
        try
        {
            AsyncOperationManager.SynchronizationContext = null;
            var fallback = AsyncOperationManager.SynchronizationContext;
            if (fallback == null || SynchronizationContext.Current != fallback) return 1;
            var context = new Context(); AsyncOperationManager.SynchronizationContext = context;
            var state = new object(); var operation = AsyncOperationManager.CreateOperation(state);
            if (operation.UserSuppliedState != state || operation.SynchronizationContext != context || context.Starts != 1) return 2;
            AsyncOperationManager.SynchronizationContext = new Context();
            var calls = 0; operation.Post(value => { if (value == state) calls++; }, state);
            if (calls != 1 || context.Posts != 1 || context.Completions != 0) return 3;
            var caught = 0;
            try { operation.Post(null, state); } catch (ArgumentNullException e) { if (e.ParamName == "d") caught++; }
            operation.PostOperationCompleted(value => {
                if (value == state) calls++;
                try { operation.Post(_ => { }, null); } catch (InvalidOperationException) { caught++; }
            }, state);
            if (calls != 2 || context.Posts != 2 || context.Completions != 1) return 4;
            try { operation.OperationCompleted(); } catch (InvalidOperationException) { caught++; }
            try { operation.Post(null, null); } catch (InvalidOperationException) { caught++; }
            AsyncOperationManager.SynchronizationContext = context;
            var completed = AsyncOperationManager.CreateOperation(null); completed.OperationCompleted();
            if (context.Starts != 2 || context.Completions != 2 || completed.UserSuppliedState != null) return 5;
            var failing = AsyncOperationManager.CreateOperation(state); context.FailPost = true;
            try { failing.PostOperationCompleted(_ => { }, state); } catch (NotSupportedException) { caught++; }
            try { failing.OperationCompleted(); } catch (InvalidOperationException) { caught++; }
            if (context.Completions != 2 || context.Starts != 3 || context.Posts != 3) return 6;
            return caught == 6 ? input + 1 : 100 + caught;
        }
        finally { SynchronizationContext.SetSynchronizationContext(original); }
    }
}
