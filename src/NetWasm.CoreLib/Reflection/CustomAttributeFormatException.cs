// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection
{
    public class CustomAttributeFormatException : FormatException
    {
        private const string DefaultMessage = "Binary format of the specified custom attribute was invalid.";

        public CustomAttributeFormatException()
            : this(DefaultMessage)
        {
        }

        public CustomAttributeFormatException(string? message)
            : this(message, null)
        {
        }

        public CustomAttributeFormatException(string? message, Exception? inner)
            : base(message ?? DefaultMessage, inner)
        {
            HResult = unchecked((int)0x80131605);
        }
    }
}
