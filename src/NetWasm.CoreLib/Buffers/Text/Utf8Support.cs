// Compatibility support for the scalar Utf8Parser/Utf8Formatter port.
// The upstream implementation is licensed under MIT by the .NET Foundation.

namespace System
{
    internal static class ThrowHelper
    {
        internal static void ThrowFormatException_BadFormatSpecifier() => throw new FormatException();
        internal static void ThrowArgumentNullException(ExceptionArgument argument) =>
            throw new ArgumentNullException(argument.ToString());
        internal static void ThrowDivideByZeroException() => throw new DivideByZeroException();
        internal static void ThrowNotSupportedException() => throw new NotSupportedException();
        internal static void ThrowOverflowException() => throw new OverflowException();
        internal static void ThrowValueArgumentOutOfRange_NeedNonNegNumException() =>
            throw new ArgumentOutOfRangeException("value");
    }

    internal enum ExceptionArgument
    {
        s,
    }
}
