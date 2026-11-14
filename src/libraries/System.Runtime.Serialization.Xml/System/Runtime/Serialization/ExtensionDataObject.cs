// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
//
// NetWasm deviation: only the public shape is ported, a sealed type with an internal constructor.
// Upstream's extension-data members and DataNode graph are filled by DataContractSerializer,
// which is not ported here (it needs the XML reader and writer), so nothing would read them.

namespace System.Runtime.Serialization
{
    public sealed class ExtensionDataObject
    {
        internal ExtensionDataObject()
        {
        }
    }
}
