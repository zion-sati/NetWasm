// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace System
{
    [StackTraceHidden]
    internal static class ThrowHelper
    {
        [DoesNotReturn]
        internal static void ThrowOverflowException()
        {
            throw new OverflowException();
        }

        [DoesNotReturn]
        internal static void ThrowNotSupportedException()
        {
            throw new NotSupportedException();
        }

        [DoesNotReturn]
        internal static void ThrowValueArgumentOutOfRange_NeedNonNegNumException()
        {
            throw new ArgumentOutOfRangeException("value", SR.ArgumentOutOfRange_NeedNonNegNum);
        }

        [DoesNotReturn]
        internal static void ThrowFormatException_BadFormatSpecifier()
        {
            throw new FormatException(SR.Argument_BadFormatSpecifier);
        }

        [DoesNotReturn]
        internal static void ThrowUnreachableException()
        {
            throw new UnreachableException();
        }
    }
}
