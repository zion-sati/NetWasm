namespace NetWasm.Hosting.Build.Tests;

// Issue #139: a generated browser module binds no browser API while it
// imports. Bound at module scope, a missing one, such as crypto.subtle outside
// a secure context, fails the import itself, where the page cannot catch it;
// the hosting module's actions look the APIs up when they run instead.
internal static class BrowserApiBindings
{
    public static void AssertNoneAtImport(string source)
    {
        foreach (var api in new[] { "crypto.", "WebAssembly.", "globalThis.fetch", "URL.revokeObjectURL", ".bind(" })
            Assert.DoesNotContain(api, source, StringComparison.Ordinal);
    }
}
