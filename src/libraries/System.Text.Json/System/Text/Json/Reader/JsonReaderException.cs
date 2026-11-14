// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Text.Json
{
    // This class exists because the serializer needs to catch reader-originated exceptions in order to throw JsonException which has Path information.
    internal sealed class JsonReaderException : JsonException
    {
        public JsonReaderException(string message, long lineNumber, long bytePositionInLine) : base(message, path: null, lineNumber, bytePositionInLine)
        {
        }
    }
}
