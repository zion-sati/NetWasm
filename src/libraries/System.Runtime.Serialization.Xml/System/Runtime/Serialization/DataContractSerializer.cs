// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: dotnet/runtime 811225a4's DataContractSerializer constructors, configuration and surrogate-provider
// storage, as the API family review selects them. Reading and writing need the reflection-based DataContract
// engine (RootContract), which NetWasm does not have, so every execution route - including the Stream and
// dictionary overloads inherited from XmlObjectSerializer - checks its argument and throws
// PlatformNotSupportedException before touching the reader, writer or stream or invoking a resolver or surrogate.

using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Xml;

namespace System.Runtime.Serialization
{
    public sealed class DataContractSerializer : XmlObjectSerializer
    {
        private Type _rootType;
        private int _maxItemsInObjectGraph;
        private bool _ignoreExtensionDataObject;
        private bool _preserveObjectReferences;
        private ReadOnlyCollection<Type>? _knownTypeCollection;
        internal IList<Type>? _knownTypeList;
        private DataContractResolver? _dataContractResolver;
        private ISerializationSurrogateProvider? _serializationSurrogateProvider;
        private bool _serializeReadOnlyTypes;

        internal static UTF8Encoding UTF8NoBom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
        internal static UTF8Encoding ValidatingUTF8 { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        internal static UnicodeEncoding UTF16NoBom { get; } = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: false);
        internal static UnicodeEncoding BEUTF16NoBom { get; } = new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: false);
        internal static UnicodeEncoding ValidatingUTF16 { get; } = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
        internal static UnicodeEncoding ValidatingBEUTF16 { get; } = new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);

        internal static Base64Encoding Base64Encoding { get; } = new Base64Encoding();
        internal static BinHexEncoding BinHexEncoding { get; } = new BinHexEncoding();

        // DataContract.GetNamespace's interning of namespace strings, without the rest of the engine.
        private static readonly Hashtable s_namespaces = new Hashtable();
        private static readonly object s_namespacesLock = new object();

        public DataContractSerializer(Type type)
            : this(type, (IEnumerable<Type>?)null)
        {
        }

        public DataContractSerializer(Type type, IEnumerable<Type>? knownTypes)
            : this(type, knownTypes, int.MaxValue, false, false)
        {
        }

        public DataContractSerializer(Type type, string rootName, string rootNamespace)
            : this(type, rootName, rootNamespace, null)
        {
        }

        public DataContractSerializer(Type type, string rootName, string rootNamespace, IEnumerable<Type>? knownTypes)
            : this(type, rootName, rootNamespace, knownTypes, false, false)
        {
        }

        internal DataContractSerializer(Type type, string rootName, string rootNamespace, IEnumerable<Type>? knownTypes,
                bool ignoreExtensionDataObject, bool preserveObjectReferences)
        {
            XmlDictionary dictionary = new XmlDictionary(2);
            Initialize(type, dictionary.Add(rootName), dictionary.Add(GetNamespace(rootNamespace)), knownTypes, int.MaxValue, ignoreExtensionDataObject, preserveObjectReferences, null, false);
        }

        public DataContractSerializer(Type type, XmlDictionaryString rootName, XmlDictionaryString rootNamespace)
            : this(type, rootName, rootNamespace, null)
        {
        }

        public DataContractSerializer(Type type, XmlDictionaryString rootName, XmlDictionaryString rootNamespace, IEnumerable<Type>? knownTypes)
        {
            Initialize(type, rootName, rootNamespace, knownTypes, int.MaxValue, false, false, null, false);
        }

        internal DataContractSerializer(Type type, IEnumerable<Type>? knownTypes, int maxItemsInObjectGraph, bool ignoreExtensionDataObject, bool preserveObjectReferences)
        {
            Initialize(type, knownTypes, maxItemsInObjectGraph, ignoreExtensionDataObject, preserveObjectReferences, null, false);
        }

        public DataContractSerializer(Type type, DataContractSerializerSettings? settings)
        {
            settings ??= new DataContractSerializerSettings();
            Initialize(type, settings.RootName, settings.RootNamespace, settings.KnownTypes, settings.MaxItemsInObjectGraph, settings.IgnoreExtensionDataObject,
                settings.PreserveObjectReferences, settings.DataContractResolver, settings.SerializeReadOnlyTypes);
        }

        [MemberNotNull(nameof(_rootType))]
        private void Initialize(Type type,
            IEnumerable<Type>? knownTypes,
            int maxItemsInObjectGraph,
            bool ignoreExtensionDataObject,
            bool preserveObjectReferences,
            DataContractResolver? dataContractResolver,
            bool serializeReadOnlyTypes)
        {
            ArgumentNullException.ThrowIfNull(type);

            _rootType = type;

            if (knownTypes != null)
            {
                _knownTypeList = new List<Type>();
                foreach (Type knownType in knownTypes)
                {
                    _knownTypeList.Add(knownType);
                }
            }

            ArgumentOutOfRangeException.ThrowIfNegative(maxItemsInObjectGraph);
            _maxItemsInObjectGraph = maxItemsInObjectGraph;

            _ignoreExtensionDataObject = ignoreExtensionDataObject;
            _preserveObjectReferences = preserveObjectReferences;
            _dataContractResolver = dataContractResolver;
            _serializeReadOnlyTypes = serializeReadOnlyTypes;
        }

        // Upstream also keeps the root name and namespace, which only the unported execution paths read.
        [MemberNotNull(nameof(_rootType))]
        private void Initialize(Type type, XmlDictionaryString? rootName, XmlDictionaryString? rootNamespace,
            IEnumerable<Type>? knownTypes,
            int maxItemsInObjectGraph,
            bool ignoreExtensionDataObject,
            bool preserveObjectReferences,
            DataContractResolver? dataContractResolver,
            bool serializeReadOnlyTypes)
        {
            Initialize(type, knownTypes, maxItemsInObjectGraph, ignoreExtensionDataObject, preserveObjectReferences, dataContractResolver, serializeReadOnlyTypes);
        }

        private static string GetNamespace(string key)
        {
            object? value = s_namespaces[key];

            if (value != null)
                return (string)value;

            lock (s_namespacesLock)
            {
                value = s_namespaces[key];

                if (value != null)
                    return (string)value;

                s_namespaces.Add(key, key);
                return key;
            }
        }

        public ReadOnlyCollection<Type> KnownTypes
        {
            get
            {
                return _knownTypeCollection ??=
                    _knownTypeList != null ?
                        new ReadOnlyCollection<Type>(_knownTypeList) :
                        ReadOnlyCollection<Type>.Empty;
            }
        }

        public int MaxItemsInObjectGraph
        {
            get { return _maxItemsInObjectGraph; }
        }

        internal ISerializationSurrogateProvider? SerializationSurrogateProvider
        {
            get { return _serializationSurrogateProvider; }
            set { _serializationSurrogateProvider = value; }
        }

        public bool PreserveObjectReferences
        {
            get { return _preserveObjectReferences; }
        }

        public bool IgnoreExtensionDataObject
        {
            get { return _ignoreExtensionDataObject; }
        }

        public DataContractResolver? DataContractResolver
        {
            get { return _dataContractResolver; }
        }

        public bool SerializeReadOnlyTypes
        {
            get { return _serializeReadOnlyTypes; }
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
        public override void WriteStartObject(XmlWriter writer, object? graph)
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
        public override void WriteEndObject(XmlWriter writer)
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

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public override void WriteObjectContent(XmlDictionaryWriter writer, object? graph)
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
        public void WriteObject(XmlDictionaryWriter writer, object? graph, DataContractResolver? dataContractResolver)
        {
            ArgumentNullException.ThrowIfNull(writer);
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
        public override bool IsStartObject(XmlReader reader)
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
        public override bool IsStartObject(XmlDictionaryReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            throw ExecutionUnsupported();
        }

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public object? ReadObject(XmlDictionaryReader reader, bool verifyObjectName, DataContractResolver? dataContractResolver)
        {
            ArgumentNullException.ThrowIfNull(reader);
            throw ExecutionUnsupported();
        }

        internal override Type GetDeserializeType()
        {
            return _rootType;
        }

        private static PlatformNotSupportedException ExecutionUnsupported() =>
            new PlatformNotSupportedException(SR.NetWasm_DataContractSerializerUnsupported);
    }
}
