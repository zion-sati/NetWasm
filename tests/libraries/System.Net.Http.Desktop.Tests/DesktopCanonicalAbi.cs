namespace System.Runtime.InteropServices.WebAssembly;

// The desktop stand-in for NetWasm CoreLib's CanonicalAbi.RunSynchronously, which the package sources use for
// synchronous sends and reads: desktop simply waits.
internal static class CanonicalAbi
{
    public static T RunSynchronously<T>(Func<Task<T>> operation) => operation().GetAwaiter().GetResult();
}
