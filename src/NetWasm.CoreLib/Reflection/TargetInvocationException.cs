// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection
{
    public sealed class TargetInvocationException : ApplicationException
    {
        private const string DefaultMessage = "Exception has been thrown by the target of an invocation.";

        public TargetInvocationException(Exception? inner)
            : base(DefaultMessage, inner)
        {
            HResult = unchecked((int)0x80131604);
        }

        public TargetInvocationException(string? message, Exception? inner)
            : base(message ?? DefaultMessage, inner)
        {
            HResult = unchecked((int)0x80131604);
        }
    }
}
