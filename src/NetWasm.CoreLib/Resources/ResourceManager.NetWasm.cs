// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm part of ResourceManager. Upstream's default groveler reads an assembly's manifest
// resources or satellite assemblies; NetWasm cannot load resources that way, so lookup that
// reaches the default groveler throws PlatformNotSupportedException. Subclasses that supply
// their sets through InternalGetResourceSet never reach it. The culture-name helpers are
// upstream CultureInfo internals (HasInvariantCultureName, VerifyCultureName at
// dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce) that NetWasm's CultureInfo
// does not declare.

using System.Collections.Generic;
using System.Globalization;

namespace System.Resources
{
    public partial class ResourceManager
    {
        private static bool HasInvariantCultureName(CultureInfo culture) => culture.Name.Length == 0;

        private static void VerifyCultureName(string cultureName)
        {
            // ResourceManager searches for resources using CultureInfo.Name,
            // so we should check against CultureInfo.Name.
            for (int i = 0; i < cultureName.Length; i++)
            {
                char c = cultureName[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                {
                    continue;
                }

                throw new ArgumentException(SR.Format(SR.Argument_InvalidResourceCultureName, cultureName));
            }
        }

        private sealed class UnavailableResourceGroveler : IResourceGroveler
        {
            public ResourceSet? GrovelForResourceSet(CultureInfo culture, Dictionary<string, ResourceSet> localResourceSets,
                bool tryParents, bool createIfNotExists) =>
                throw new PlatformNotSupportedException(SR.PlatformNotSupported_ResourceManagerAssembly);
        }
    }
}
