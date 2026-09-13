// Adapted from dotnet/runtime System.Runtime.ExceptionServices metadata.
// The upstream implementation is licensed under MIT.

namespace System.Runtime.ExceptionServices
{
    // NetWasm does not deliver process-corrupted-state exceptions.  Keeping
    // this obsolete marker preserves the source contract without adding a
    // process-wide exception or recovery mechanism.
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    [Obsolete(
        "Recovery from corrupted process state exceptions is not supported; HandleProcessCorruptedStateExceptionsAttribute is ignored.",
        DiagnosticId = "SYSLIB0032",
        UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
    public sealed class HandleProcessCorruptedStateExceptionsAttribute : Attribute
    {
        public HandleProcessCorruptedStateExceptionsAttribute() { }
    }
}
