// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;

// NetWasm: dotnet/runtime 811225a4's XsdDataContractExporter configuration and schema set, as the API family review
// selects them. Exporting, CanExport and the schema/root-name queries discover data contracts by reflection
// (or Assembly.GetTypes), which NetWasm does not have; they check their arguments and throw
// PlatformNotSupportedException before enumerating the caller's collections or touching the schema set.

namespace System.Runtime.Serialization
{
    /// <summary>
    /// Allows the transformation of a set of .NET types that are used in data contracts into an XML schema file (.xsd).
    /// </summary>
    /// <remarks>
    /// Use the <see cref="XsdDataContractExporter"/> class when you have created a Web service that incorporates data represented by
    /// runtime types and when you need to export XML schemas for each type to be consumed by other Web services.
    /// That is, <see cref="XsdDataContractExporter"/> transforms a set of runtime types into XML schemas. The schemas can then be exposed
    /// through a Web Services Description Language (WSDL) document for use by others who need to interoperate with your service.
    ///
    /// Conversely, if you are creating a Web service that must interoperate with an existing Web service, use the XsdDataContractImporter
    /// to transform XML schemas and create the runtime types that represent the data in a selected programming language.
    ///
    /// The <see cref="XsdDataContractExporter"/> generates an <see cref="XmlSchemaSet"/> object that contains the collection of schemas.
    /// Access the set of schemas through the <see cref="Schemas"/> property.
    /// </remarks>
    public class XsdDataContractExporter
    {
        private ExportOptions? _options;
        private XmlSchemaSet? _schemas;

        /// <summary>
        /// Initializes a new instance of the <see cref="XsdDataContractExporter"/> class.
        /// </summary>
        public XsdDataContractExporter()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="XsdDataContractExporter"/> class with the specified set of schemas.
        /// </summary>
        /// <param name="schemas">An <see cref="XmlSchemaSet"/> that contains the schemas to be exported.</param>
        public XsdDataContractExporter(XmlSchemaSet? schemas)
        {
            _schemas = schemas;
        }

        /// <summary>
        /// Gets or sets an <see cref="ExportOptions"/> that contains options that can be set for the export operation.
        /// </summary>
        public ExportOptions? Options
        {
            get { return _options; }
            set { _options = value; }
        }

        /// <summary>
        /// Gets the collection of exported XML schemas.
        /// </summary>
        public XmlSchemaSet Schemas
        {
            get
            {
                XmlSchemaSet schemaSet = GetSchemaSet();
                CompileSchemaSet(schemaSet);
                return schemaSet;
            }
        }

        private XmlSchemaSet GetSchemaSet()
        {
            if (_schemas == null)
            {
                _schemas = new XmlSchemaSet();
                _schemas.XmlResolver = null;
            }
            return _schemas;
        }

        /// <summary>
        /// Transforms the types contained in the specified collection of assemblies.
        /// </summary>
        /// <param name="assemblies">A <see cref="ICollection{T}"/> (of <see cref="Assembly"/>) that contains the types to export.</param>
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public void Export(ICollection<Assembly> assemblies)
        {
            ArgumentNullException.ThrowIfNull(assemblies);
            throw new PlatformNotSupportedException(SR.NetWasm_DataContractExportUnsupported);
        }

        /// <summary>
        /// Transforms the types contained in the <see cref="ICollection{T}"/> passed to this method.
        /// </summary>
        /// <param name="types">A <see cref="ICollection{T}"/> (of <see cref="Type"/>) that contains the types to export.</param>
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public void Export(ICollection<Type> types)
        {
            ArgumentNullException.ThrowIfNull(types);
            throw new PlatformNotSupportedException(SR.NetWasm_DataContractExportUnsupported);
        }

        /// <summary>
        /// Transforms the specified .NET Framework type into an XML schema definition language (XSD) schema.
        /// </summary>
        /// <param name="type">The <see cref="Type"/> to transform into an XML schema.</param>
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public void Export(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new PlatformNotSupportedException(SR.NetWasm_DataContractExportUnsupported);
        }

        /// <summary>
        /// Returns the contract name and contract namespace for the <see cref="Type"/>.
        /// </summary>
        /// <param name="type">The <see cref="Type"/> that was exported.</param>
        /// <returns>An <see cref="XmlQualifiedName"/> that represents the contract name of the type and its namespace.</returns>
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public XmlQualifiedName GetSchemaTypeName(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new PlatformNotSupportedException(SR.NetWasm_DataContractExportUnsupported);
        }

        /// <summary>
        /// Returns the XML schema type for the specified type.
        /// </summary>
        /// <param name="type">The type to return a schema for.</param>
        /// <returns>An <see cref="XmlSchemaType"/> that contains the XML schema.</returns>
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public XmlSchemaType? GetSchemaType(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new PlatformNotSupportedException(SR.NetWasm_DataContractExportUnsupported);
        }

        /// <summary>
        /// Returns the top-level name and namespace for the <see cref="Type"/>.
        /// </summary>
        /// <param name="type">The <see cref="Type"/> to query.</param>
        /// <returns>The <see cref="XmlQualifiedName"/> that represents the top-level name and namespace for this Type, which is written to the stream when writing this object.</returns>
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public XmlQualifiedName? GetRootElementName(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new PlatformNotSupportedException(SR.NetWasm_DataContractExportUnsupported);
        }

        /// <summary>
        /// Gets a value that indicates whether the set of runtime types contained in a set of assemblies can be exported.
        /// </summary>
        /// <param name="assemblies">A <see cref="ICollection{T}"/> of <see cref="Assembly"/> that contains the assemblies with the types to export.</param>
        /// <returns>true if the types can be exported; otherwise, false.</returns>
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public bool CanExport(ICollection<Assembly> assemblies)
        {
            ArgumentNullException.ThrowIfNull(assemblies);
            throw new PlatformNotSupportedException(SR.NetWasm_DataContractExportUnsupported);
        }

        /// <summary>
        /// Gets a value that indicates whether the set of runtime types contained in a <see cref="ICollection{T}"/> can be exported.
        /// </summary>
        /// <param name="types">A <see cref="ICollection{T}"/> that contains the specified types to export.</param>
        /// <returns>true if the types can be exported; otherwise, false.</returns>
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public bool CanExport(ICollection<Type> types)
        {
            ArgumentNullException.ThrowIfNull(types);
            throw new PlatformNotSupportedException(SR.NetWasm_DataContractExportUnsupported);
        }

        /// <summary>
        /// Gets a value that indicates whether the specified runtime type can be exported.
        /// </summary>
        /// <param name="type">The <see cref="Type"/> to export.</param>
        /// <returns>true if the type can be exported; otherwise, false.</returns>
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public bool CanExport(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new PlatformNotSupportedException(SR.NetWasm_DataContractExportUnsupported);
        }

        // SchemaImporter.CompileSchemaSet: add the base XSD schema with a top-level "schema" element if absent, then compile.
        private static void CompileSchemaSet(XmlSchemaSet schemaSet)
        {
            if (schemaSet.Contains(XmlSchema.Namespace))
                schemaSet.Compile();
            else
            {
                XmlSchema xsdSchema = new XmlSchema();
                xsdSchema.TargetNamespace = XmlSchema.Namespace;
                XmlSchemaElement element = new XmlSchemaElement();
                element.Name = Globals.SchemaLocalName;
                element.SchemaType = new XmlSchemaComplexType();
                xsdSchema.Items.Add(element);
                schemaSet.Add(xsdSchema);
                schemaSet.Compile();
            }
        }
    }
}
