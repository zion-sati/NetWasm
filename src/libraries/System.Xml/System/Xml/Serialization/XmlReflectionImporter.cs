// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: dotnet/runtime 811225a4's XmlReflectionImporter builds mappings by inspecting CLR attributes, members and
// constructors through reflection, which NetWasm does not have. Its constructors are upstream's; IncludeType,
// IncludeTypes and the Import* operations check their arguments and throw PlatformNotSupportedException
// (classified intentionally-unsupported in the API policy).

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace System.Xml.Serialization
{
    public class XmlReflectionImporter
    {
        public XmlReflectionImporter() : this(null, null)
        {
        }

        public XmlReflectionImporter(string? defaultNamespace) : this(null, defaultNamespace)
        {
        }

        public XmlReflectionImporter(XmlAttributeOverrides? attributeOverrides) : this(attributeOverrides, null)
        {
        }

        public XmlReflectionImporter(XmlAttributeOverrides? attributeOverrides, string? defaultNamespace)
        {
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public void IncludeTypes(ICustomAttributeProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public void IncludeType(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public XmlTypeMapping ImportTypeMapping(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public XmlTypeMapping ImportTypeMapping(Type type, string? defaultNamespace)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public XmlTypeMapping ImportTypeMapping(Type type, XmlRootAttribute? root)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public XmlTypeMapping ImportTypeMapping(Type type, XmlRootAttribute? root, string? defaultNamespace)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public XmlMembersMapping ImportMembersMapping(string? elementName, string? ns, XmlReflectionMember[] members, bool hasWrapperElement)
        {
            ArgumentNullException.ThrowIfNull(members);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public XmlMembersMapping ImportMembersMapping(string? elementName, string? ns, XmlReflectionMember[] members, bool hasWrapperElement, bool rpc)
        {
            ArgumentNullException.ThrowIfNull(members);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public XmlMembersMapping ImportMembersMapping(string? elementName, string? ns, XmlReflectionMember[] members, bool hasWrapperElement, bool rpc, bool openModel)
        {
            ArgumentNullException.ThrowIfNull(members);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public XmlMembersMapping ImportMembersMapping(string? elementName, string? ns, XmlReflectionMember[] members, bool hasWrapperElement, bool rpc, bool openModel, XmlMappingAccess access)
        {
            ArgumentNullException.ThrowIfNull(members);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }
    }
}
