// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.IO
{
    // Provides seek reference points. To seek to the end of a stream, call
    // stream.Seek(0, SeekOrigin.End).
    public enum SeekOrigin
    {
        Begin = 0,
        Current = 1,
        End = 2,
    }
}
