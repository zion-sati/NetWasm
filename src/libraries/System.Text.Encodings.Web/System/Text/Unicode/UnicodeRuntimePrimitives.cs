// NetWasm adaptation: the upstream encoder consumes these tiny CoreLib-only
// assertion and scalar predicates. They remain internal and carry no runtime
// or platform dependency.

namespace System.Text.Unicode;

internal static class Debug
{
    internal static void Assert(bool condition, string? message = null) { }
}

internal static class UnicodeDebug
{
    internal static void AssertIsBmpCodePoint(uint value) { }
    internal static void AssertIsValidScalar(uint value) { }
    internal static void AssertIsValidSupplementaryPlaneScalar(uint value) { }
}

internal static class UnicodeUtility
{
    internal static bool IsBmpCodePoint(uint value) => value <= char.MaxValue;
    internal static bool IsAsciiCodePoint(byte value) => value <= 0x7F;
}
