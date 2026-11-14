// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;

namespace System.Xml.Serialization
{
    internal sealed class XmlSerializationPrimitiveWriter : System.Xml.Serialization.XmlSerializationWriter
    {
        // NetWasm: the root element's name and namespace, which XmlSerializer sets for a primitive type given an
        // XmlRootAttribute or a default namespace. Upstream serializes those through the reflection-based
        // writer, which NetWasm does not have; this writer writes the same element under the requested root.
        internal string? RootName;
        internal string RootNamespace = string.Empty;

        internal void Write_string(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteNullTagLiteral(RootName ?? @"string", RootNamespace);
                return;
            }
            TopLevelElement();
            WriteNullableStringLiteral(RootName ?? @"string", RootNamespace, ((string)o));
        }

        internal void Write_int(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"int", RootNamespace);
                return;
            }
            WriteElementStringRaw(RootName ?? @"int", RootNamespace, System.Xml.XmlConvert.ToString((int)((int)o)));
        }

        internal void Write_boolean(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"boolean", RootNamespace);
                return;
            }
            WriteElementStringRaw(RootName ?? @"boolean", RootNamespace, System.Xml.XmlConvert.ToString((bool)((bool)o)));
        }

        internal void Write_short(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"short", RootNamespace);
                return;
            }
            WriteElementStringRaw(RootName ?? @"short", RootNamespace, System.Xml.XmlConvert.ToString((short)((short)o)));
        }

        internal void Write_long(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"long", RootNamespace);
                return;
            }
            WriteElementStringRaw(RootName ?? @"long", RootNamespace, System.Xml.XmlConvert.ToString((long)((long)o)));
        }

        internal void Write_float(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"float", RootNamespace);
                return;
            }
            WriteElementStringRaw(RootName ?? @"float", RootNamespace, System.Xml.XmlConvert.ToString((float)((float)o)));
        }

        internal void Write_double(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"double", RootNamespace);
                return;
            }
            WriteElementStringRaw(RootName ?? @"double", RootNamespace, System.Xml.XmlConvert.ToString((double)((double)o)));
        }

        internal void Write_decimal(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"decimal", RootNamespace);
                return;
            }
            // Preventing inlining optimization which is causing issue for XmlConvert.ToString(Decimal)
            decimal d = (decimal)o;
            WriteElementStringRaw(RootName ?? @"decimal", RootNamespace, System.Xml.XmlConvert.ToString(d));
        }

        internal void Write_dateTime(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"dateTime", RootNamespace);
                return;
            }
            WriteElementStringRaw(RootName ?? @"dateTime", RootNamespace, FromDateTime(((System.DateTime)o)));
        }

        internal void Write_dateTimeOffset(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"dateTimeOffset", RootNamespace);
                return;
            }
            DateTimeOffset dto = (DateTimeOffset)o;
            WriteElementStringRaw(RootName ?? @"dateTimeOffset", RootNamespace, System.Xml.XmlConvert.ToString(dto));
        }

        internal void Write_dateOnly(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"dateOnly", RootNamespace);
                return;
            }
            DateOnly dateOnly = (DateOnly)o;
            WriteElementStringRaw(RootName ?? @"dateOnly", RootNamespace, FromDateOnly(dateOnly));
        }

        internal void Write_timeOnly(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"timeOnly", RootNamespace);
                return;
            }
            TimeOnly timeOnly = (TimeOnly)o;
            WriteElementStringRaw(RootName ?? @"timeOnly", RootNamespace, FromTimeOnly(timeOnly));
        }

        internal void Write_unsignedByte(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"unsignedByte", RootNamespace);
                return;
            }
            WriteElementStringRaw(RootName ?? @"unsignedByte", RootNamespace, System.Xml.XmlConvert.ToString((byte)((byte)o)));
        }

        internal void Write_byte(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"byte", RootNamespace);
                return;
            }
            WriteElementStringRaw(RootName ?? @"byte", RootNamespace, System.Xml.XmlConvert.ToString((sbyte)((sbyte)o)));
        }

        internal void Write_unsignedShort(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"unsignedShort", RootNamespace);
                return;
            }
            WriteElementStringRaw(RootName ?? @"unsignedShort", RootNamespace, System.Xml.XmlConvert.ToString((ushort)((ushort)o)));
        }

        internal void Write_unsignedInt(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"unsignedInt", RootNamespace);
                return;
            }
            WriteElementStringRaw(RootName ?? @"unsignedInt", RootNamespace, System.Xml.XmlConvert.ToString((uint)((uint)o)));
        }

        internal void Write_unsignedLong(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"unsignedLong", RootNamespace);
                return;
            }
            WriteElementStringRaw(RootName ?? @"unsignedLong", RootNamespace, System.Xml.XmlConvert.ToString((ulong)((ulong)o)));
        }

        internal void Write_base64Binary(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteNullTagLiteral(RootName ?? @"base64Binary", RootNamespace);
                return;
            }
            TopLevelElement();
            WriteNullableStringLiteralRaw(RootName ?? @"base64Binary", RootNamespace, FromByteArrayBase64(((byte[])o)));
        }

        internal void Write_guid(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"guid", RootNamespace);
                return;
            }
            // Preventing inlining optimization which is causing issue for XmlConvert.ToString(Guid)
            Guid guid = (Guid)o;
            WriteElementStringRaw(RootName ?? @"guid", RootNamespace, System.Xml.XmlConvert.ToString(guid));
        }

        internal void Write_TimeSpan(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"TimeSpan", RootNamespace);
                return;
            }
            TimeSpan timeSpan = (TimeSpan)o;
            WriteElementStringRaw(RootName ?? @"TimeSpan", RootNamespace, System.Xml.XmlConvert.ToString(timeSpan));
        }

        internal void Write_char(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteEmptyTag(RootName ?? @"char", RootNamespace);
                return;
            }
            WriteElementString(RootName ?? @"char", RootNamespace, FromChar(((char)o)));
        }

        internal void Write_QName(object? o)
        {
            WriteStartDocument();
            if (o == null)
            {
                WriteNullTagLiteral(RootName ?? @"QName", RootNamespace);
                return;
            }
            TopLevelElement();
            WriteNullableQualifiedNameLiteral(RootName ?? @"QName", RootNamespace, ((global::System.Xml.XmlQualifiedName)o));
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        protected override void InitCallbacks()
        {
        }
    }

    internal sealed class XmlSerializationPrimitiveReader : System.Xml.Serialization.XmlSerializationReader
    {
        // NetWasm: the root element's name and namespace, which XmlSerializer sets for a primitive type given an
        // XmlRootAttribute or a default namespace. Upstream serializes those through the reflection-based
        // reader, which NetWasm does not have; this reader matches the same element under the requested root.
        internal string? RootName;
        internal string RootNamespace = string.Empty;

        internal object? Read_string()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id1_string && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    if (ReadNull())
                    {
                        o = null;
                    }
                    else
                    {
                        o = Reader.ReadElementString();
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_int()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id3_int && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    {
                        o = System.Xml.XmlConvert.ToInt32(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_boolean()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id4_boolean && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    {
                        o = System.Xml.XmlConvert.ToBoolean(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_short()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id5_short && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    {
                        o = System.Xml.XmlConvert.ToInt16(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_long()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id6_long && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    {
                        o = System.Xml.XmlConvert.ToInt64(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_float()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id7_float && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    {
                        o = System.Xml.XmlConvert.ToSingle(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_double()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id8_double && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    {
                        o = System.Xml.XmlConvert.ToDouble(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_decimal()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id9_decimal && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    {
                        o = System.Xml.XmlConvert.ToDecimal(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_dateTime()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id10_dateTime && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    {
                        o = ToDateTime(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_dateTimeOffset()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id20_dateTimeOffset && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    if (Reader.IsEmptyElement)
                    {
                        Reader.Skip();
                        o = default(DateTimeOffset);
                    }
                    else
                    {
                        o = System.Xml.XmlConvert.ToDateTimeOffset(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_dateOnly()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id20_dateOnly && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    if (Reader.IsEmptyElement)
                    {
                        Reader.Skip();
                        o = default(DateOnly);
                    }
                    else
                    {
                        o = ToDateOnly(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_timeOnly()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id20_timeOnly && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    if (Reader.IsEmptyElement)
                    {
                        Reader.Skip();
                        o = default(TimeOnly);
                    }
                    else
                    {
                        o = ToTimeOnly(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_unsignedByte()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id11_unsignedByte && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    {
                        o = System.Xml.XmlConvert.ToByte(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_byte()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id12_byte && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    {
                        o = System.Xml.XmlConvert.ToSByte(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_unsignedShort()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id13_unsignedShort && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    {
                        o = System.Xml.XmlConvert.ToUInt16(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_unsignedInt()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id14_unsignedInt && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    {
                        o = System.Xml.XmlConvert.ToUInt32(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_unsignedLong()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id15_unsignedLong && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    {
                        o = System.Xml.XmlConvert.ToUInt64(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_base64Binary()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id16_base64Binary && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    if (ReadNull())
                    {
                        o = null;
                    }
                    else
                    {
                        o = ToByteArrayBase64(false);
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_guid()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id17_guid && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    {
                        o = System.Xml.XmlConvert.ToGuid(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_TimeSpan()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id19_TimeSpan && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    if (Reader.IsEmptyElement)
                    {
                        Reader.Skip();
                        o = default(TimeSpan);
                    }
                    else
                    {
                        o = System.Xml.XmlConvert.ToTimeSpan(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_char()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id18_char && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    {
                        o = ToChar(Reader.ReadElementString());
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        internal object? Read_QName()
        {
            object? o = null;
            Reader.MoveToContent();
            if (Reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (((object)Reader.LocalName == (object)_id1_QName && (object)Reader.NamespaceURI == (object)_id2_Item))
                {
                    if (ReadNull())
                    {
                        o = null;
                    }
                    else
                    {
                        o = ReadElementQualifiedName();
                    }
                }
                else
                {
                    throw CreateUnknownNodeException();
                }
            }
            else
            {
                UnknownNode(null);
            }
            return (object?)o;
        }

        [RequiresUnreferencedCode(XmlSerializer.TrimSerializationWarning)]
        [RequiresDynamicCode(XmlSerializer.AotSerializationWarning)]
        protected override void InitCallbacks()
        {
        }

        private string _id4_boolean = null!;
        private string _id14_unsignedInt = null!;
        private string _id15_unsignedLong = null!;
        private string _id7_float = null!;
        private string _id10_dateTime = null!;
        private string _id20_dateTimeOffset = null!;
        private string _id20_dateOnly = null!;
        private string _id20_timeOnly = null!;
        private string _id6_long = null!;
        private string _id9_decimal = null!;
        private string _id8_double = null!;
        private string _id17_guid = null!;
        private string _id19_TimeSpan = null!;
        private string _id2_Item = null!;
        private string _id13_unsignedShort = null!;
        private string _id18_char = null!;
        private string _id3_int = null!;
        private string _id12_byte = null!;
        private string _id16_base64Binary = null!;
        private string _id11_unsignedByte = null!;
        private string _id5_short = null!;
        private string _id1_string = null!;
        private string _id1_QName = null!;

        protected override void InitIDs()
        {
            _id4_boolean = Reader.NameTable.Add(RootName ?? @"boolean");
            _id14_unsignedInt = Reader.NameTable.Add(RootName ?? @"unsignedInt");
            _id15_unsignedLong = Reader.NameTable.Add(RootName ?? @"unsignedLong");
            _id7_float = Reader.NameTable.Add(RootName ?? @"float");
            _id10_dateTime = Reader.NameTable.Add(RootName ?? @"dateTime");
            _id20_dateTimeOffset = Reader.NameTable.Add(RootName ?? @"dateTimeOffset");
            _id20_dateOnly = Reader.NameTable.Add(RootName ?? @"dateOnly");
            _id20_timeOnly = Reader.NameTable.Add(RootName ?? @"timeOnly");
            _id6_long = Reader.NameTable.Add(RootName ?? @"long");
            _id9_decimal = Reader.NameTable.Add(RootName ?? @"decimal");
            _id8_double = Reader.NameTable.Add(RootName ?? @"double");
            _id17_guid = Reader.NameTable.Add(RootName ?? @"guid");
            _id19_TimeSpan = Reader.NameTable.Add(RootName ?? @"TimeSpan");
            _id2_Item = Reader.NameTable.Add(RootNamespace);
            _id13_unsignedShort = Reader.NameTable.Add(RootName ?? @"unsignedShort");
            _id18_char = Reader.NameTable.Add(RootName ?? @"char");
            _id3_int = Reader.NameTable.Add(RootName ?? @"int");
            _id12_byte = Reader.NameTable.Add(RootName ?? @"byte");
            _id16_base64Binary = Reader.NameTable.Add(RootName ?? @"base64Binary");
            _id11_unsignedByte = Reader.NameTable.Add(RootName ?? @"unsignedByte");
            _id5_short = Reader.NameTable.Add(RootName ?? @"short");
            _id1_string = Reader.NameTable.Add(RootName ?? @"string");
            _id1_QName = Reader.NameTable.Add(RootName ?? @"QName");
        }
    }
}
