// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using System.Xml.Serialization.Configuration;

namespace System.Xml.Serialization
{
    public struct XmlDeserializationEvents
    {
        private XmlNodeEventHandler? _onUnknownNode;
        private XmlAttributeEventHandler? _onUnknownAttribute;
        private XmlElementEventHandler? _onUnknownElement;
        private UnreferencedObjectEventHandler? _onUnreferencedObject;
        internal object? sender;

        public XmlNodeEventHandler? OnUnknownNode
        {
            get
            {
                return _onUnknownNode;
            }

            set
            {
                _onUnknownNode = value;
            }
        }

        public XmlAttributeEventHandler? OnUnknownAttribute
        {
            get
            {
                return _onUnknownAttribute;
            }
            set
            {
                _onUnknownAttribute = value;
            }
        }

        public XmlElementEventHandler? OnUnknownElement
        {
            get
            {
                return _onUnknownElement;
            }
            set
            {
                _onUnknownElement = value;
            }
        }

        public UnreferencedObjectEventHandler? OnUnreferencedObject
        {
            get
            {
                return _onUnreferencedObject;
            }
            set
            {
                _onUnreferencedObject = value;
            }
        }
    }

    public abstract class XmlSerializerImplementation
    {
        public virtual XmlSerializationReader Reader { get { throw new NotSupportedException(); } }

        public virtual XmlSerializationWriter Writer { get { throw new NotSupportedException(); } }

        public virtual Hashtable ReadMethods { get { throw new NotSupportedException(); } }

        public virtual Hashtable WriteMethods { get { throw new NotSupportedException(); } }

        public virtual Hashtable TypedSerializers { get { throw new NotSupportedException(); } }

        public virtual bool CanSerialize(Type type) { throw new NotSupportedException(); }

        public virtual XmlSerializer GetSerializer(Type type) { throw new NotSupportedException(); }
    }

    // This enum is intentionally kept outside of the XmlSerializer class since if it would be a subclass
    // of XmlSerializer, then any access to this enum would be treated by AOT compilers as access to the XmlSerializer
    // as well, which has a large static ctor which brings in a lot of code. So keeping the enum separate
    // makes sure that using just the enum itself doesn't bring in the whole of serialization code base.
    // NetWasm: XmlSerializer follows dotnet/runtime 811225a4 wherever no reflection or code generation is involved.
    // Upstream serializes an arbitrary type by importing it through reflection and then generating IL or running the
    // reflection-based serializer; NetWasm has neither (classified in the API policy), so:
    // - the primitive types that upstream serializes without reflection (TypeScope.PrimtiveTypes) are supported,
    //   including under an XmlRootAttribute's name and namespace or a default namespace, which upstream routes
    //   through the reflection-based serializer and which follow the same importer rules here;
    // - any other type, attribute overrides for the type, extra types, a location or a type mapping throws
    //   PlatformNotSupportedException, as do FromMappings, FromTypes and the serializer-assembly name helpers;
    // - a subclass created through the protected constructor serializes through its CreateWriter/Serialize and
    //   CreateReader/Deserialize overrides, as upstream does where it can generate code.
    public class XmlSerializer
    {
        private readonly Type? _primitiveType;
        private readonly string? _primitiveRootName;
        private readonly string _primitiveRootNamespace = string.Empty;
        private XmlDeserializationEvents _events;
        internal string? DefaultNamespace;

        private static XmlSerializerNamespaces? s_defaultNamespaces;
        private static readonly XmlWriterSettings s_writerSettings = new XmlWriterSettings() { Encoding = new UTF8Encoding(false), Indent = true };

        private static XmlSerializerNamespaces DefaultNamespaces
        {
            get
            {
                if (s_defaultNamespaces == null)
                {
                    XmlSerializerNamespaces nss = new XmlSerializerNamespaces();
                    nss.AddInternal("xsi", XmlSchema.InstanceNamespace);
                    nss.AddInternal("xsd", XmlSchema.Namespace);
                    s_defaultNamespaces ??= nss;
                }
                return s_defaultNamespaces;
            }
        }

        // Trimmer warning messages
        internal const string TrimSerializationWarning = "Members from serialized types may be trimmed if not referenced directly";
        private const string TrimDeserializationWarning = "Members from deserialized types may be trimmed if not referenced directly";
        internal const string AotSerializationWarning = "XML serializer relies on dynamic code generation which is not available with Ahead of Time compilation";

        protected XmlSerializer()
        {
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public XmlSerializer(Type type, XmlAttributeOverrides? overrides, Type[]? extraTypes, XmlRootAttribute? root, string? defaultNamespace) :
            this(type, overrides, extraTypes, root, defaultNamespace, null)
        {
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public XmlSerializer(Type type, XmlRootAttribute? root) : this(type, null, Type.EmptyTypes, root, null, null)
        {
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public XmlSerializer(Type type, Type[]? extraTypes) : this(type, null, extraTypes, null, null, null)
        {
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public XmlSerializer(Type type, XmlAttributeOverrides? overrides) : this(type, overrides, Type.EmptyTypes, null, null, null)
        {
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public XmlSerializer(XmlTypeMapping xmlTypeMapping)
        {
            ArgumentNullException.ThrowIfNull(xmlTypeMapping);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlSerializerMappingUnsupported);
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public XmlSerializer(Type type) : this(type, (string?)null)
        {
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public XmlSerializer(Type type, string? defaultNamespace)
        {
            ArgumentNullException.ThrowIfNull(type);

            DefaultNamespace = defaultNamespace;
            _primitiveType = GetSupportedPrimitiveType(type);
            _primitiveRootNamespace = defaultNamespace ?? string.Empty;
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public XmlSerializer(Type type, XmlAttributeOverrides? overrides, Type[]? extraTypes, XmlRootAttribute? root, string? defaultNamespace, string? location)
        {
            ArgumentNullException.ThrowIfNull(type);

            DefaultNamespace = defaultNamespace;
            _primitiveType = GetSupportedPrimitiveType(type);
            if ((overrides != null && overrides[type] != null) || (extraTypes != null && extraTypes.Length > 0) || location != null)
            {
                throw new PlatformNotSupportedException(SR.Format(SR.NetWasm_XmlSerializerTypeUnsupported, type.FullName));
            }

            // XmlReflectionImporter.ImportElement's rules for the root element.
            _primitiveRootNamespace = root?.Namespace ?? defaultNamespace ?? string.Empty;
            if (root != null)
            {
                if (root.ElementName.Length > 0)
                {
                    _primitiveRootName = XmlConvert.EncodeLocalName(root.ElementName);
                }

                TypeDesc typeDesc = (TypeDesc)TypeScope.PrimtiveTypes[type]!;
                if (root.GetIsNullableSpecified() && root.IsNullable != typeDesc.IsNullable)
                {
                    // Upstream writes a null value differently for a root whose nullability differs from the type's.
                    throw new PlatformNotSupportedException(SR.Format(SR.NetWasm_XmlSerializerTypeUnsupported, type.FullName));
                }
            }
        }

        private static Type GetSupportedPrimitiveType(Type type)
        {
            if (TypeScope.PrimtiveTypes[type] == null)
            {
                throw new PlatformNotSupportedException(SR.Format(SR.NetWasm_XmlSerializerTypeUnsupported, type.FullName));
            }

            return type;
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public void Serialize(TextWriter textWriter, object? o)
        {
            Serialize(textWriter, o, null);
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public void Serialize(TextWriter textWriter, object? o, XmlSerializerNamespaces? namespaces)
        {
            XmlWriter xmlWriter = XmlWriter.Create(textWriter, s_writerSettings);
            Serialize(xmlWriter, o, namespaces);
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public void Serialize(Stream stream, object? o)
        {
            Serialize(stream, o, null);
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public void Serialize(Stream stream, object? o, XmlSerializerNamespaces? namespaces)
        {
            XmlWriter xmlWriter = XmlWriter.Create(stream, s_writerSettings);
            Serialize(xmlWriter, o, namespaces);
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public void Serialize(XmlWriter xmlWriter, object? o)
        {
            Serialize(xmlWriter, o, null);
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public void Serialize(XmlWriter xmlWriter, object? o, XmlSerializerNamespaces? namespaces)
        {
            Serialize(xmlWriter, o, namespaces, null);
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public void Serialize(XmlWriter xmlWriter, object? o, XmlSerializerNamespaces? namespaces, string? encodingStyle)
        {
            Serialize(xmlWriter, o, namespaces, encodingStyle, null);
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public void Serialize(XmlWriter xmlWriter, object? o, XmlSerializerNamespaces? namespaces, string? encodingStyle, string? id)
        {
            try
            {
                if (_primitiveType != null)
                {
                    if (encodingStyle != null && encodingStyle.Length > 0)
                    {
                        throw new InvalidOperationException(SR.Format(SR.XmlInvalidEncodingNotEncoded1, encodingStyle));
                    }
                    SerializePrimitive(xmlWriter, o, namespaces);
                }
                else
                {
                    XmlSerializationWriter writer = CreateWriter();
                    writer.Init(xmlWriter, namespaces == null || namespaces.Count == 0 ? DefaultNamespaces : namespaces, encodingStyle, id);
                    Serialize(o, writer);
                }
            }
            catch (Exception? e)
            {
                if (e is TargetInvocationException)
                    e = e.InnerException;
                throw new InvalidOperationException(SR.XmlGenError, e);
            }
            xmlWriter.Flush();
        }

        [RequiresUnreferencedCode(TrimDeserializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public object? Deserialize(Stream stream)
        {
            XmlReader xmlReader = XmlReader.Create(stream, new XmlReaderSettings() { IgnoreWhitespace = true });
            return Deserialize(xmlReader, null);
        }

        [RequiresUnreferencedCode(TrimDeserializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public object? Deserialize(TextReader textReader)
        {
            XmlTextReader xmlReader = new XmlTextReader(textReader);
            xmlReader.WhitespaceHandling = WhitespaceHandling.Significant;
            xmlReader.Normalization = true;
            xmlReader.XmlResolver = null;
            return Deserialize(xmlReader, null);
        }

        [RequiresUnreferencedCode(TrimDeserializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public object? Deserialize(XmlReader xmlReader)
        {
            return Deserialize(xmlReader, null);
        }

        [RequiresUnreferencedCode(TrimDeserializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public object? Deserialize(XmlReader xmlReader, XmlDeserializationEvents events)
        {
            return Deserialize(xmlReader, null, events);
        }

        [RequiresUnreferencedCode(TrimDeserializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public object? Deserialize(XmlReader xmlReader, string? encodingStyle)
        {
            return Deserialize(xmlReader, encodingStyle, _events);
        }

        [RequiresUnreferencedCode(TrimDeserializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public object? Deserialize(XmlReader xmlReader, string? encodingStyle, XmlDeserializationEvents events)
        {
            events.sender = this;
            try
            {
                AdvancePastTopLevelEndElementIfNeeded(xmlReader);

                if (_primitiveType != null)
                {
                    if (encodingStyle != null && encodingStyle.Length > 0)
                    {
                        throw new InvalidOperationException(SR.Format(SR.XmlInvalidEncodingNotEncoded1, encodingStyle));
                    }
                    return DeserializePrimitive(xmlReader, events);
                }
                else
                {
                    XmlSerializationReader reader = CreateReader();
                    reader.Init(xmlReader, events, encodingStyle);
                    return Deserialize(reader);
                }
            }
            catch (Exception? e)
            {
                if (e is TargetInvocationException)
                    e = e.InnerException;

                if (xmlReader is IXmlLineInfo lineInfo)
                {
                    throw new InvalidOperationException(SR.Format(SR.XmlSerializeErrorDetails, lineInfo.LineNumber.ToString(CultureInfo.InvariantCulture), lineInfo.LinePosition.ToString(CultureInfo.InvariantCulture)), e);
                }
                else
                {
                    throw new InvalidOperationException(SR.XmlSerializeError, e);
                }
            }
        }

        public virtual bool CanDeserialize(XmlReader xmlReader)
        {
            AdvancePastTopLevelEndElementIfNeeded(xmlReader);

            if (_primitiveType != null)
            {
                TypeDesc typeDesc = (TypeDesc)TypeScope.PrimtiveTypes[_primitiveType]!;
                return xmlReader.IsStartElement(_primitiveRootName ?? typeDesc.DataType!.Name!, _primitiveRootNamespace);
            }
            else
            {
                return false;
            }
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public static XmlSerializer?[] FromMappings(XmlMapping[]? mappings)
        {
            return FromMappings(mappings, (Type?)null);
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public static XmlSerializer?[] FromMappings(XmlMapping[]? mappings, Type? type)
        {
            if (mappings == null || mappings.Length == 0) return Array.Empty<XmlSerializer>();
            throw new PlatformNotSupportedException(SR.NetWasm_XmlSerializerMappingUnsupported);
        }

        [RequiresUnreferencedCode(TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        public static XmlSerializer?[] FromTypes(Type[]? types)
        {
            if (types == null)
                return Array.Empty<XmlSerializer>();
            throw new PlatformNotSupportedException(SR.NetWasm_XmlSerializerMappingUnsupported);
        }

        public static string GetXmlSerializerAssemblyName(Type type)
        {
            return GetXmlSerializerAssemblyName(type, null);
        }

        public static string GetXmlSerializerAssemblyName(Type type, string? defaultNamespace)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new PlatformNotSupportedException(SR.NetWasm_XmlSerializerDynamicAssembliesUnsupported);
        }

        public event XmlNodeEventHandler UnknownNode
        {
            add
            {
                _events.OnUnknownNode += value;
            }
            remove
            {
                _events.OnUnknownNode -= value;
            }
        }

        public event XmlAttributeEventHandler UnknownAttribute
        {
            add
            {
                _events.OnUnknownAttribute += value;
            }
            remove
            {
                _events.OnUnknownAttribute -= value;
            }
        }

        public event XmlElementEventHandler UnknownElement
        {
            add
            {
                _events.OnUnknownElement += value;
            }
            remove
            {
                _events.OnUnknownElement -= value;
            }
        }

        public event UnreferencedObjectEventHandler UnreferencedObject
        {
            add
            {
                _events.OnUnreferencedObject += value;
            }
            remove
            {
                _events.OnUnreferencedObject -= value;
            }
        }

        protected virtual XmlSerializationReader CreateReader() { throw new NotImplementedException(); }

        protected virtual object Deserialize(XmlSerializationReader reader) { throw new NotImplementedException(); }

        protected virtual XmlSerializationWriter CreateWriter() { throw new NotImplementedException(); }

        protected virtual void Serialize(object? o, XmlSerializationWriter writer) { throw new NotImplementedException(); }

        private static void AdvancePastTopLevelEndElementIfNeeded(XmlReader xmlReader)
        {
            if (LocalAppContextSwitches.UseXmlSerializerReadEndElementWorkaround &&
                xmlReader.NodeType == XmlNodeType.EndElement &&
                xmlReader.Depth == 0)
            {
                xmlReader.Read();
            }
        }

        private void SerializePrimitive(XmlWriter xmlWriter, object? o, XmlSerializerNamespaces? namespaces)
        {
            XmlSerializationPrimitiveWriter writer = new XmlSerializationPrimitiveWriter();
            writer.RootName = _primitiveRootName;
            writer.RootNamespace = _primitiveRootNamespace;
            writer.Init(xmlWriter, namespaces, null, null);
            switch (Type.GetTypeCode(_primitiveType))
            {
                case TypeCode.String:
                    writer.Write_string(o);
                    break;
                case TypeCode.Int32:
                    writer.Write_int(o);
                    break;
                case TypeCode.Boolean:
                    writer.Write_boolean(o);
                    break;
                case TypeCode.Int16:
                    writer.Write_short(o);
                    break;
                case TypeCode.Int64:
                    writer.Write_long(o);
                    break;
                case TypeCode.Single:
                    writer.Write_float(o);
                    break;
                case TypeCode.Double:
                    writer.Write_double(o);
                    break;
                case TypeCode.Decimal:
                    writer.Write_decimal(o);
                    break;
                case TypeCode.DateTime:
                    writer.Write_dateTime(o);
                    break;
                case TypeCode.Char:
                    writer.Write_char(o);
                    break;
                case TypeCode.Byte:
                    writer.Write_unsignedByte(o);
                    break;
                case TypeCode.SByte:
                    writer.Write_byte(o);
                    break;
                case TypeCode.UInt16:
                    writer.Write_unsignedShort(o);
                    break;
                case TypeCode.UInt32:
                    writer.Write_unsignedInt(o);
                    break;
                case TypeCode.UInt64:
                    writer.Write_unsignedLong(o);
                    break;

                default:
                    if (_primitiveType == typeof(XmlQualifiedName))
                    {
                        writer.Write_QName(o);
                    }
                    else if (_primitiveType == typeof(byte[]))
                    {
                        writer.Write_base64Binary(o);
                    }
                    else if (_primitiveType == typeof(Guid))
                    {
                        writer.Write_guid(o);
                    }
                    else if (_primitiveType == typeof(TimeSpan))
                    {
                        writer.Write_TimeSpan(o);
                    }
                    else if (_primitiveType == typeof(DateTimeOffset))
                    {
                        writer.Write_dateTimeOffset(o);
                    }
                    else if (_primitiveType == typeof(DateOnly))
                    {
                        writer.Write_dateOnly(o);
                    }
                    else if (_primitiveType == typeof(TimeOnly))
                    {
                        writer.Write_timeOnly(o);
                    }
                    else
                    {
                        throw new InvalidOperationException(SR.Format(SR.XmlUnxpectedType, _primitiveType!.FullName));
                    }
                    break;
            }
        }

        private object? DeserializePrimitive(XmlReader xmlReader, XmlDeserializationEvents events)
        {
            XmlSerializationPrimitiveReader reader = new XmlSerializationPrimitiveReader();
            reader.RootName = _primitiveRootName;
            reader.RootNamespace = _primitiveRootNamespace;
            reader.Init(xmlReader, events, null);
            object? o;
            switch (Type.GetTypeCode(_primitiveType))
            {
                case TypeCode.String:
                    o = reader.Read_string();
                    break;
                case TypeCode.Int32:
                    o = reader.Read_int();
                    break;
                case TypeCode.Boolean:
                    o = reader.Read_boolean();
                    break;
                case TypeCode.Int16:
                    o = reader.Read_short();
                    break;
                case TypeCode.Int64:
                    o = reader.Read_long();
                    break;
                case TypeCode.Single:
                    o = reader.Read_float();
                    break;
                case TypeCode.Double:
                    o = reader.Read_double();
                    break;
                case TypeCode.Decimal:
                    o = reader.Read_decimal();
                    break;
                case TypeCode.DateTime:
                    o = reader.Read_dateTime();
                    break;
                case TypeCode.Char:
                    o = reader.Read_char();
                    break;
                case TypeCode.Byte:
                    o = reader.Read_unsignedByte();
                    break;
                case TypeCode.SByte:
                    o = reader.Read_byte();
                    break;
                case TypeCode.UInt16:
                    o = reader.Read_unsignedShort();
                    break;
                case TypeCode.UInt32:
                    o = reader.Read_unsignedInt();
                    break;
                case TypeCode.UInt64:
                    o = reader.Read_unsignedLong();
                    break;

                default:
                    if (_primitiveType == typeof(XmlQualifiedName))
                    {
                        o = reader.Read_QName();
                    }
                    else if (_primitiveType == typeof(byte[]))
                    {
                        o = reader.Read_base64Binary();
                    }
                    else if (_primitiveType == typeof(Guid))
                    {
                        o = reader.Read_guid();
                    }
                    else if (_primitiveType == typeof(TimeSpan))
                    {
                        o = reader.Read_TimeSpan();
                    }
                    else if (_primitiveType == typeof(DateTimeOffset))
                    {
                        o = reader.Read_dateTimeOffset();
                    }
                    else if (_primitiveType == typeof(DateOnly))
                    {
                        o = reader.Read_dateOnly();
                    }
                    else if (_primitiveType == typeof(TimeOnly))
                    {
                        o = reader.Read_timeOnly();
                    }
                    else
                    {
                        throw new InvalidOperationException(SR.Format(SR.XmlUnxpectedType, _primitiveType!.FullName));
                    }
                    break;
            }
            return o;
        }

    }
}
