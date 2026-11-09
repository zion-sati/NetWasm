namespace System.Runtime.InteropServices
{
    // A JavaScript module has no asynchronous Main to run its event loop, so its JavaScript host
    // runs the loop after each call into the module. Run runs the ready work, then reports EXIT
    // when nothing is pending or WAIT on the loop's waitable set; the host then calls Callback with
    // each event of that set, or with no event after another call queued work, until EXIT.
    internal static class HostEventLoop
    {
        internal static int Run() => PlatformServices.RunHostEventLoop();

        internal static int Callback(int eventCode, int waitable, int payload) =>
            PlatformServices.DeliverHostEventLoopEvent(eventCode, waitable, unchecked((uint)payload));
    }
}
