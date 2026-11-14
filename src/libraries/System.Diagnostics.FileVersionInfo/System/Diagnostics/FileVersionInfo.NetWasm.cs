// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm platform layer for FileVersionInfo, in place of dotnet/runtime commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce FileVersionInfo.Unix.cs. GetVersionInfo keeps upstream's
// path and existence checks; reading version resources or assembly metadata from the file is not
// available, so loading an existing file throws PlatformNotSupportedException.

namespace System.Diagnostics
{
    public sealed partial class FileVersionInfo
    {
        private FileVersionInfo(string fileName)
        {
            _fileName = fileName;
            throw new PlatformNotSupportedException(SR.FileVersionInfoNotSupported);
        }
    }
}
