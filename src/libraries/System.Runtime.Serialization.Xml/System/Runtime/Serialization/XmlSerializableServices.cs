// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;

namespace System.Runtime.Serialization
{
    public static class XmlSerializableServices
    {
        public static XmlNode[] ReadNodes(XmlReader xmlReader)
        {
            ArgumentNullException.ThrowIfNull(xmlReader);

            XmlDocument doc = new XmlDocument();
            List<XmlNode> nodeList = new List<XmlNode>();
            if (xmlReader.MoveToFirstAttribute())
            {
                do
                {
                    if (IsValidAttribute(xmlReader))
                    {
                        XmlNode? node = doc.ReadNode(xmlReader);
                        if (node == null)
                            throw XmlObjectSerializer.CreateSerializationException(SR.UnexpectedEndOfFile);
                        nodeList.Add(node);
                    }
                } while (xmlReader.MoveToNextAttribute());
            }
            xmlReader.MoveToElement();
            if (!xmlReader.IsEmptyElement)
            {
                int startDepth = xmlReader.Depth;
                xmlReader.Read();
                while (xmlReader.Depth > startDepth && xmlReader.NodeType != XmlNodeType.EndElement)
                {
                    XmlNode? node = doc.ReadNode(xmlReader);
                    if (node == null)
                        throw XmlObjectSerializer.CreateSerializationException(SR.UnexpectedEndOfFile);
                    nodeList.Add(node);
                }
            }
            return nodeList.ToArray();
        }

        private static bool IsValidAttribute(XmlReader xmlReader)
        {
            return xmlReader.NamespaceURI != Globals.SerializationNamespace &&
                                   xmlReader.NamespaceURI != Globals.SchemaInstanceNamespace &&
                                   xmlReader.Prefix != "xmlns" &&
                                   xmlReader.LocalName != "xmlns";
        }

        public static void WriteNodes(XmlWriter xmlWriter, XmlNode?[]? nodes)
        {
            ArgumentNullException.ThrowIfNull(xmlWriter);

            if (nodes != null)
                for (int i = 0; i < nodes.Length; i++)
                    if (nodes[i] != null)
                        nodes[i]!.WriteTo(xmlWriter);
        }

        internal const string AddDefaultSchemaMethodName = "AddDefaultSchema";
        public static void AddDefaultSchema(XmlSchemaSet schemas, XmlQualifiedName typeQName)
        {
            ArgumentNullException.ThrowIfNull(schemas);
            ArgumentNullException.ThrowIfNull(typeQName);

            AddDefaultXmlType(schemas, typeQName.Name, typeQName.Namespace);
        }

        // NetWasm: SchemaExporter.AddDefaultXmlType and SchemaHelper.GetSchema/CreateSchema from dotnet/runtime 811225a4,
        // without the rest of SchemaExporter, whose data-contract export needs reflection.
        private static void AddDefaultXmlType(XmlSchemaSet schemas, string localName, string ns)
        {
            XmlSchemaComplexType defaultXmlType = CreateAnyType();
            defaultXmlType.Name = localName;
            XmlSchema schema = GetSchema(ns, schemas);
            schema.Items.Add(defaultXmlType);
            schemas.Reprocess(schema);
        }

        private static XmlSchemaComplexType CreateAnyType()
        {
            XmlSchemaComplexType anyType = new XmlSchemaComplexType();
            anyType.IsMixed = true;
            anyType.Particle = new XmlSchemaSequence();
            XmlSchemaAny any = new XmlSchemaAny();
            any.MinOccurs = 0;
            any.MaxOccurs = decimal.MaxValue;
            any.ProcessContents = XmlSchemaContentProcessing.Lax;
            ((XmlSchemaSequence)anyType.Particle).Items.Add(any);
            anyType.AnyAttribute = new XmlSchemaAnyAttribute();
            return anyType;
        }

        private static XmlSchema GetSchema(string ns, XmlSchemaSet schemas)
        {
            ns ??= string.Empty;

            ICollection currentSchemas = schemas.Schemas();
            foreach (XmlSchema schema in currentSchemas)
            {
                if ((schema.TargetNamespace == null && ns.Length == 0) || ns.Equals(schema.TargetNamespace))
                {
                    return schema;
                }
            }
            return CreateSchema(ns, schemas);
        }

        private static XmlSchema CreateSchema(string ns, XmlSchemaSet schemas)
        {
            XmlSchema schema = new XmlSchema();

            schema.ElementFormDefault = XmlSchemaForm.Qualified;
            if (ns.Length > 0)
            {
                schema.TargetNamespace = ns;
                schema.Namespaces.Add(Globals.TnsPrefix, ns);
            }

            schemas.Add(schema);
            return schema;
        }
    }
}
