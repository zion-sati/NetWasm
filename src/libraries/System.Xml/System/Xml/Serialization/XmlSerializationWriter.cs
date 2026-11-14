// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace System.Xml.Serialization
{
    ///<internalonly/>
    public abstract class XmlSerializationWriter : XmlSerializationGeneratedCode
    {
        private XmlWriter _w = null!;
        private XmlSerializerNamespaces? _namespaces;
        private int _tempNamespacePrefix;
        private HashSet<int>? _usedPrefixes;
        private Hashtable? _references;
        private string? _idBase;
        private int _nextId;
        private Hashtable? _typeEntries;
        private ArrayList? _referencesToWrite;
        private Hashtable? _objectsInUse;
        private readonly string _aliasBase = "q";
        private bool _soap12;
        private bool _escapeName = true;

        //char buffer for serializing primitive values
        private readonly char[] _primitivesBuffer = new char[64];

        // this method must be called before any generated serialization methods are called
        internal void Init(XmlWriter w, XmlSerializerNamespaces? namespaces, string? encodingStyle, string? idBase)
        {
            _w = w;
            _namespaces = namespaces;
            _soap12 = (encodingStyle == Soap12.Encoding);
            _idBase = idBase;
        }

        protected bool EscapeName
        {
            get
            {
                return _escapeName;
            }
            set
            {
                _escapeName = value;
            }
        }

        protected XmlWriter Writer
        {
            get
            {
                return _w;
            }
            set
            {
                _w = value;
            }
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        protected ArrayList? Namespaces
        {
            get
            {
                return _namespaces?.NamespaceList;
            }

            [UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "ToArray is called for known reference types only.")]
            set
            {
                if (value == null)
                {
                    _namespaces = null;
                }
                else
                {
                    XmlQualifiedName[] qnames = (XmlQualifiedName[])value.ToArray(typeof(XmlQualifiedName));
                    _namespaces = new XmlSerializerNamespaces(qnames);
                }
            }
        }

        protected static byte[] FromByteArrayBase64(byte[] value)
        {
            // Unlike other "From" functions that one is just a place holder for automatic code generation.
            // The reason is performance and memory consumption for (potentially) big 64base-encoded chunks
            // And it is assumed that the caller generates the code that will distinguish between byte[] and string return types
            //
            return value;
        }

        ///<internalonly/>
        // NetWasm: there are no dynamically generated serializer assemblies to resolve (classified
        // intentionally-unsupported in the API policy).
        protected static Assembly? ResolveDynamicAssembly(string assemblyFullName)
        {
            throw new PlatformNotSupportedException(SR.NetWasm_XmlSerializerDynamicAssembliesUnsupported);
        }

        [return: NotNullIfNotNull(nameof(value))]
        protected static string? FromByteArrayHex(byte[]? value)
        {
            return XmlCustomFormatter.FromByteArrayHex(value);
        }

        protected static string FromDateTime(DateTime value)
        {
            return XmlCustomFormatter.FromDateTime(value);
        }

        internal static bool TryFormatDateTime(DateTime value, Span<char> destination, out int charsWritten)
        {
            return XmlCustomFormatter.TryFormatDateTime(value, destination, out charsWritten);
        }

        protected static string FromDate(DateTime value)
        {
            return XmlCustomFormatter.FromDate(value);
        }

        protected static string FromTime(DateTime value)
        {
            return XmlCustomFormatter.FromTime(value);
        }

        protected static string FromDateOnly(DateOnly value)
        {
            return XmlCustomFormatter.FromDateOnly(value);
        }

        protected static string FromTimeOnly(TimeOnly value)
        {
            return XmlCustomFormatter.FromTimeOnly(value);
        }

        protected static string FromTimeOnlyIgnoreOffset(TimeOnly value)
        {
            return XmlCustomFormatter.FromTimeOnlyIgnoreOffset(value);
        }

        internal static bool TryFormatDateOnly(DateOnly value, Span<char> destination, out int charsWritten)
        {
            return XmlCustomFormatter.TryFormatDateOnly(value, destination, out charsWritten);
        }

        internal static bool TryFormatTimeOnly(TimeOnly value, Span<char> destination, out int charsWritten)
        {
            return XmlCustomFormatter.TryFormatTimeOnly(value, destination, out charsWritten);
        }

        protected static string FromChar(char value)
        {
            return XmlCustomFormatter.FromChar(value);
        }

        protected static string FromEnum(long value, string[] values, long[] ids)
        {
            return XmlCustomFormatter.FromEnum(value, values, ids, null);
        }

        protected static string FromEnum(long value, string[] values, long[] ids, string typeName)
        {
            return XmlCustomFormatter.FromEnum(value, values, ids, typeName);
        }

        [return: NotNullIfNotNull(nameof(name))]
        protected static string? FromXmlName(string? name)
        {
            return XmlCustomFormatter.FromXmlName(name);
        }

        [return: NotNullIfNotNull(nameof(ncName))]
        protected static string? FromXmlNCName(string? ncName)
        {
            return XmlCustomFormatter.FromXmlNCName(ncName);
        }

        [return: NotNullIfNotNull(nameof(nmToken))]
        protected static string? FromXmlNmToken(string? nmToken)
        {
            return XmlCustomFormatter.FromXmlNmToken(nmToken);
        }

        [return: NotNullIfNotNull(nameof(nmTokens))]
        protected static string? FromXmlNmTokens(string? nmTokens)
        {
            return XmlCustomFormatter.FromXmlNmTokens(nmTokens);
        }

        protected void WriteXsiType(string name, string? ns)
        {
            WriteAttribute("type", XmlSchema.InstanceNamespace, GetQualifiedName(name, ns));
        }

        [RequiresUnreferencedCode("calls GetPrimitiveTypeName")]
        private XmlQualifiedName GetPrimitiveTypeName(Type type)
        {
            return GetPrimitiveTypeName(type, true)!;
        }

        [RequiresUnreferencedCode("calls CreateUnknownTypeException")]
        private XmlQualifiedName? GetPrimitiveTypeName(Type type, bool throwIfUnknown)
        {
            XmlQualifiedName? qname = GetPrimitiveTypeNameInternal(type);
            if (throwIfUnknown && qname == null)
                throw CreateUnknownTypeException(type);
            return qname;
        }

        internal static XmlQualifiedName? GetPrimitiveTypeNameInternal(Type type)
        {
            string typeName;
            string typeNs = XmlSchema.Namespace;

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.String: typeName = "string"; break;
                case TypeCode.Int32: typeName = "int"; break;
                case TypeCode.Boolean: typeName = "boolean"; break;
                case TypeCode.Int16: typeName = "short"; break;
                case TypeCode.Int64: typeName = "long"; break;
                case TypeCode.Single: typeName = "float"; break;
                case TypeCode.Double: typeName = "double"; break;
                case TypeCode.Decimal: typeName = "decimal"; break;
                case TypeCode.DateTime: typeName = "dateTime"; break;
                case TypeCode.Byte: typeName = "unsignedByte"; break;
                case TypeCode.SByte: typeName = "byte"; break;
                case TypeCode.UInt16: typeName = "unsignedShort"; break;
                case TypeCode.UInt32: typeName = "unsignedInt"; break;
                case TypeCode.UInt64: typeName = "unsignedLong"; break;
                case TypeCode.Char:
                    typeName = "char";
                    typeNs = UrtTypes.Namespace;
                    break;
                default:
                    if (type == typeof(XmlQualifiedName)) typeName = "QName";
                    else if (type == typeof(byte[])) typeName = "base64Binary";
                    else if (type == typeof(Guid))
                    {
                        typeName = "guid";
                        typeNs = UrtTypes.Namespace;
                    }
                    else if (type == typeof(TimeSpan))
                    {
                        typeName = "TimeSpan";
                        typeNs = UrtTypes.Namespace;
                    }
                    else if (type == typeof(DateTimeOffset))
                    {
                        typeName = "dateTimeOffset";
                        typeNs = UrtTypes.Namespace;
                    }
                    else if (type == typeof(DateOnly))
                    {
                        typeName = "dateOnly";
                        typeNs = UrtTypes.Namespace;
                    }
                    else if (type == typeof(TimeOnly))
                    {
                        typeName = "timeOnly";
                        typeNs = UrtTypes.Namespace;
                    }
                    else if (type == typeof(XmlNode[]))
                    {
                        typeName = Soap.UrType;
                    }
                    else
                        return null;
                    break;
            }
            return new XmlQualifiedName(typeName, typeNs);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        protected void WriteTypedPrimitive(string? name, string? ns, object o, bool xsiType)
        {
            string? value = null;
            string type;
            string typeNs = XmlSchema.Namespace;
            bool writeRaw = true;
            bool writeDirect = false;
            Type t = o.GetType();
            bool wroteStartElement = false;
            bool? tryFormatResult = null;
            int charsWritten = -1;

            switch (Type.GetTypeCode(t))
            {
                case TypeCode.String:
                    value = (string)o;
                    type = "string";
                    writeRaw = false;
                    break;
                case TypeCode.Int32:
                    tryFormatResult = XmlConvert.TryFormat((int)o, _primitivesBuffer, out charsWritten);
                    type = "int";
                    break;
                case TypeCode.Boolean:
                    tryFormatResult = XmlConvert.TryFormat((bool)o, _primitivesBuffer, out charsWritten);
                    type = "boolean";
                    break;
                case TypeCode.Int16:
                    tryFormatResult = XmlConvert.TryFormat((short)o, _primitivesBuffer, out charsWritten);
                    type = "short";
                    break;
                case TypeCode.Int64:
                    tryFormatResult = XmlConvert.TryFormat((long)o, _primitivesBuffer, out charsWritten);
                    type = "long";
                    break;
                case TypeCode.Single:
                    tryFormatResult = XmlConvert.TryFormat((float)o, _primitivesBuffer, out charsWritten);
                    type = "float";
                    break;
                case TypeCode.Double:
                    tryFormatResult = XmlConvert.TryFormat((double)o, _primitivesBuffer, out charsWritten);
                    type = "double";
                    break;
                case TypeCode.Decimal:
                    tryFormatResult = XmlConvert.TryFormat((decimal)o, _primitivesBuffer, out charsWritten);
                    type = "decimal";
                    break;
                case TypeCode.DateTime:
                    tryFormatResult = TryFormatDateTime((DateTime)o, _primitivesBuffer, out charsWritten);
                    type = "dateTime";
                    break;
                case TypeCode.Char:
                    tryFormatResult = XmlConvert.TryFormat((ushort)(char)o, _primitivesBuffer, out charsWritten);
                    type = "char";
                    typeNs = UrtTypes.Namespace;
                    break;
                case TypeCode.Byte:
                    tryFormatResult = XmlConvert.TryFormat((byte)o, _primitivesBuffer, out charsWritten);
                    type = "unsignedByte";
                    break;
                case TypeCode.SByte:
                    tryFormatResult = XmlConvert.TryFormat((sbyte)o, _primitivesBuffer, out charsWritten);
                    type = "byte";
                    break;
                case TypeCode.UInt16:
                    tryFormatResult = XmlConvert.TryFormat((ushort)o, _primitivesBuffer, out charsWritten);
                    type = "unsignedShort";
                    break;
                case TypeCode.UInt32:
                    tryFormatResult = XmlConvert.TryFormat((uint)o, _primitivesBuffer, out charsWritten);
                    type = "unsignedInt";
                    break;
                case TypeCode.UInt64:
                    tryFormatResult = XmlConvert.TryFormat((ulong)o, _primitivesBuffer, out charsWritten);
                    type = "unsignedLong";
                    break;

                default:
                    if (t == typeof(XmlQualifiedName))
                    {
                        type = "QName";
                        // need to write start element ahead of time to establish context
                        // for ns definitions by FromXmlQualifiedName
                        wroteStartElement = true;
                        if (name == null)
                            _w.WriteStartElement(type, typeNs);
                        else
                            _w.WriteStartElement(name, ns);
                        value = FromXmlQualifiedName((XmlQualifiedName)o, false);
                    }
                    else if (t == typeof(byte[]))
                    {
                        value = string.Empty;
                        writeDirect = true;
                        type = "base64Binary";
                    }
                    else if (t == typeof(Guid))
                    {
                        tryFormatResult = XmlConvert.TryFormat((Guid)o, _primitivesBuffer, out charsWritten);
                        type = "guid";
                        typeNs = UrtTypes.Namespace;
                    }
                    else if (t == typeof(TimeSpan))
                    {
                        tryFormatResult = XmlConvert.TryFormat((TimeSpan)o, _primitivesBuffer, out charsWritten);
                        type = "TimeSpan";
                        typeNs = UrtTypes.Namespace;
                    }
                    else if (t == typeof(DateTimeOffset))
                    {
                        tryFormatResult = XmlConvert.TryFormat((DateTimeOffset)o, _primitivesBuffer, out charsWritten);
                        type = "dateTimeOffset";
                        typeNs = UrtTypes.Namespace;
                    }
                    else if (t == typeof(DateOnly))
                    {
                        tryFormatResult = TryFormatDateOnly((DateOnly)o, _primitivesBuffer, out charsWritten);
                        type = "dateOnly";
                        typeNs = UrtTypes.Namespace;
                    }
                    else if (t == typeof(TimeOnly))
                    {
                        tryFormatResult = TryFormatTimeOnly((TimeOnly)o, _primitivesBuffer, out charsWritten);
                        type = "timeOnly";
                        typeNs = UrtTypes.Namespace;
                    }
                    else if (typeof(XmlNode[]).IsAssignableFrom(t))
                    {
                        if (name == null)
                            _w.WriteStartElement(Soap.UrType, XmlSchema.Namespace);
                        else
                            _w.WriteStartElement(name, ns);

                        XmlNode[] xmlNodes = (XmlNode[])o;
                        for (int i = 0; i < xmlNodes.Length; i++)
                        {
                            if (xmlNodes[i] == null)
                                continue;
                            xmlNodes[i].WriteTo(_w);
                        }
                        _w.WriteEndElement();
                        return;
                    }
                    else
                    {
                        throw CreateUnknownTypeException(t);
                    }

                    break;
            }
            if (!wroteStartElement)
            {
                if (name == null)
                    _w.WriteStartElement(type, typeNs);
                else
                    _w.WriteStartElement(name, ns);
            }

            if (xsiType) WriteXsiType(type, typeNs);

            if (writeDirect)
            {
                // only one type currently writes directly to XML stream
                XmlCustomFormatter.WriteArrayBase64(_w, (byte[])o, 0, ((byte[])o).Length);
            }
            else if (tryFormatResult != null)
            {
                Debug.Assert(tryFormatResult.Value, "Something goes wrong with formatting primitives to the buffer.");
#if DEBUG
                const string escapeChars = "<>\"'&";
                ReadOnlySpan<char> span = _primitivesBuffer;
                Debug.Assert(!span.Slice(0, charsWritten).ContainsAny(escapeChars), "Primitive value contains illegal xml char.");
#endif
                //all the primitive types except string and XmlQualifiedName writes to the buffer
                _w.WriteRaw(_primitivesBuffer, 0, charsWritten);
            }
            else
            {
                if (value == null)
                    _w.WriteAttributeString("nil", XmlSchema.InstanceNamespace, "true");
                else if (writeRaw)
                {
                    _w.WriteRaw(value);
                }
                else
                    _w.WriteString(value);
            }

            _w.WriteEndElement();
        }

        private string GetQualifiedName(string name, string? ns)
        {
            if (string.IsNullOrEmpty(ns)) return name;
            string? prefix = _w.LookupPrefix(ns);
            if (prefix == null)
            {
                if (ns == XmlReservedNs.NsXml)
                {
                    prefix = "xml";
                }
                else
                {
                    prefix = NextPrefix();
                    WriteAttribute("xmlns", prefix, null, ns);
                }
            }
            else if (prefix.Length == 0)
            {
                return name;
            }
            return $"{prefix}:{name}";
        }

        protected string? FromXmlQualifiedName(XmlQualifiedName? xmlQualifiedName)
        {
            return FromXmlQualifiedName(xmlQualifiedName, true);
        }

        protected string? FromXmlQualifiedName(XmlQualifiedName? xmlQualifiedName, bool ignoreEmpty)
        {
            if (xmlQualifiedName == null) return null;
            if (xmlQualifiedName.IsEmpty && ignoreEmpty) return null;
            return GetQualifiedName(EscapeName ? XmlConvert.EncodeLocalName(xmlQualifiedName.Name) : xmlQualifiedName.Name, xmlQualifiedName.Namespace);
        }

        protected void WriteStartElement(string name)
        {
            WriteStartElement(name, null, null, false, null);
        }

        protected void WriteStartElement(string name, string? ns)
        {
            WriteStartElement(name, ns, null, false, null);
        }

        protected void WriteStartElement(string name, string? ns, bool writePrefixed)
        {
            WriteStartElement(name, ns, null, writePrefixed, null);
        }

        protected void WriteStartElement(string name, string? ns, object? o)
        {
            WriteStartElement(name, ns, o, false, null);
        }

        protected void WriteStartElement(string name, string? ns, object? o, bool writePrefixed)
        {
            WriteStartElement(name, ns, o, writePrefixed, null);
        }

        protected void WriteStartElement(string name, string? ns, object? o, bool writePrefixed, XmlSerializerNamespaces? xmlns)
        {
            if (o != null && _objectsInUse != null)
            {
                if (_objectsInUse.ContainsKey(o)) throw new InvalidOperationException(SR.Format(SR.XmlCircularReference, o.GetType().FullName));
                _objectsInUse.Add(o, o);
            }

            string? prefix = null;
            bool needEmptyDefaultNamespace = false;
            if (_namespaces != null)
            {
                _namespaces.TryLookupPrefix(ns, out prefix);

                if (_namespaces.TryLookupNamespace("", out string? defaultNS))
                {
                    if (string.IsNullOrEmpty(defaultNS))
                        needEmptyDefaultNamespace = true;
                    if (ns != defaultNS)
                        writePrefixed = true;
                }

                _usedPrefixes = ListUsedPrefixes(_namespaces, _aliasBase);
            }
            if (writePrefixed && prefix == null && ns != null && ns.Length > 0)
            {
                prefix = _w.LookupPrefix(ns);
                if (string.IsNullOrEmpty(prefix))
                {
                    prefix = NextPrefix();
                }
            }
            if (prefix == null && xmlns != null)
            {
                xmlns.TryLookupPrefix(ns, out prefix);
            }
            if (needEmptyDefaultNamespace && prefix == null && !string.IsNullOrEmpty(ns))
                prefix = NextPrefix();
            _w.WriteStartElement(prefix, name, ns);
            if (_namespaces != null)
            {
                foreach (XmlQualifiedName qname in _namespaces.Namespaces)
                {
                    string alias = qname.Name;
                    string? aliasNs = qname.Namespace;
                    if (alias.Length == 0 && string.IsNullOrEmpty(aliasNs))
                        continue;
                    if (string.IsNullOrEmpty(aliasNs))
                    {
                        if (alias.Length > 0)
                            throw new InvalidOperationException(SR.Format(SR.XmlInvalidXmlns, alias));
                        WriteAttribute(nameof(xmlns), alias, null, aliasNs);
                    }
                    else
                    {
                        if (_w.LookupPrefix(aliasNs) == null)
                        {
                            // write the default namespace declaration only if we have not written it already, over wise we just ignore one provided by the user
                            if (prefix == null && alias.Length == 0)
                                break;
                            WriteAttribute(nameof(xmlns), alias, null, aliasNs);
                        }
                    }
                }
            }
            WriteNamespaceDeclarations(xmlns);
        }

        private static HashSet<int>? ListUsedPrefixes(XmlSerializerNamespaces nsList, string prefix)
        {
            var qnIndexes = new HashSet<int>();
            int prefixLength = prefix.Length;
            const string MaxInt32 = "2147483647";
            foreach (XmlQualifiedName qname in nsList.Namespaces)
            {
                if (qname.Name.Length > prefixLength)
                {
                    string name = qname.Name;
                    if (name.Length > prefixLength && name.Length <= prefixLength + MaxInt32.Length && name.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        bool numeric = true;
                        for (int j = prefixLength; j < name.Length; j++)
                        {
                            if (!char.IsDigit(name, j))
                            {
                                numeric = false;
                                break;
                            }
                        }
                        if (numeric)
                        {
                            long index = long.Parse(name.AsSpan(prefixLength), NumberStyles.Integer, CultureInfo.InvariantCulture);
                            if (index <= int.MaxValue)
                            {
                                int newIndex = (int)index;
                                qnIndexes.Add(newIndex);
                            }
                        }
                    }
                }
            }
            if (qnIndexes.Count > 0)
            {
                return qnIndexes;
            }
            return null;
        }

        protected void WriteNullTagEncoded(string? name)
        {
            WriteNullTagEncoded(name, null);
        }

        protected void WriteNullTagEncoded(string? name, string? ns)
        {
            if (string.IsNullOrEmpty(name))
                return;
            WriteStartElement(name, ns, null, true);
            _w.WriteAttributeString("nil", XmlSchema.InstanceNamespace, "true");
            _w.WriteEndElement();
        }

        protected void WriteNullTagLiteral(string? name)
        {
            WriteNullTagLiteral(name, null);
        }

        protected void WriteNullTagLiteral(string? name, string? ns)
        {
            if (string.IsNullOrEmpty(name))
                return;
            WriteStartElement(name, ns, null, false);
            _w.WriteAttributeString("nil", XmlSchema.InstanceNamespace, "true");
            _w.WriteEndElement();
        }

        protected void WriteEmptyTag(string? name)
        {
            WriteEmptyTag(name, null);
        }

        protected void WriteEmptyTag(string? name, string? ns)
        {
            if (string.IsNullOrEmpty(name))
                return;
            WriteStartElement(name, ns, null, false);
            _w.WriteEndElement();
        }

        protected void WriteEndElement()
        {
            _w.WriteEndElement();
        }

        protected void WriteEndElement(object? o)
        {
            _w.WriteEndElement();

            if (o != null && _objectsInUse != null)
            {
#if DEBUG
                // use exception in the place of Debug.Assert to avoid throwing asserts from a server process such as aspnet_ewp.exe
                if (!_objectsInUse.ContainsKey(o)) throw new InvalidOperationException(SR.Format(SR.XmlInternalErrorDetails, "missing stack object of type " + o.GetType().FullName));
#endif

                _objectsInUse.Remove(o);
            }
        }

        protected void WriteSerializable(IXmlSerializable? serializable, string name, string ns, bool isNullable)
        {
            WriteSerializable(serializable, name, ns, isNullable, true);
        }

        protected void WriteSerializable(IXmlSerializable? serializable, string name, string? ns, bool isNullable, bool wrapped)
        {
            if (serializable == null)
            {
                if (isNullable) WriteNullTagLiteral(name, ns);
                return;
            }
            if (wrapped)
            {
                _w.WriteStartElement(name, ns);
            }
            serializable.WriteXml(_w);
            if (wrapped)
            {
                _w.WriteEndElement();
            }
        }

        protected void WriteNullableStringEncoded(string name, string? ns, string? value, XmlQualifiedName? xsiType)
        {
            if (value == null)
                WriteNullTagEncoded(name, ns);
            else
                WriteElementString(name, ns, value, xsiType);
        }

        protected void WriteNullableStringLiteral(string name, string? ns, string? value)
        {
            if (value == null)
                WriteNullTagLiteral(name, ns);
            else
                WriteElementString(name, ns, value, null);
        }


        protected void WriteNullableStringEncodedRaw(string name, string? ns, string? value, XmlQualifiedName? xsiType)
        {
            if (value == null)
                WriteNullTagEncoded(name, ns);
            else
                WriteElementStringRaw(name, ns, value, xsiType);
        }

        protected void WriteNullableStringEncodedRaw(string name, string? ns, byte[]? value, XmlQualifiedName? xsiType)
        {
            if (value == null)
                WriteNullTagEncoded(name, ns);
            else
                WriteElementStringRaw(name, ns, value, xsiType);
        }

        protected void WriteNullableStringLiteralRaw(string name, string? ns, string? value)
        {
            if (value == null)
                WriteNullTagLiteral(name, ns);
            else
                WriteElementStringRaw(name, ns, value, null);
        }

        protected void WriteNullableStringLiteralRaw(string name, string? ns, byte[]? value)
        {
            if (value == null)
                WriteNullTagLiteral(name, ns);
            else
                WriteElementStringRaw(name, ns, value, null);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        protected void WriteNullableQualifiedNameEncoded(string name, string? ns, XmlQualifiedName? value, XmlQualifiedName? xsiType)
        {
            if (value == null)
                WriteNullTagEncoded(name, ns);
            else
                WriteElementQualifiedName(name, ns, value, xsiType);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        protected void WriteNullableQualifiedNameLiteral(string name, string? ns, XmlQualifiedName? value)
        {
            if (value == null)
                WriteNullTagLiteral(name, ns);
            else
                WriteElementQualifiedName(name, ns, value, null);
        }


        protected void WriteElementEncoded(XmlNode? node, string name, string? ns, bool isNullable, bool any)
        {
            if (node == null)
            {
                if (isNullable) WriteNullTagEncoded(name, ns);
                return;
            }
            WriteElement(node, name, ns, isNullable, any);
        }

        protected void WriteElementLiteral(XmlNode? node, string name, string? ns, bool isNullable, bool any)
        {
            if (node == null)
            {
                if (isNullable) WriteNullTagLiteral(name, ns);
                return;
            }
            WriteElement(node, name, ns, isNullable, any);
        }

        private void WriteElement(XmlNode node, string name, string? ns, bool isNullable, bool any)
        {
            if (typeof(XmlAttribute).IsAssignableFrom(node.GetType()))
                throw new InvalidOperationException(SR.XmlNoAttributeHere);
            if (node is XmlDocument)
            {
                node = ((XmlDocument)node).DocumentElement!;
                if (node == null)
                {
                    if (isNullable) WriteNullTagEncoded(name, ns);
                    return;
                }
            }
            if (any)
            {
                if (node is XmlElement && name != null && name.Length > 0)
                {
                    // need to check against schema
                    if (node.LocalName != name || node.NamespaceURI != ns)
                        throw new InvalidOperationException(SR.Format(SR.XmlElementNameMismatch, node.LocalName, node.NamespaceURI, name, ns));
                }
            }
            else
                _w.WriteStartElement(name, ns);

            node.WriteTo(_w);

            if (!any)
                _w.WriteEndElement();
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        protected Exception CreateUnknownTypeException(object o)
        {
            return CreateUnknownTypeException(o.GetType());
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        protected Exception CreateUnknownTypeException(Type type)
        {
            if (typeof(IXmlSerializable).IsAssignableFrom(type)) return new InvalidOperationException(SR.Format(SR.XmlInvalidSerializable, type.FullName));
            TypeDesc typeDesc = new TypeScope().GetTypeDesc(type);
            if (!typeDesc.IsStructLike) return new InvalidOperationException(SR.Format(SR.XmlInvalidUseOfType, type.FullName));
            return new InvalidOperationException(SR.Format(SR.XmlUnxpectedType, type.FullName));
        }

        protected Exception CreateMismatchChoiceException(string value, string elementName, string enumValue)
        {
            // Value of {0} mismatches the type of {1}, you need to set it to {2}.
            return new InvalidOperationException(SR.Format(SR.XmlChoiceMismatchChoiceException, elementName, value, enumValue));
        }

        protected Exception CreateUnknownAnyElementException(string name, string ns)
        {
            return new InvalidOperationException(SR.Format(SR.XmlUnknownAnyElement, name, ns));
        }

        protected Exception CreateInvalidChoiceIdentifierValueException(string type, string identifier)
        {
            return new InvalidOperationException(SR.Format(SR.XmlInvalidChoiceIdentifierValue, type, identifier));
        }

        protected Exception CreateChoiceIdentifierValueException(string value, string identifier, string name, string ns)
        {
            // XmlChoiceIdentifierMismatch=Value '{0}' of the choice identifier '{1}' does not match element '{2}' from namespace '{3}'.
            return new InvalidOperationException(SR.Format(SR.XmlChoiceIdentifierMismatch, value, identifier, name, ns));
        }

        protected Exception CreateInvalidEnumValueException(object value, string typeName)
        {
            return new InvalidOperationException(SR.Format(SR.XmlUnknownConstant, value, typeName));
        }

        protected Exception CreateInvalidAnyTypeException(object o)
        {
            return CreateInvalidAnyTypeException(o.GetType());
        }

        protected Exception CreateInvalidAnyTypeException(Type type)
        {
            return new InvalidOperationException(SR.Format(SR.XmlIllegalAnyElement, type.FullName));
        }

        protected void WriteReferencingElement(string n, string? ns, object? o)
        {
            WriteReferencingElement(n, ns, o, false);
        }

        protected void WriteReferencingElement(string n, string? ns, object? o, bool isNullable)
        {
            if (o == null)
            {
                if (isNullable) WriteNullTagEncoded(n, ns);
                return;
            }

            WriteStartElement(n, ns, null, true);
            if (_soap12)
                _w.WriteAttributeString("ref", Soap12.Encoding, GetId(o, true));
            else
                _w.WriteAttributeString("href", $"#{GetId(o, true)}");

            _w.WriteEndElement();
        }

        private bool IsIdDefined(object o)
        {
            if (_references != null) return _references.Contains(o);
            else return false;
        }

        private string GetId(object o, bool addToReferencesList)
        {
            if (_references == null)
            {
                _references = new Hashtable();
                _referencesToWrite = new ArrayList();
            }
            string? id = (string?)_references[o];
            if (id == null)
            {
                id = string.Create(CultureInfo.InvariantCulture, $"{_idBase}id{++_nextId}");
                _references.Add(o, id);
                if (addToReferencesList) _referencesToWrite!.Add(o);
            }
            return id;
        }

        protected void WriteId(object o)
        {
            WriteId(o, true);
        }

        private void WriteId(object o, bool addToReferencesList)
        {
            if (_soap12)
                _w.WriteAttributeString("id", Soap12.Encoding, GetId(o, addToReferencesList));
            else
                _w.WriteAttributeString("id", GetId(o, addToReferencesList));
        }

        protected void WriteXmlAttribute(XmlNode node)
        {
            WriteXmlAttribute(node, null);
        }

        protected void WriteXmlAttribute(XmlNode node, object? container)
        {
            XmlAttribute? attr = node as XmlAttribute;
            if (attr == null) throw new InvalidOperationException(SR.XmlNeedAttributeHere);
            if (attr.Value != null)
            {
                if (attr.NamespaceURI == Wsdl.Namespace && attr.LocalName == Wsdl.ArrayType)
                {
                    string dims;
                    XmlQualifiedName qname = TypeScope.ParseWsdlArrayType(attr.Value, out dims, (container is XmlSchemaObject) ? (XmlSchemaObject)container : null);

                    string? value = FromXmlQualifiedName(qname, true) + dims;

                    //<xsd:attribute xmlns:q3="s0" wsdl:arrayType="q3:FoosBase[]" xmlns:q4="http://schemas.xmlsoap.org/soap/encoding/" ref="q4:arrayType" />
                    WriteAttribute(Wsdl.ArrayType, Wsdl.Namespace, value);
                }
                else
                {
                    WriteAttribute(attr.Name, attr.NamespaceURI, attr.Value);
                }
            }
        }

        protected void WriteAttribute(string localName, string? ns, string? value)
        {
            if (value == null) return;

            if (localName != "xmlns" && !localName.StartsWith("xmlns:", StringComparison.Ordinal))
            {
                int colon = localName.IndexOf(':');

                if (colon < 0)
                {
                    if (ns == XmlReservedNs.NsXml)
                    {
                        string? prefix = _w.LookupPrefix(ns);

                        if (string.IsNullOrEmpty(prefix))
                        {
                            prefix = "xml";
                        }
                        _w.WriteAttributeString(prefix, localName, ns, value);
                    }
                    else
                    {
                        _w.WriteAttributeString(localName, ns, value);
                    }
                }
                else
                {
                    string prefix = localName.Substring(0, colon);
                    _w.WriteAttributeString(prefix, localName.Substring(colon + 1), ns, value);
                }
            }
        }

        protected void WriteAttribute(string localName, string ns, byte[]? value)
        {
            if (value == null) return;

            if (localName != "xmlns" && !localName.StartsWith("xmlns:", StringComparison.Ordinal))
            {
                int colon = localName.IndexOf(':');

                if (colon < 0)
                {
                    if (ns == XmlReservedNs.NsXml)
                    {
                        _w.WriteStartAttribute("xml", localName, ns);
                    }
                    else
                    {
                        _w.WriteStartAttribute(null, localName, ns);
                    }
                }
                else
                {
                    string? prefix = _w.LookupPrefix(ns);
                    _w.WriteStartAttribute(prefix, localName.Substring(colon + 1), ns);
                }

                XmlCustomFormatter.WriteArrayBase64(_w, value, 0, value.Length);
                _w.WriteEndAttribute();
            }
        }

        protected void WriteAttribute(string localName, string? value)
        {
            if (value == null) return;
            _w.WriteAttributeString(localName, null, value);
        }

        protected void WriteAttribute(string localName, byte[]? value)
        {
            if (value == null) return;

            _w.WriteStartAttribute(null, localName, (string?)null);
            XmlCustomFormatter.WriteArrayBase64(_w, value, 0, value.Length);
            _w.WriteEndAttribute();
        }

        protected void WriteAttribute(string? prefix, string localName, string? ns, string? value)
        {
            if (value == null) return;
            _w.WriteAttributeString(prefix, localName, null, value);
        }

        protected void WriteValue(string? value)
        {
            if (value == null) return;
            _w.WriteString(value);
        }

        protected void WriteValue(byte[]? value)
        {
            if (value == null) return;
            XmlCustomFormatter.WriteArrayBase64(_w, value, 0, value.Length);
        }

        protected void WriteStartDocument()
        {
            if (_w.WriteState == WriteState.Start)
            {
                _w.WriteStartDocument();
            }
        }

        protected void WriteElementString(string localName, string? value)
        {
            WriteElementString(localName, null, value, null);
        }

        protected void WriteElementString(string localName, string? ns, string? value)
        {
            WriteElementString(localName, ns, value, null);
        }

        protected void WriteElementString(string localName, string? value, XmlQualifiedName? xsiType)
        {
            WriteElementString(localName, null, value, xsiType);
        }

        protected void WriteElementString(string localName, string? ns, string? value, XmlQualifiedName? xsiType)
        {
            if (value == null) return;
            if (xsiType == null)
                _w.WriteElementString(localName, ns, value);
            else
            {
                _w.WriteStartElement(localName, ns);
                WriteXsiType(xsiType.Name, xsiType.Namespace);
                _w.WriteString(value);
                _w.WriteEndElement();
            }
        }

        protected void WriteElementStringRaw(string localName, string? value)
        {
            WriteElementStringRaw(localName, null, value, null);
        }

        protected void WriteElementStringRaw(string localName, byte[]? value)
        {
            WriteElementStringRaw(localName, null, value, null);
        }

        protected void WriteElementStringRaw(string localName, string? ns, string? value)
        {
            WriteElementStringRaw(localName, ns, value, null);
        }

        protected void WriteElementStringRaw(string localName, string? ns, byte[]? value)
        {
            WriteElementStringRaw(localName, ns, value, null);
        }

        protected void WriteElementStringRaw(string localName, string? value, XmlQualifiedName? xsiType)
        {
            WriteElementStringRaw(localName, null, value, xsiType);
        }

        protected void WriteElementStringRaw(string localName, byte[]? value, XmlQualifiedName? xsiType)
        {
            WriteElementStringRaw(localName, null, value, xsiType);
        }

        protected void WriteElementStringRaw(string localName, string? ns, string? value, XmlQualifiedName? xsiType)
        {
            if (value == null) return;
            _w.WriteStartElement(localName, ns);
            if (xsiType != null)
                WriteXsiType(xsiType.Name, xsiType.Namespace);
            _w.WriteRaw(value);
            _w.WriteEndElement();
        }

        protected void WriteElementStringRaw(string localName, string? ns, byte[]? value, XmlQualifiedName? xsiType)
        {
            if (value == null) return;
            _w.WriteStartElement(localName, ns);
            if (xsiType != null)
                WriteXsiType(xsiType.Name, xsiType.Namespace);
            XmlCustomFormatter.WriteArrayBase64(_w, value, 0, value.Length);
            _w.WriteEndElement();
        }

        protected void WriteRpcResult(string name, string? ns)
        {
            if (!_soap12) return;
            WriteElementQualifiedName(Soap12.RpcResult, Soap12.RpcNamespace, new XmlQualifiedName(name, ns), null);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        protected void WriteElementQualifiedName(string localName, XmlQualifiedName? value)
        {
            WriteElementQualifiedName(localName, null, value, null);
        }

        protected void WriteElementQualifiedName(string localName, XmlQualifiedName? value, XmlQualifiedName? xsiType)
        {
            WriteElementQualifiedName(localName, null, value, xsiType);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        protected void WriteElementQualifiedName(string localName, string? ns, XmlQualifiedName? value)
        {
            WriteElementQualifiedName(localName, ns, value, null);
        }

        protected void WriteElementQualifiedName(string localName, string? ns, XmlQualifiedName? value, XmlQualifiedName? xsiType)
        {
            if (value == null) return;
            if (string.IsNullOrEmpty(value.Namespace))
            {
                WriteStartElement(localName, ns, null, true);
                WriteAttribute("xmlns", "");
            }
            else
                _w.WriteStartElement(localName, ns);
            if (xsiType != null)
                WriteXsiType(xsiType.Name, xsiType.Namespace);
            _w.WriteString(FromXmlQualifiedName(value, false));
            _w.WriteEndElement();
        }

        protected void AddWriteCallback(Type type, string typeName, string? typeNs, XmlSerializationWriteCallback callback)
        {
            TypeEntry entry = new TypeEntry();
            entry.typeName = typeName;
            entry.typeNs = typeNs;
            entry.type = type;
            entry.callback = callback;
            _typeEntries![type] = entry;
        }

        [RequiresUnreferencedCode("calls GetArrayElementType")]
        private void WriteArray(string name, string? ns, object o, Type type)
        {
            Type elementType = TypeScope.GetArrayElementType(type, null)!;
            string typeName;
            string? typeNs;

            StringBuilder arrayDims = new StringBuilder();
            if (!_soap12)
            {
                while ((elementType.IsArray || typeof(IEnumerable).IsAssignableFrom(elementType)) && GetPrimitiveTypeName(elementType, false) == null)
                {
                    elementType = TypeScope.GetArrayElementType(elementType, null)!;
                    arrayDims.Append("[]");
                }
            }

            if (elementType == typeof(object))
            {
                typeName = Soap.UrType;
                typeNs = XmlSchema.Namespace;
            }
            else
            {
                TypeEntry? entry = GetTypeEntry(elementType);
                if (entry != null)
                {
                    typeName = entry.typeName!;
                    typeNs = entry.typeNs;
                }
                else if (_soap12)
                {
                    XmlQualifiedName? qualName = GetPrimitiveTypeName(elementType, false);
                    if (qualName != null)
                    {
                        typeName = qualName.Name;
                        typeNs = qualName.Namespace;
                    }
                    else
                    {
                        Type? elementBaseType = elementType.BaseType;
                        while (elementBaseType != null)
                        {
                            entry = GetTypeEntry(elementBaseType);
                            if (entry != null) break;
                            elementBaseType = elementBaseType.BaseType;
                        }
                        if (entry != null)
                        {
                            typeName = entry.typeName!;
                            typeNs = entry.typeNs;
                        }
                        else
                        {
                            typeName = Soap.UrType;
                            typeNs = XmlSchema.Namespace;
                        }
                    }
                }
                else
                {
                    XmlQualifiedName qualName = GetPrimitiveTypeName(elementType);
                    typeName = qualName.Name;
                    typeNs = qualName.Namespace;
                }
            }

            if (arrayDims.Length > 0)
                typeName += arrayDims.ToString();

            if (_soap12 && name != null && name.Length > 0)
                WriteStartElement(name, ns, null, false);
            else
                WriteStartElement(Soap.Array, Soap.Encoding, null, true);

            WriteId(o, false);

            if (type.IsArray)
            {
                Array a = (Array)o;
                int arrayLength = a.Length;
                if (_soap12)
                {
                    _w.WriteAttributeString("itemType", Soap12.Encoding, GetQualifiedName(typeName, typeNs));
                    _w.WriteAttributeString("arraySize", Soap12.Encoding, arrayLength.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    _w.WriteAttributeString("arrayType", Soap.Encoding, $"{GetQualifiedName(typeName, typeNs)}[{arrayLength}]");
                }
                for (int i = 0; i < arrayLength; i++)
                {
                    WritePotentiallyReferencingElement("Item", "", a.GetValue(i), elementType, false, true);
                }
            }
            else
            {
#if DEBUG
                // use exception in the place of Debug.Assert to avoid throwing asserts from a server process such as aspnet_ewp.exe
                if (!typeof(IEnumerable).IsAssignableFrom(type)) throw new InvalidOperationException(SR.Format(SR.XmlInternalErrorDetails, "not array like type " + type.FullName));
#endif

                int arrayLength = typeof(ICollection).IsAssignableFrom(type) ? ((ICollection)o).Count : -1;
                if (_soap12)
                {
                    _w.WriteAttributeString("itemType", Soap12.Encoding, GetQualifiedName(typeName, typeNs));
                    if (arrayLength >= 0)
                        _w.WriteAttributeString("arraySize", Soap12.Encoding, arrayLength.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    string brackets = arrayLength >= 0 ? $"[{arrayLength}]" : "[]";
                    _w.WriteAttributeString("arrayType", Soap.Encoding, GetQualifiedName(typeName, typeNs) + brackets);
                }
                IEnumerator e = ((IEnumerable)o).GetEnumerator();
                if (e != null)
                {
                    while (e.MoveNext())
                    {
                        WritePotentiallyReferencingElement("Item", "", e.Current, elementType, false, true);
                    }
                }
            }
            _w.WriteEndElement();
        }
        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        protected void WritePotentiallyReferencingElement(string? n, string? ns, object? o)
        {
            WritePotentiallyReferencingElement(n, ns, o, null, false, false);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        protected void WritePotentiallyReferencingElement(string? n, string? ns, object? o, Type? ambientType)
        {
            WritePotentiallyReferencingElement(n, ns, o, ambientType, false, false);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        protected void WritePotentiallyReferencingElement(string n, string? ns, object? o, Type? ambientType, bool suppressReference)
        {
            WritePotentiallyReferencingElement(n, ns, o, ambientType, suppressReference, false);
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        protected void WritePotentiallyReferencingElement(string? n, string? ns, object? o, Type? ambientType, bool suppressReference, bool isNullable)
        {
            if (o == null)
            {
                if (isNullable) WriteNullTagEncoded(n, ns);
                return;
            }
            Type t = o.GetType();
            if (Type.GetTypeCode(t) == TypeCode.Object && !(o is Guid) && (t != typeof(XmlQualifiedName)) && !(o is XmlNode[]) && (t != typeof(byte[])))
            {
                if ((suppressReference || _soap12) && !IsIdDefined(o))
                {
                    WriteReferencedElement(n, ns, o, ambientType);
                }
                else
                {
                    if (n == null)
                    {
                        TypeEntry entry = GetTypeEntry(t)!;
                        WriteReferencingElement(entry.typeName!, entry.typeNs, o, isNullable);
                    }
                    else
                        WriteReferencingElement(n, ns, o, isNullable);
                }
            }
            else
            {
                // Enums always write xsi:type, so don't write it again here.
                bool needXsiType = t != ambientType && !t.IsEnum;
                TypeEntry? entry = GetTypeEntry(t);
                if (entry != null)
                {
                    if (n == null)
                        WriteStartElement(entry.typeName!, entry.typeNs, null, true);
                    else
                        WriteStartElement(n, ns, null, true);

                    if (needXsiType) WriteXsiType(entry.typeName!, entry.typeNs);
                    entry.callback!(o);
                    _w.WriteEndElement();
                }
                else
                {
                    WriteTypedPrimitive(n, ns, o, needXsiType);
                }
            }
        }

        [RequiresUnreferencedCode("calls WriteReferencedElement")]
        private void WriteReferencedElement(object o, Type? ambientType)
        {
            WriteReferencedElement(null, null, o, ambientType);
        }

        [RequiresUnreferencedCode("calls WriteArray")]
        private void WriteReferencedElement(string? name, string? ns, object o, Type? ambientType)
        {
            name ??= string.Empty;
            Type t = o.GetType();
            if (t.IsArray || typeof(IEnumerable).IsAssignableFrom(t))
            {
                WriteArray(name, ns, o, t);
            }
            else
            {
                TypeEntry? entry = GetTypeEntry(t);
                if (entry == null) throw CreateUnknownTypeException(t);
                WriteStartElement(name.Length == 0 ? entry.typeName! : name!, ns ?? entry.typeNs, null, true);
                WriteId(o, false);
                if (ambientType != t) WriteXsiType(entry.typeName!, entry.typeNs);
                entry.callback!(o);
                _w.WriteEndElement();
            }
        }

        [RequiresUnreferencedCode("calls InitCallbacks")]
        private TypeEntry? GetTypeEntry(Type t)
        {
            if (_typeEntries == null)
            {
                _typeEntries = new Hashtable();
                InitCallbacks();
            }
            return (TypeEntry?)_typeEntries[t];
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        protected abstract void InitCallbacks();

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        protected void WriteReferencedElements()
        {
            if (_referencesToWrite == null) return;

            for (int i = 0; i < _referencesToWrite.Count; i++)
            {
                WriteReferencedElement(_referencesToWrite[i]!, null);
            }
        }

        protected void TopLevelElement()
        {
            _objectsInUse = new Hashtable();
        }

        ///<internalonly/>
        protected void WriteNamespaceDeclarations(XmlSerializerNamespaces? xmlns)
        {
            if (xmlns != null)
            {
                foreach (XmlQualifiedName qname in xmlns.Namespaces)
                {
                    string prefix = qname.Name;
                    string? ns = qname.Namespace;
                    if (_namespaces != null)
                    {
                        string? oldNs;
                        if (_namespaces.TryLookupNamespace(prefix, out oldNs) && oldNs != null && oldNs != ns)
                        {
                            throw new InvalidOperationException(SR.Format(SR.XmlDuplicateNs, prefix, ns));
                        }
                    }
                    string? oldPrefix = string.IsNullOrEmpty(ns) ? null : Writer.LookupPrefix(ns);

                    if (oldPrefix == null || oldPrefix != prefix)
                    {
                        WriteAttribute(nameof(xmlns), prefix, null, ns);
                    }
                }
            }

            _namespaces = null;
        }

        private string NextPrefix()
        {
            if (_usedPrefixes == null)
            {
                return _aliasBase + (++_tempNamespacePrefix);
            }
            while (_usedPrefixes.Contains(++_tempNamespacePrefix)) { }
            return _aliasBase + _tempNamespacePrefix;
        }

        internal sealed class TypeEntry
        {
            internal XmlSerializationWriteCallback? callback;
            internal string? typeNs;
            internal string? typeName;
            internal Type? type;
        }
    }

    ///<internalonly/>
    public delegate void XmlSerializationWriteCallback(object o);
}
