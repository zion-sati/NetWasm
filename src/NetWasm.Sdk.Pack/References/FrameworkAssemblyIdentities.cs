using System.Collections.Frozen;

namespace NetWasm.Sdk.Pack.References;

// Explicit reference-package identities observed in the official standard
// contracts and framework backports. Keys establish binding, not authentication.
internal static class FrameworkAssemblyIdentities
{
    internal static readonly FrozenDictionary<string, string> ReferencePackages =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["System.AppContext"] = "b03f5f7f11d50a3a",
            ["System.Collections"] = "b03f5f7f11d50a3a",
            ["System.Collections.Concurrent"] = "b03f5f7f11d50a3a",
            ["System.Console"] = "b03f5f7f11d50a3a",
            ["System.Diagnostics.Debug"] = "b03f5f7f11d50a3a",
            ["System.Diagnostics.Tools"] = "b03f5f7f11d50a3a",
            ["System.Globalization"] = "b03f5f7f11d50a3a",
            ["System.IO"] = "b03f5f7f11d50a3a",
            ["System.Linq.Expressions"] = "b03f5f7f11d50a3a",
            ["System.Net.Primitives"] = "b03f5f7f11d50a3a",
            ["System.ObjectModel"] = "b03f5f7f11d50a3a",
            ["System.Reflection"] = "b03f5f7f11d50a3a",
            ["System.Reflection.Extensions"] = "b03f5f7f11d50a3a",
            ["System.Reflection.Primitives"] = "b03f5f7f11d50a3a",
            ["System.Runtime"] = "b03f5f7f11d50a3a",
            ["System.Runtime.Extensions"] = "b03f5f7f11d50a3a",
            ["System.Runtime.Handles"] = "b03f5f7f11d50a3a",
            ["System.Runtime.InteropServices"] = "b03f5f7f11d50a3a",
            ["System.Runtime.InteropServices.RuntimeInformation"] = "b03f5f7f11d50a3a",
            ["System.Runtime.Numerics"] = "b03f5f7f11d50a3a",
            ["System.Security.Cryptography.Algorithms"] = "b03f5f7f11d50a3a",
            ["System.Security.Cryptography.Primitives"] = "b03f5f7f11d50a3a",
            ["System.Text.Encoding"] = "b03f5f7f11d50a3a",
            ["System.Text.Encoding.Extensions"] = "b03f5f7f11d50a3a",
            ["System.Threading"] = "b03f5f7f11d50a3a",
            ["System.Threading.Tasks"] = "b03f5f7f11d50a3a",
            ["System.Threading.Timer"] = "b03f5f7f11d50a3a",
            ["System.Memory"] = "cc7b13ffcd2ddd51",
            ["System.Buffers"] = "cc7b13ffcd2ddd51",
            ["System.Threading.Tasks.Extensions"] = "cc7b13ffcd2ddd51",
            ["System.Runtime.CompilerServices.Unsafe"] = "b03f5f7f11d50a3a",
            ["System.Numerics.Vectors"] = "b03f5f7f11d50a3a",
            // The backports of IAsyncEnumerable/IAsyncDisposable and HashCode, which netstandard 2.1 (and so CoreLib)
            // also declares; common netstandard2.0 library dependencies.
            ["Microsoft.Bcl.AsyncInterfaces"] = "cc7b13ffcd2ddd51",
            ["Microsoft.Bcl.HashCode"] = "cc7b13ffcd2ddd51"
        }.ToFrozenDictionary(StringComparer.Ordinal);

}
