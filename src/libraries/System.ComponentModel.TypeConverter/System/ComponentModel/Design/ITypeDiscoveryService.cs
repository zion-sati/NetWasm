// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Collections;

namespace System.ComponentModel.Design
{
    /// <summary>
    /// The type discovery service is used to discover available types at design time,
    /// when the consumer doesn't know the names of existing types or referenced assemblies.
    /// </summary>
    public interface ITypeDiscoveryService
    {
        /// <summary>
        /// Retrieves the list of available types. If baseType is null, all
        /// types are returned. Otherwise, only types deriving from the
        /// specified base type are returned. If bool excludeGlobalTypes is false,
        /// types from all referenced assemblies are checked. Otherwise,
        /// only types from non-GAC referenced assemblies are checked.
        /// </summary>
        ICollection GetTypes(Type? baseType, bool excludeGlobalTypes);
    }
}
