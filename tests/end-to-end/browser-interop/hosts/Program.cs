// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;

public static class HostExports
{
    [JSImport("label", "app.labels")]
    private static extern string Label(int value);

    [JSExport("twice")]
    public static int Twice(int value) => value * 2;

    [JSExport("label")]
    public static string LabelValue(int value) => Label(value);

    [JSExport("sum")]
    public static long Sum(int[] values)
    {
        long total = 0;
        foreach (var value in values)
        {
            total += value;
        }
        return total;
    }

    [JSExport("later")]
    public static async Task<int> Later(string text)
    {
        await Task.Yield();
        return text.Length + DateTime.UtcNow.Year.ToString().Length;
    }

    // IndexedDB commits a transaction once it has no pending request and control returns to the browser's event
    // loop. An awaited import's continuation runs inside the promise's reaction, so a request issued after the await
    // finds its transaction still active, in either host; a timer, or a Task.Yield once queued work has run 30 ms,
    // returns to the browser and ends it. A reaction page code adds to the same promise later runs after the
    // continuation.
    [JSExport("transactions")]
    public static async Task<string> Transactions(string name)
    {
        var database = await IndexedDb.Open(name);
        try
        {
            // With the module's event loop idle, and again while a timer it waits for is pending.
            await AwaitedRequestsInOneTransaction(database, "idle loop");
            using (var pending = new CancellationTokenSource())
            {
                var timer = Task.Delay(TimeSpan.FromMinutes(5), pending.Token);
                await AwaitedRequestsInOneTransaction(database, "pending timer");
                pending.Cancel();
                try
                {
                    await timer;
                }
                catch (OperationCanceledException)
                {
                }
            }

            // A timer returns control to the browser, which ends the transaction: the next request is refused. The
            // transaction is inactive or already finished by then, which IndexedDB names differently.
            var delayed = IndexedDb.Begin(database);
            Expect("ok", await IndexedDb.Put(delayed, "d0", "v"), "put before Task.Delay");
            await Task.Delay(1);
            ExpectEnded(await IndexedDb.Put(delayed, "d1", "v"), "put after Task.Delay");
            Expect("complete", await IndexedDb.Finished(delayed), "the transaction Task.Delay ended");

            var held = IndexedDb.Hold();
            IndexedDb.AttachLater();
            IndexedDb.ReleaseHeld();
            await held;
            IndexedDb.Record("C#");
            await IndexedDb.NextTask();
            Expect("C#,JS", IndexedDb.Recorded(), "the continuation and a later reaction to the same promise");
            return "one transaction across awaits; a batch's Task.Yield and Task.Delay ended one; continuation before a later reaction";
        }
        finally
        {
            await IndexedDb.Drop(database);
        }
    }

    // Eight awaited writes and an awaited read of one of them in one transaction. Once the turn's batch of queued work
    // has run 30 ms, a Task.Yield returns to the browser: a zero-delay timer set before it has run, and the
    // transaction has ended. (Whether an earlier Task.Yield stays in the turn depends on the machine's speed, so it
    // is not checked.)
    private static async Task AwaitedRequestsInOneTransaction(int database, string pass)
    {
        var transaction = IndexedDb.Begin(database);
        for (var i = 0; i < 8; i++)
        {
            Expect("ok", await IndexedDb.Put(transaction, "k" + i, pass + i), $"{pass}: awaited put {i}");
        }
        Expect(pass + 3, await IndexedDb.Get(transaction, "k3"), $"{pass}: awaited get");
        Spin(35);
        HostTurns.Arm();
        await Task.Yield();
        if (!HostTurns.TimerRan())
        {
            throw new InvalidOperationException($"{pass}: a Task.Yield after 35 ms of the batch did not return to the browser");
        }
        ExpectEnded(await IndexedDb.Put(transaction, "k9", pass), $"{pass}: put after the batch's Task.Yield");
        Expect("complete", await IndexedDb.Finished(transaction), $"{pass}: the awaited requests' transaction");
    }

    private static void Spin(int milliseconds)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < milliseconds)
        {
        }
    }

    // A transaction the browser ended is inactive, or already finished, by the next request, which IndexedDB names
    // differently.
    private static void ExpectEnded(string refused, string step)
    {
        if (refused is not ("TransactionInactiveError" or "InvalidStateError"))
        {
            throw new InvalidOperationException($"{step}: expected the transaction to have ended, got {refused}");
        }
    }

    // Queued work runs in batches of up to 30 ms, so a Task.Yield loop and a chain of Task.Run calls each let the
    // browser run a timer page code set before them, well within a second.
    [JSExport("yieldLoop")]
    public static Task<int> YieldLoop() => UntilTimerRuns("a Task.Yield loop", static async () => await Task.Yield());

    [JSExport("taskRunChain")]
    public static Task<int> TaskRunChain() => UntilTimerRuns("a Task.Run chain", static () => Task.Run(static () => { }));

    private static async Task<int> UntilTimerRuns(string loop, Func<Task> step)
    {
        HostTurns.Arm();
        var elapsed = Stopwatch.StartNew();
        var iterations = 0;
        while (!HostTurns.TimerRan())
        {
            if (elapsed.ElapsedMilliseconds > 1000)
            {
                throw new InvalidOperationException($"{loop} did not let the browser run a timer callback within a second");
            }
            iterations++;
            await step();
        }
        return iterations;
    }

    private static void Expect(string expected, string actual, string step)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException($"{step}: expected {expected}, got {actual}");
        }
    }

    // Issue #216: allocation paces collections, so calls that allocate nothing never collect,
    // even in a module whose objects have finalizers.
    [JSExport("collections")]
    public static int Collections() => GC.CollectionCount(0);

    [JSExport("finalizes")]
    public static int Finalizes()
    {
        Abandon();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        return Finalizable.Finalized;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Abandon() => _ = new Finalizable();

    private sealed class Finalizable
    {
        internal static int Finalized;

        ~Finalizable() => Finalized++;
    }
}

internal static class IndexedDb
{
    [JSImport("open", "app.indexeddb")]
    internal static extern Task<int> Open(string name);

    [JSImport("drop", "app.indexeddb")]
    internal static extern Task Drop(int database);

    [JSImport("begin", "app.indexeddb")]
    internal static extern int Begin(int database);

    [JSImport("finished", "app.indexeddb")]
    internal static extern Task<string> Finished(int transaction);

    [JSImport("put", "app.indexeddb")]
    internal static extern Task<string> Put(int transaction, string key, string value);

    [JSImport("get", "app.indexeddb")]
    internal static extern Task<string> Get(int transaction, string key);

    [JSImport("nextTask", "app.indexeddb")]
    internal static extern Task NextTask();

    [JSImport("hold", "app.indexeddb")]
    internal static extern Task Hold();

    [JSImport("attachLater", "app.indexeddb")]
    internal static extern void AttachLater();

    [JSImport("releaseHeld", "app.indexeddb")]
    internal static extern void ReleaseHeld();

    [JSImport("record", "app.indexeddb")]
    internal static extern void Record(string entry);

    [JSImport("recorded", "app.indexeddb")]
    internal static extern string Recorded();
}

internal static class HostTurns
{
    [JSImport("arm", "app.turns")]
    internal static extern void Arm();

    [JSImport("timerRan", "app.turns")]
    internal static extern bool TimerRan();
}
