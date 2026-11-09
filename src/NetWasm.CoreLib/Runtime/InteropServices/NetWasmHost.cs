using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices;

/// <summary>The host flavour the application was compiled for.</summary>
internal static class NetWasmHost
{
    /// <summary>
    /// True for modules behind the JavaScript export boundary (raw core modules and browser
    /// JavaScript modules); false for WIT components. The compiler emits the answer as a constant,
    /// so it reflects the compiled target rather than a probe of the running host.
    /// </summary>
    internal static extern bool IsJavaScriptHost
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        get;
    }
}
