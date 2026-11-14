// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: dotnet/runtime 811225a4's DataContractJsonSerializer constructors, settings and root-name helpers, as the
// API family review selects them. Upstream expands every non-null known type through CollectionDataContract,
// which inspects the type by reflection; NetWasm does not have that, so a non-null known type throws
// PlatformNotSupportedException (null or empty sequences and null entries remain usable). Reading and writing need
// the reflection-based DataContract engine, so every execution route checks its argument and throws
// PlatformNotSupportedException before touching the reader, writer or stream. The newer surrogate-provider
// methods, which netstandard does not have, are not exposed.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Xml;

namespace System.Runtime.Serialization.Json
{
    public sealed class DataContractJsonSerializer : XmlObjectSerializer
    {
        internal IList<Type>? knownTypeList;
        private EmitTypeInformation _emitTypeInformation;
        private ReadOnlyCollection<Type>? _knownTypeCollection;
        private int _maxItemsInObjectGraph;
        private bool _serializeReadOnlyTypes;
        private DateTimeFormat? _dateTimeFormat;
        private bool _useSimpleDictionaryFormat;
        private bool _ignoreExtensionDataObject;
        // The root name, as upstream computes it; only the unported execution paths read it.
#pragma warning disable CS0414
        private XmlDictionaryString? _rootName;
        private bool _rootNameRequiresMapping;
#pragma warning restore CS0414
        private Type _rootType;

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public DataContractJsonSerializer(Type type)
            : this(type, (IEnumerable<Type>?)null)
        {
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public DataContractJsonSerializer(Type type, string? rootName)
            : this(type, rootName, null)
        {
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public DataContractJsonSerializer(Type type, XmlDictionaryString? rootName)
            : this(type, rootName, null)
        {
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public DataContractJsonSerializer(Type type, IEnumerable<Type>? knownTypes)
            : this(type, null, knownTypes, int.MaxValue, false, false)
        {
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public DataContractJsonSerializer(Type type, string? rootName, IEnumerable<Type>? knownTypes)
            : this(type, new DataContractJsonSerializerSettings() { RootName = rootName, KnownTypes = knownTypes })
        {
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public DataContractJsonSerializer(Type type, XmlDictionaryString? rootName, IEnumerable<Type>? knownTypes)
            : this(type, rootName, knownTypes, int.MaxValue, false, false)
        {
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public DataContractJsonSerializer(Type type, DataContractJsonSerializerSettings? settings)
        {
            settings ??= new DataContractJsonSerializerSettings();

            XmlDictionaryString? rootName = (settings.RootName == null) ? null : new XmlDictionary(1).Add(settings.RootName);
            Initialize(type, rootName, settings.KnownTypes, settings.MaxItemsInObjectGraph, settings.IgnoreExtensionDataObject,
                settings.EmitTypeInformation, settings.SerializeReadOnlyTypes, settings.DateTimeFormat, settings.UseSimpleDictionaryFormat);
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        internal DataContractJsonSerializer(Type type,
            XmlDictionaryString? rootName,
            IEnumerable<Type>? knownTypes,
            int maxItemsInObjectGraph,
            bool ignoreExtensionDataObject,
            bool alwaysEmitTypeInformation)
        {
            EmitTypeInformation emitTypeInformation = alwaysEmitTypeInformation ? EmitTypeInformation.Always : EmitTypeInformation.AsNeeded;
            Initialize(type, rootName, knownTypes, maxItemsInObjectGraph, ignoreExtensionDataObject, emitTypeInformation, false, null, false);
        }

        public bool IgnoreExtensionDataObject
        {
            get { return _ignoreExtensionDataObject; }
        }

        public ReadOnlyCollection<Type> KnownTypes
        {
            get
            {
                return _knownTypeCollection ??=
                    knownTypeList != null ?
                        new ReadOnlyCollection<Type>(knownTypeList) :
                        ReadOnlyCollection<Type>.Empty;
            }
        }

        public int MaxItemsInObjectGraph
        {
            get { return _maxItemsInObjectGraph; }
        }

        public DateTimeFormat? DateTimeFormat
        {
            get
            {
                return _dateTimeFormat;
            }
        }

        public EmitTypeInformation EmitTypeInformation
        {
            get
            {
                return _emitTypeInformation;
            }
        }

        public bool SerializeReadOnlyTypes
        {
            get
            {
                return _serializeReadOnlyTypes;
            }
        }

        public bool UseSimpleDictionaryFormat
        {
            get
            {
                return _useSimpleDictionaryFormat;
            }
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override bool IsStartObject(XmlReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            throw ExecutionUnsupported();
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override bool IsStartObject(XmlDictionaryReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            throw ExecutionUnsupported();
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override object? ReadObject(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            throw ExecutionUnsupported();
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override object? ReadObject(XmlReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            throw ExecutionUnsupported();
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override object? ReadObject(XmlReader reader, bool verifyObjectName)
        {
            ArgumentNullException.ThrowIfNull(reader);
            throw ExecutionUnsupported();
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override object? ReadObject(XmlDictionaryReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            throw ExecutionUnsupported();
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override object? ReadObject(XmlDictionaryReader reader, bool verifyObjectName)
        {
            ArgumentNullException.ThrowIfNull(reader);
            throw ExecutionUnsupported();
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override void WriteEndObject(XmlWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            throw ExecutionUnsupported();
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override void WriteEndObject(XmlDictionaryWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            throw ExecutionUnsupported();
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override void WriteObject(Stream stream, object? graph)
        {
            ArgumentNullException.ThrowIfNull(stream);
            throw ExecutionUnsupported();
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override void WriteObject(XmlWriter writer, object? graph)
        {
            ArgumentNullException.ThrowIfNull(writer);
            throw ExecutionUnsupported();
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override void WriteObject(XmlDictionaryWriter writer, object? graph)
        {
            ArgumentNullException.ThrowIfNull(writer);
            throw ExecutionUnsupported();
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override void WriteObjectContent(XmlWriter writer, object? graph)
        {
            ArgumentNullException.ThrowIfNull(writer);
            throw ExecutionUnsupported();
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override void WriteObjectContent(XmlDictionaryWriter writer, object? graph)
        {
            ArgumentNullException.ThrowIfNull(writer);
            throw ExecutionUnsupported();
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override void WriteStartObject(XmlWriter writer, object? graph)
        {
            ArgumentNullException.ThrowIfNull(writer);
            throw ExecutionUnsupported();
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override void WriteStartObject(XmlDictionaryWriter writer, object? graph)
        {
            ArgumentNullException.ThrowIfNull(writer);
            throw ExecutionUnsupported();
        }

        internal static bool CheckIfJsonNameRequiresMapping(string? jsonName)
        {
            if (jsonName != null)
            {
                if (!IsValidNCName(jsonName))
                {
                    return true;
                }

                for (int i = 0; i < jsonName.Length; i++)
                {
                    if (XmlJsonWriter.CharacterNeedsEscaping(jsonName[i]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        internal static bool CheckIfJsonNameRequiresMapping(XmlDictionaryString? jsonName)
        {
            return (jsonName == null) ? false : CheckIfJsonNameRequiresMapping(jsonName.Value);
        }

        internal static string ConvertXmlNameToJsonName(string xmlName)
        {
            return XmlConvert.DecodeName(xmlName);
        }

        [return: NotNullIfNotNull(nameof(xmlName))]
        internal static XmlDictionaryString? ConvertXmlNameToJsonName(XmlDictionaryString? xmlName)
        {
            return (xmlName == null) ? null : new XmlDictionary().Add(ConvertXmlNameToJsonName(xmlName.Value));
        }

        // DataContract.IsValidNCName.
        private static bool IsValidNCName(string name)
        {
            try
            {
                XmlConvert.VerifyNCName(name);
                return true;
            }
            catch (XmlException)
            {
                return false;
            }
        }

        [MemberNotNull(nameof(_rootType))]
        private void Initialize(Type type,
            IEnumerable<Type>? knownTypes,
            int maxItemsInObjectGraph,
            bool ignoreExtensionDataObject,
            EmitTypeInformation emitTypeInformation,
            bool serializeReadOnlyTypes,
            DateTimeFormat? dateTimeFormat,
            bool useSimpleDictionaryFormat)
        {
            ArgumentNullException.ThrowIfNull(type);

            _rootType = type;

            if (knownTypes != null)
            {
                this.knownTypeList = new List<Type>();
                foreach (Type knownType in knownTypes)
                {
                    this.knownTypeList.Add(knownType);
                    if (knownType != null)
                    {
                        // Upstream's AddCollectionItemTypeToKnownTypes inspects the type through CollectionDataContract.
                        throw new PlatformNotSupportedException(SR.Format(SR.NetWasm_DataContractJsonKnownTypeUnsupported, knownType.FullName));
                    }
                }
            }

            ArgumentOutOfRangeException.ThrowIfNegative(maxItemsInObjectGraph);
            _maxItemsInObjectGraph = maxItemsInObjectGraph;
            _ignoreExtensionDataObject = ignoreExtensionDataObject;
            _emitTypeInformation = emitTypeInformation;
            _serializeReadOnlyTypes = serializeReadOnlyTypes;
            _dateTimeFormat = dateTimeFormat;
            _useSimpleDictionaryFormat = useSimpleDictionaryFormat;
        }

        [MemberNotNull(nameof(_rootType))]
        private void Initialize(Type type,
            XmlDictionaryString? rootName,
            IEnumerable<Type>? knownTypes,
            int maxItemsInObjectGraph,
            bool ignoreExtensionDataObject,
            EmitTypeInformation emitTypeInformation,
            bool serializeReadOnlyTypes,
            DateTimeFormat? dateTimeFormat,
            bool useSimpleDictionaryFormat)
        {
            Initialize(type, knownTypes, maxItemsInObjectGraph, ignoreExtensionDataObject, emitTypeInformation, serializeReadOnlyTypes, dateTimeFormat, useSimpleDictionaryFormat);
            _rootName = ConvertXmlNameToJsonName(rootName);
            _rootNameRequiresMapping = CheckIfJsonNameRequiresMapping(_rootName);
        }

        internal override Type GetDeserializeType()
        {
            return _rootType;
        }

        private static PlatformNotSupportedException ExecutionUnsupported() =>
            new PlatformNotSupportedException(SR.NetWasm_DataContractSerializerUnsupported);
    }
}
