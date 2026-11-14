// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: SpecifiedAccessor from dotnet/runtime 811225a4's Models.cs. The rest of Models.cs is the reflection
// model that XmlReflectionImporter builds, which NetWasm does not port.

namespace System.Xml.Serialization
{
    internal enum SpecifiedAccessor
    {
        None,
        ReadOnly,
        ReadWrite,
    }
}
