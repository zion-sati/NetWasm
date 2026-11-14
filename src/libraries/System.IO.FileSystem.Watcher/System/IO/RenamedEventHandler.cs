// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.IO
{
    /// <devdoc>
    ///    Represents the method that will handle the <see cref='System.IO.FileSystemWatcher.Renamed'/> event of a <see cref='System.IO.FileSystemWatcher'/> class.
    /// </devdoc>
    public delegate void RenamedEventHandler(object sender, RenamedEventArgs e);
}
