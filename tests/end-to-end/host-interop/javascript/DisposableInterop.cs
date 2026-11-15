// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Runtime.InteropServices.JavaScript;

public static class DisposableInterop
{
    [JSImport("create", "consumer.disposal")]
    private static extern JSObject Create();

    [JSImport("subscribe", "consumer.disposal")]
    private static extern JSSubscription Subscribe(Action callback);

    private static int _managedDisposals;

    public static int Run(int mode)
    {
        switch (mode)
        {
            case 0:
                Create().Dispose();
                Subscribe(Callback).Dispose();
                break;
            case 1:
                using (var value = Create())
                using (var subscription = Subscribe(Callback)) { }
                break;
            case 2:
                DisposeInterface(Create());
                DisposeInterface(Subscribe(Callback));
                break;
            case 3:
                DisposeGeneric(Create());
                DisposeGeneric(Subscribe(Callback));
                break;
            case 4:
                try
                {
                    using var value = Create();
                    using var subscription = Subscribe(Callback);
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException) { }
                break;
            case 5:
                var repeatedObject = Create();
                var repeatedSubscription = Subscribe(Callback);
                DisposeInterface(repeatedObject);
                DisposeInterface(repeatedSubscription);
                repeatedObject.Dispose();
                repeatedSubscription.Dispose();
                DisposeGeneric(repeatedObject);
                DisposeGeneric(repeatedSubscription);
                break;
            case 6:
                using ((IDisposable?)null) { }
                try { DisposeInterface(null!); return 1; }
                catch (NullReferenceException) { }
                break;
            case 7:
                var before = _managedDisposals;
                using ((IDisposable)new ManagedDisposable()) { }
                if (_managedDisposals != before + 1) return 3;
                break;
            default:
                return 2;
        }
        return 42;
    }

    private static void Callback() { }

    private static void DisposeInterface(IDisposable value) => value.Dispose();

    private static void DisposeGeneric<T>(T value) where T : IDisposable => value.Dispose();

    private sealed class ManagedDisposable : IDisposable
    {
        public void Dispose() => _managedDisposals++;
    }
}
