// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: dotnet/runtime 811225a4's XPathQueryGenerator discovers the type's data contract and compares member
// paths by reflection, which NetWasm does not have (classified intentionally-unsupported). Both operations check
// their arguments as upstream does and throw PlatformNotSupportedException without touching the StringBuilder.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Xml;

namespace System.Runtime.Serialization
{
    public static class XPathQueryGenerator
    {
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public static string CreateFromDataContractSerializer(Type type, MemberInfo[] pathToMember, out XmlNamespaceManager namespaces)
        {
            return CreateFromDataContractSerializer(type, pathToMember, null, out namespaces);
        }

        // Here you can provide your own root element Xpath which will replace the Xpath of the top level element
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public static string CreateFromDataContractSerializer(Type type, MemberInfo[] pathToMember, StringBuilder? rootElementXpath, out XmlNamespaceManager namespaces)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(pathToMember);

            throw new PlatformNotSupportedException(SR.NetWasm_DataContractExportUnsupported);
        }
    }
}
