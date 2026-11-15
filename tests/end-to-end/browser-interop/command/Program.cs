// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

public delegate void BytesCallback(byte[]? value);

public static class Program
{
    [JSImport("show", "app.host")]
    private static extern void Show(string value);

    [JSImport("connect", "app.host")]
    private static extern Task<int> Connect();

    [JSImport("onMessage", "app.host")]
    private static extern JSSubscription OnMessage(BytesCallback callback);

    [JSImport("createObject", "app.host")]
    private static extern JSObject CreateObject();

    [JSImport("isNullObject", "app.host")]
    private static extern bool IsNullObject(JSObject? value);

    [JSImport("assertReleased", "app.host")]
    private static extern void AssertReleased();

    public static async Task Main()
    {
        var received = new TaskCompletionSource<int>();
        var valueObject = CreateObject();
        using (valueObject)
        using (IDisposable subscription = OnMessage(bytes => received.SetResult(bytes![0])))
        {
            var connected = await Connect();
            Show("ready");
            var value = await received.Task;
            if (connected + value != 42)
                throw new InvalidOperationException("Page callback result mismatch.");
            Show("completed:42");
        }
        if (!IsNullObject(valueObject))
            throw new InvalidOperationException("Disposed object handle remained active.");
        // Check before Main completes so host instance teardown cannot hide a
        // missing explicit subscription release.
        AssertReleased();
    }
}
