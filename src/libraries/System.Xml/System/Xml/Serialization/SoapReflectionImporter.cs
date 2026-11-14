// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: dotnet/runtime 811225a4's SoapReflectionImporter builds mappings by inspecting CLR attributes, members and
// constructors through reflection, which NetWasm does not have. Its constructors are upstream's; IncludeType,
// IncludeTypes and the Import* operations check their arguments and throw PlatformNotSupportedException
// (classified intentionally-unsupported in the API policy).

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace System.Xml.Serialization
{
    public class SoapReflectionImporter
    {
        public SoapReflectionImporter() : this(null, null)
        {
        }

        public SoapReflectionImporter(string? defaultNamespace) : this(null, defaultNamespace)
        {
        }

        public SoapReflectionImporter(SoapAttributeOverrides? attributeOverrides) : this(attributeOverrides, null)
        {
        }

        public SoapReflectionImporter(SoapAttributeOverrides? attributeOverrides, string? defaultNamespace)
        {
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        public void IncludeTypes(ICustomAttributeProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        public void IncludeType(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        public XmlTypeMapping ImportTypeMapping(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        public XmlTypeMapping ImportTypeMapping(Type type, string? defaultNamespace)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        public XmlMembersMapping ImportMembersMapping(string? elementName, string? ns, XmlReflectionMember[] members)
        {
            ArgumentNullException.ThrowIfNull(members);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        public XmlMembersMapping ImportMembersMapping(string? elementName, string? ns, XmlReflectionMember[] members, bool hasWrapperElement, bool writeAccessors)
        {
            ArgumentNullException.ThrowIfNull(members);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        public XmlMembersMapping ImportMembersMapping(string? elementName, string? ns, XmlReflectionMember[] members, bool hasWrapperElement, bool writeAccessors, bool validate)
        {
            ArgumentNullException.ThrowIfNull(members);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        public XmlMembersMapping ImportMembersMapping(string? elementName, string? ns, XmlReflectionMember[] members, bool hasWrapperElement, bool writeAccessors, bool validate, XmlMappingAccess access)
        {
            ArgumentNullException.ThrowIfNull(members);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlReflectionImportUnsupported);
        }
    }
}
