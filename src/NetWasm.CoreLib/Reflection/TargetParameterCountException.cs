// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection
{
    public sealed class TargetParameterCountException : ApplicationException
    {
        private const string DefaultMessage = "Parameter count mismatch.";

        public TargetParameterCountException()
            : base(DefaultMessage)
        {
            HResult = unchecked((int)0x8002000E);
        }

        public TargetParameterCountException(string? message)
            : base(message ?? DefaultMessage)
        {
            HResult = unchecked((int)0x8002000E);
        }

        public TargetParameterCountException(string? message, Exception? inner)
            : base(message ?? DefaultMessage, inner)
        {
            HResult = unchecked((int)0x8002000E);
        }
    }
}
