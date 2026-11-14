// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.ComponentModel;

namespace System.Runtime.InteropServices
{
    // Wrapper that is converted to a variant with VT_ERROR.
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class ErrorWrapper
    {
        public ErrorWrapper(int errorCode)
        {
            ErrorCode = errorCode;
        }

        public ErrorWrapper(object errorCode)
        {
            if (errorCode is not int i)
                throw new ArgumentException(SR.Arg_MustBeInt32, nameof(errorCode));
            ErrorCode = i;
        }

        public ErrorWrapper(Exception e)
        {
            ErrorCode = Marshal.GetHRForException(e);
        }

        public int ErrorCode { get; }
    }
}
