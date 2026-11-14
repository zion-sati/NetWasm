// Formatting without a format string, for the callers that never pass one: the primitive numbers'
// ToString(IFormatProvider) and the interpolated string handlers' holes without a format.
//
// Upstream writes a null or empty format on a fast path inside the methods that also parse one
// (FormatInt32, TryFormatInt32, FormatFloat, FormatDecimal) and marks them AggressiveInlining, so the
// JIT drops the rest when the caller's format is a constant. NetWasm decides what is reachable before
// it inlines, so those callers would keep the format string parser and the custom formatter in every
// program. These entry points write what upstream's fast paths write, and nothing else, with the
// provider's NumberFormatInfo, or the current culture's when there is no provider.

using System.Globalization;

namespace System
{
    internal static partial class Number
    {
        internal static string FormatInt32WithoutFormat(int value, IFormatProvider? provider) =>
            value >= 0
                ? UInt32ToDecStr((uint)value)
                : NegativeInt32ToDecStr(value, digits: -1, NegativeSign(provider));

        internal static string FormatInt64WithoutFormat(long value, IFormatProvider? provider) =>
            value >= 0
                ? UInt64ToDecStr((ulong)value)
                : NegativeInt64ToDecStr(value, digits: -1, NegativeSign(provider));

        internal static string FormatInt128WithoutFormat(Int128 value, IFormatProvider? provider) =>
            Int128.IsPositive(value)
                ? UInt128ToDecStr((UInt128)value, digits: -1)
                : NegativeInt128ToDecStr(value, digits: -1, NegativeSign(provider));

        // Whether TryFormatWithoutFormat writes values of T. The type tests fold for each closed T.
        internal static bool FormatsWithoutFormat<T>() =>
            typeof(T) == typeof(int) || typeof(T) == typeof(uint) || typeof(T) == typeof(long) || typeof(T) == typeof(ulong) ||
            typeof(T) == typeof(short) || typeof(T) == typeof(ushort) || typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte) ||
            typeof(T) == typeof(nint) || typeof(T) == typeof(nuint) || typeof(T) == typeof(Int128) || typeof(T) == typeof(UInt128) ||
            typeof(T) == typeof(float) || typeof(T) == typeof(double) || typeof(T) == typeof(Half) || typeof(T) == typeof(decimal);

        // What value.TryFormat(destination, out charsWritten, default, provider) writes, for a T that
        // FormatsWithoutFormat accepts.
        internal static bool TryFormatWithoutFormat<T>(T value, IFormatProvider? provider, Span<char> destination, out int charsWritten)
        {
            if (typeof(T) == typeof(int)) return TryFormatInt32WithoutFormat((int)(object)value!, provider, destination, out charsWritten);
            if (typeof(T) == typeof(short)) return TryFormatInt32WithoutFormat((short)(object)value!, provider, destination, out charsWritten);
            if (typeof(T) == typeof(sbyte)) return TryFormatInt32WithoutFormat((sbyte)(object)value!, provider, destination, out charsWritten);
            if (typeof(T) == typeof(long)) return TryFormatInt64WithoutFormat((long)(object)value!, provider, destination, out charsWritten);
            if (typeof(T) == typeof(uint)) return TryUInt32ToDecStr((uint)(object)value!, destination, out charsWritten);
            if (typeof(T) == typeof(ushort)) return TryUInt32ToDecStr((ushort)(object)value!, destination, out charsWritten);
            if (typeof(T) == typeof(byte)) return TryUInt32ToDecStr((byte)(object)value!, destination, out charsWritten);
            if (typeof(T) == typeof(ulong)) return TryUInt64ToDecStr((ulong)(object)value!, destination, out charsWritten);
            if (typeof(T) == typeof(nint))
            {
                return IntPtr.Size == 8
                    ? TryFormatInt64WithoutFormat((nint)(object)value!, provider, destination, out charsWritten)
                    : TryFormatInt32WithoutFormat((int)(nint)(object)value!, provider, destination, out charsWritten);
            }
            if (typeof(T) == typeof(nuint)) return TryUInt64ToDecStr((nuint)(object)value!, destination, out charsWritten);
            if (typeof(T) == typeof(Int128))
            {
                var wide = (Int128)(object)value!;
                return Int128.IsPositive(wide)
                    ? TryUInt128ToDecStr((UInt128)wide, digits: -1, destination, out charsWritten)
                    : TryNegativeInt128ToDecStr(wide, digits: -1, NegativeSignChars(provider), destination, out charsWritten);
            }
            if (typeof(T) == typeof(UInt128)) return TryUInt128ToDecStr((UInt128)(object)value!, digits: -1, destination, out charsWritten);
            if (typeof(T) == typeof(float)) return TryFormatShortestFloat((float)(object)value!, destination, out charsWritten, Info(provider));
            if (typeof(T) == typeof(double)) return TryFormatShortestFloat((double)(object)value!, destination, out charsWritten, Info(provider));
            if (typeof(T) == typeof(Half)) return TryFormatShortestFloat((Half)(object)value!, destination, out charsWritten, Info(provider));
            if (typeof(T) == typeof(decimal)) return decimal.TryFormatWithoutFormat((decimal)(object)value!, Info(provider), destination, out charsWritten);
            throw new InvalidOperationException();
        }

        // The provider's NumberFormatInfo, or none for the current culture's, as FormatFloat takes it.
        private static NumberFormatInfo? Info(IFormatProvider? provider) =>
            provider is null ? null : NumberFormatInfo.GetInstance(provider);

        // The provider's negative sign, or the current culture's mirror of it without a provider, so
        // a hole without one consults no culture.
        private static string NegativeSign(IFormatProvider? provider) =>
            provider is null ? NumberFormatInfo.CurrentNegativeSign : NumberFormatInfo.GetInstance(provider).NegativeSign;

        private static ReadOnlySpan<char> NegativeSignChars(IFormatProvider? provider) =>
            provider is null
                ? NumberFormatInfo.CurrentNegativeSignTChar<char>()
                : NumberFormatInfo.GetInstance(provider).NegativeSignTChar<char>();

        private static bool TryFormatInt32WithoutFormat(int value, IFormatProvider? provider, Span<char> destination, out int charsWritten) =>
            value >= 0
                ? TryUInt32ToDecStr((uint)value, destination, out charsWritten)
                : TryNegativeInt32ToDecStr(value, digits: -1, NegativeSignChars(provider), destination, out charsWritten);

        private static bool TryFormatInt64WithoutFormat(long value, IFormatProvider? provider, Span<char> destination, out int charsWritten) =>
            value >= 0
                ? TryUInt64ToDecStr((ulong)value, destination, out charsWritten)
                : TryNegativeInt64ToDecStr(value, digits: -1, NegativeSignChars(provider), destination, out charsWritten);
    }
}
