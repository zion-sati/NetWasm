// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;

// Cells the page and the worker both run.
//
// An async import taking a JSObject that C# obtained. The import was once given the object's host handle as its task's
// handle, so it completed whichever task that number named and hung whenever the two numbers differed; each cell runs
// with and without extra live objects, so the numbers differ either way.
//
// What a failed import raises. Its JSException's message is the JavaScript value's toString(), as .NET's browser
// runtime gives it, and the exception keeps the value.
public static class InteropCells
{
    private const string Module = "app.cells";

    [JSImport("createController", Module)]
    internal static extern JSObject CreateController();

    [JSImport("abort", Module)]
    internal static extern void Abort(JSObject controller);

    [JSImport("asyncObjToInt", Module)]
    internal static extern Task<int> AsyncObjToInt(JSObject value);

    [JSImport("delay", Module)]
    internal static extern Task<string[]> Delay(int milliseconds, JSObject controller);

    [JSImport("immediate", Module)]
    internal static extern Task<string[]> Immediate(JSObject controller);

    [JSImport("listen", Module)]
    internal static extern JSSubscription Listen(Action callback, JSObject target);

    [JSImport("throwValue", Module)]
    internal static extern void ThrowValue(string kind);

    // Scalars only, so a direct WebAssembly call.
    [JSImport("throwDirect", Module)]
    internal static extern int ThrowDirect(int kind);

    [JSImport("rejectWith", Module)]
    internal static extern Task<int> RejectWith(string kind);

    [JSImport("abortedFetch", Module)]
    internal static extern Task<int> AbortedFetch(string url, string reason);

    // Gives an exception back to JavaScript, which keeps it.
    [JSImport("keep", Module)]
    internal static extern void Keep(Exception? error);

    // Whether what JavaScript kept is the value the last failing import threw or rejected with.
    [JSImport("keptOriginal", Module)]
    internal static extern bool KeptOriginal();

    // What JavaScript kept, as its name and message.
    [JSImport("keptDescription", Module)]
    internal static extern string KeptDescription();

    // The last failure's JavaScript stack without the message an engine puts at its top.
    [JSImport("lastStackTail", Module)]
    internal static extern string LastStackTail();

    [JSImport("throwManaged", Module)]
    internal static extern void ThrowManaged(Exception error);

    [JSImport("rejectManaged", Module)]
    internal static extern Task<int> RejectManaged(Exception error);

    // The callback-lowered Promise form.
    [JSImportPromise("rejectLater", Module)]
    internal static extern JSSubscription BeginRejectLater(string kind, Action<int> resolve, Action reject);

    [JSImport("touch", Module)]
    internal static extern int Touch(JSObject value);

    private static JSObject? s_fresh;

    public static async Task<string> Run(string cell, bool displace)
    {
        // Live objects that move the next object's host handle away from the next task's handle.
        using var extra = displace ? CreateController() : null;
        using var global = displace ? JSHost.GlobalThis : null;
        return cell switch
        {
            "usingImport" => await UsingImport(),
            "usingGlobalProperty" => await UsingGlobalProperty(),
            "neverDisposed" => await NeverDisposed(),
            "disposeAfter" => await DisposeAfter(),
            "tryFinally" => await TryFinally(),
            "lambda" => await ThroughLambda(),
            "registration" => await WithRegistration(),
            "registrationNoDispose" => await RegistrationNoDispose(),
            "immediateArray" => await ImmediateArray(),
            "delayedArray" => await DelayedArray(),
            "nested" => string.Join("/", await Nested(20)),
            "withToken" => await RunWithToken(),
            "concurrent" => await Concurrent(),
            "subscriptionWhilePending" => await SubscriptionWhilePending(),
            "releaseOnceStart" => ReleaseOnceStart(),
            "releaseOnceFinish" => ReleaseOnceFinish(),
            _ => Failure(cell),
        };
    }

    private static async Task<string> UsingImport()
    {
        using var controller = CreateController();
        return (await AsyncObjToInt(controller)).ToString();
    }

    private static async Task<string> UsingGlobalProperty()
    {
        using var controller = JSHost.GlobalThis.GetPropertyAsJSObject("cellsController")!;
        return (await AsyncObjToInt(controller)).ToString();
    }

    private static async Task<string> NeverDisposed()
    {
        var controller = CreateController();
        return (await AsyncObjToInt(controller)).ToString();
    }

    private static async Task<string> DisposeAfter()
    {
        var controller = CreateController();
        var result = await AsyncObjToInt(controller);
        controller.Dispose();
        return result.ToString();
    }

    private static async Task<string> TryFinally()
    {
        var controller = CreateController();
        try { return (await AsyncObjToInt(controller)).ToString(); }
        finally { controller.Dispose(); }
    }

    private static async Task<string> ThroughLambda()
    {
        Func<JSObject, Task<int>> call = value => AsyncObjToInt(value);
        var controller = CreateController();
        try { return (await call(controller)).ToString(); }
        finally { controller.Dispose(); }
    }

    private static async Task<string> WithRegistration()
    {
        using var source = new CancellationTokenSource();
        var controller = CreateController();
        try
        {
            using var registration = source.Token.Register(static state => Abort((JSObject)state!), controller);
            return (await AsyncObjToInt(controller)).ToString();
        }
        finally { controller.Dispose(); }
    }

    private static async Task<string> RegistrationNoDispose()
    {
        using var source = new CancellationTokenSource();
        var controller = CreateController();
        using var registration = source.Token.Register(static state => Abort((JSObject)state!), controller);
        return (await AsyncObjToInt(controller)).ToString();
    }

    private static async Task<string> ImmediateArray()
    {
        var controller = CreateController();
        try { return string.Join("/", await Immediate(controller)); }
        finally { controller.Dispose(); }
    }

    private static async Task<string> DelayedArray()
    {
        var controller = CreateController();
        try { return string.Join("/", await Delay(20, controller)); }
        finally { controller.Dispose(); }
    }

    private static async Task<string[]> Nested(int milliseconds)
    {
        var controller = CreateController();
        try { return await Delay(milliseconds, controller); }
        finally { controller.Dispose(); }
    }

    // A cancellation helper: one AbortController per call, aborted from the token, the
    // import called through a delegate parameter.
    private static async Task<string[]> WithToken(Func<JSObject, Task<string[]>> call, CancellationToken token)
    {
        var controller = CreateController();
        try
        {
            using var registration = token.Register(static state => Abort((JSObject)state!), controller);
            return await call(controller);
        }
        finally { controller.Dispose(); }
    }

    private static async Task<string> RunWithToken()
    {
        using var never = new CancellationTokenSource();
        var completed = string.Join("/", await WithToken(controller => Delay(20, controller), never.Token));
        using var soon = new CancellationTokenSource(50);
        var cancelled = string.Join("/", await WithToken(controller => Delay(5000, controller), soon.Token));
        return completed + "|" + cancelled;
    }

    // Two operations in flight at once, each with its own object, each completing its own task.
    private static async Task<string> Concurrent()
    {
        using var first = CreateController();
        using var second = JSHost.GlobalThis.GetPropertyAsJSObject("cellsController")!;
        var slow = Delay(30, first);
        var fast = Delay(10, second);
        return string.Join("/", await slow) + "|" + string.Join("/", await fast);
    }

    // A subscription whose callback comes before a JSObject argument releases its own callback's handle, not a
    // pending task's.
    private static async Task<string> SubscriptionWhilePending()
    {
        using var controller = CreateController();
        var pending = Delay(20, controller);
        using (var target = CreateController())
        {
            Listen(static () => { }, target).Dispose();
        }
        return string.Join("/", await pending);
    }

    // Reading a JSException's StackTrace releases its error's host handle. The next object takes that number, and the
    // exception's finalizer must not release it again: the export's return runs finalizers, then the next call uses it.
    private static string ReleaseOnceStart()
    {
        ReleaseAndDrop();
        s_fresh = CreateController();
        GC.Collect();
        return "started";
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ReleaseAndDrop()
    {
        try { ThrowValue("syncType"); }
        catch (JSException error) { _ = error.StackTrace; }
    }

    private static string ReleaseOnceFinish()
    {
        GC.Collect();
        var result = Touch(s_fresh!).ToString();
        s_fresh!.Dispose();
        s_fresh = null;
        return result;
    }

    private static string Failure(string cell) => throw new ArgumentException("unknown cell " + cell);

    // Upstream behaviors beside the JSException's message: a managed exception crossing back, the callback-lowered
    // Promise form, and StackTrace.
    public static async Task<string> Extra(string cell)
    {
        switch (cell)
        {
            case "managedSync":
            {
                var original = new InvalidOperationException("managed original");
                try { ThrowManaged(original); }
                catch (Exception error) { return ReferenceEquals(error, original) ? "same" : "other " + error.GetType().Name; }
                return "no exception";
            }
            case "managedAsync":
            {
                var original = new InvalidOperationException("managed original");
                try { await RejectManaged(original); }
                catch (Exception error) { return ReferenceEquals(error, original) ? "same" : "other " + error.GetType().Name; }
                return "no exception";
            }
            case "promiseImport":
            {
                var source = new JSPromiseTaskSource<int>();
                source.SetSubscription(BeginRejectLater("rejectError", source.Resolve, source.Reject));
                try { await source.Task; }
                catch (JSException error)
                {
                    Keep(error);
                    return error.Message + (KeptOriginal() ? "|same" : "|other");
                }
                return "no exception";
            }
            case "stackTrace":
            {
                try { ThrowValue("syncType"); }
                catch (JSException error)
                {
                    var tail = LastStackTail();
                    var stack = error.StackTrace ?? "";
                    // Reading it released the error, so the exception now crosses as itself, as on .NET.
                    Keep(error);
                    return (tail.Length != 0 && stack.EndsWith(tail, StringComparison.Ordinal) ? "stack" : "no stack: " + stack)
                        + "|" + (KeptOriginal() ? "original" : KeptDescription());
                }
                return "no exception";
            }
            case "equality":
            {
                try { ThrowValue("syncType"); }
                catch (JSException first)
                {
                    var same = new JSException("x");
                    return (first.Equals(first) ? "self" : "not self") + "|" + (first.Equals(same) ? "equal" : "unequal")
                        + "|" + (same.Equals(new JSException("y")) ? "none-equal" : "none-unequal");
                }
                return "no exception";
            }
            default:
                throw new ArgumentException("unknown cell " + cell);
        }
    }

    // A synchronous export a failed import's JSException leaves.
    public static int FailingSyncExport(string kind)
    {
        ThrowValue(kind);
        return 0;
    }

    // An async export whose task faults with a failed import's JSException.
    public static async Task<int> FailingExport(string kind)
    {
        await Task.Yield();
        return await RejectWith(kind);
    }

    // What a failed import raised: its JSException's message.
    public static async Task<string> Fail(string cell)
    {
        try
        {
            switch (cell)
            {
                case "direct":
                    ThrowDirect(1);
                    break;
                case "rejectError":
                case "rejectString":
                case "rejectNothing":
                    await RejectWith(cell);
                    break;
                case "fetchAbort":
                    await AbortedFetch("index.html", "");
                    break;
                case "fetchTimeout":
                    await AbortedFetch("index.html", "TimeoutError");
                    break;
                default:
                    ThrowValue(cell);
                    break;
            }
        }
        catch (JSException error)
        {
            Keep(error);
            return error.Message + (error.ToString() == error.Message ? "" : " (ToString differs)")
                + (KeptOriginal() ? "|same" : "|other");
        }
        return "no exception";
    }
}
