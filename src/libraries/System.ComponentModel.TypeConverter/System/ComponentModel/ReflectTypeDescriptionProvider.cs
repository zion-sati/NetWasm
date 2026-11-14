// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce
// (ReflectTypeDescriptionProvider.cs and ReflectTypeDescriptionProvider.ReflectedTypeData.cs).
//
// NetWasm deviation: this is a bounded, reflection-free default provider, not upstream's reflection
// provider. It keeps upstream's name and internal surface so TypeDescriptor dispatches to it
// unchanged, and keeps upstream's intrinsic converter table, editor tables, extender discovery,
// component naming, cache and type registration code. It never discovers members or attributes and
// never constructs a type at run time:
// - GetConverter answers from the intrinsic converter table (primitives, string, the date and time
//   types, Guid, Uri, Version, CultureInfo, enums, Nullable<T>, arrays, ICollection, interfaces, and
//   TypeConverter for every other type). [TypeConverter] attributes are not read: NetWasm keeps no
//   attribute metadata for a Type chosen at run time, and activating a converter named by an
//   attribute needs Type.GetType and Activator. The converters that CoreLib's own types name in
//   their attributes upstream (Color, Point, Rectangle, Size, SizeF, IComponent implementers and
//   DesignerOptionCollection) are mapped statically ahead of the table, as upstream checks the
//   attribute first. A [TypeConverter] on a user type is not honored; explicit providers and
//   ICustomTypeDescriptor implementations supply other converters.
// - GetAttributes, GetProperties, GetEvents, GetDefaultEvent and GetDefaultProperty need member or
//   attribute discovery and throw PlatformNotSupportedException instead of returning empty data.
// - GetEditor skips the EditorAttribute lookup and searches the editor tables. An instance entry is
//   returned; a Type or type-name entry needs activation and throws PlatformNotSupportedException.
//   Editor tables are not populated by running the editor base type's class constructor.
// - With no applicable extender provider, extended properties are empty, as upstream. An
//   IExtenderProvider that extends the instance needs its ProvideProperty methods reflected and
//   throws PlatformNotSupportedException.
// - CreateInstance has no Activator fallback and throws PlatformNotSupportedException.
// - No reflected metadata is cached, so IsPopulated is always false and Refresh(Type) only drops
//   the cached converter.

using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Globalization;
using System.Threading;

namespace System.ComponentModel
{
    /// <summary>
    /// The default type description provider. Unless someone has provided a custom type description
    /// provider for a type or instance, or unless an instance implements ICustomTypeDescriptor, any
    /// query for type information will go through this class. There is a single instance of this
    /// class associated with "object".
    /// </summary>
    internal sealed partial class ReflectTypeDescriptionProvider : TypeDescriptionProvider
    {
        // ReflectedTypeData contains all of the type information we have gathered for a given type.
        private readonly CollectibleKeyConcurrentHashtable<Type, ReflectedTypeData> _typeData = new CollectibleKeyConcurrentHashtable<Type, ReflectedTypeData>();

        // This is where we store the various converters for intrinsic types.
        private static Hashtable? s_editorTables;
        private static Dictionary<object, IntrinsicTypeConverterData>? s_intrinsicTypeConverters;

        // For converters, etc that are bound to class attribute data, rather than a class
        // type, we have special key sentinel values that we put into the hash table.
        private static readonly object s_intrinsicReferenceKey = new object();
        private static readonly object s_intrinsicNullableKey = new object();

        // The key we put into IDictionaryService to store our cache dictionary.
        private static readonly object s_dictionaryKey = new object();

        // This is a cache on top of core reflection. The cache
        // builds itself recursively, so if you ask for the properties
        // on Control, Component and object are also automatically filled
        // in. The keys to the property and event caches are types.
        // The keys to the attribute cache are either MemberInfos or types.
        private static readonly Guid s_extenderPropertiesKey = Guid.NewGuid();
        private static readonly Guid s_extenderProviderPropertiesKey = Guid.NewGuid();

        internal static Guid ExtenderProviderKey { get; } = Guid.NewGuid();

        /// <summary>
        /// Creates a new ReflectTypeDescriptionProvider. The type is the
        /// type we will obtain type information for.
        /// </summary>
        internal ReflectTypeDescriptionProvider()
        {
        }

        private static Hashtable EditorTables => LazyInitializer.EnsureInitialized(ref s_editorTables, () => new Hashtable(4));

        /// <summary>
        /// Provides a way to create <see cref="TypeConverter"/> instances, and cache them where applicable.
        /// </summary>
        private sealed class IntrinsicTypeConverterData
        {
            private readonly Func<Type, TypeConverter> _constructionFunc;

            private readonly bool _cacheConverterInstance;

            private TypeConverter? _converterInstance;

            /// <summary>
            /// Creates a new instance of <see cref="IntrinsicTypeConverterData"/>.
            /// </summary>
            /// <param name="constructionFunc">
            /// A func that creates a new <see cref="TypeConverter"/> instance.
            /// </param>
            /// <param name="cacheConverterInstance">
            /// Indicates whether to cache created <see cref="TypeConverter"/> instances. This is false when the converter handles multiple types,
            /// specifically <see cref="EnumConverter"/>, <see cref="NullableConverter"/>, and <see cref="ReferenceConverter"/>.
            /// </param>
            public IntrinsicTypeConverterData(Func<Type, TypeConverter> constructionFunc, bool cacheConverterInstance = true)
            {
                _constructionFunc = constructionFunc;
                _cacheConverterInstance = cacheConverterInstance;
            }

            public TypeConverter GetOrCreateConverterInstance(Type innerType)
            {
                if (!_cacheConverterInstance)
                {
                    return _constructionFunc(innerType);
                }

                _converterInstance ??= _constructionFunc(innerType);
                return _converterInstance;
            }
        }

        /// <summary>
        /// This is a table we create for intrinsic types.There should be entries here ONLY for
        /// intrinsic types, as all other types we should be able to add attributes directly as metadata.
        /// </summary>
        /// <remarks>
        /// <see cref="Uri"/> and <see cref="CultureInfo"/> are the only types that can be inherited for which
        /// we have intrinsic converters for. The appropriate converter needs to be fetched when look-ups are done
        /// for deriving types. When adding to this cache, consider whether the type can be inherited and add the
        /// appropriate logic to handle this in <see cref="GetIntrinsicTypeConverter(Type)"/> below.
        /// </remarks>
        private static Dictionary<object, IntrinsicTypeConverterData> IntrinsicTypeConverters
        {
            get
            {
                return LazyInitializer.EnsureInitialized(ref s_intrinsicTypeConverters, () => new Dictionary<object, IntrinsicTypeConverterData>(32)
                {
                    // Add the intrinsics
                    //
                    // When modifying this list, be sure to update the initial dictionary capacity above
                    [typeof(bool)] = new IntrinsicTypeConverterData((type) => new BooleanConverter()),
                    [typeof(byte)] = new IntrinsicTypeConverterData((type) => new ByteConverter()),
                    [typeof(sbyte)] = new IntrinsicTypeConverterData((type) => new SByteConverter()),
                    [typeof(char)] = new IntrinsicTypeConverterData((type) => new CharConverter()),
                    [typeof(double)] = new IntrinsicTypeConverterData((type) => new DoubleConverter()),
                    [typeof(string)] = new IntrinsicTypeConverterData((type) => new StringConverter()),
                    [typeof(int)] = new IntrinsicTypeConverterData((type) => new Int32Converter()),
                    [typeof(Int128)] = new IntrinsicTypeConverterData((type) => new Int128Converter()),
                    [typeof(short)] = new IntrinsicTypeConverterData((type) => new Int16Converter()),
                    [typeof(long)] = new IntrinsicTypeConverterData((type) => new Int64Converter()),
                    [typeof(float)] = new IntrinsicTypeConverterData((type) => new SingleConverter()),
                    [typeof(Half)] = new IntrinsicTypeConverterData((type) => new HalfConverter()),
                    [typeof(UInt128)] = new IntrinsicTypeConverterData((type) => new UInt128Converter()),
                    [typeof(ushort)] = new IntrinsicTypeConverterData((type) => new UInt16Converter()),
                    [typeof(uint)] = new IntrinsicTypeConverterData((type) => new UInt32Converter()),
                    [typeof(ulong)] = new IntrinsicTypeConverterData((type) => new UInt64Converter()),
                    [typeof(object)] = new IntrinsicTypeConverterData((type) => new TypeConverter()),
                    [typeof(CultureInfo)] = new IntrinsicTypeConverterData((type) => new CultureInfoConverter()),
                    [typeof(DateOnly)] = new IntrinsicTypeConverterData((type) => new DateOnlyConverter()),
                    [typeof(DateTime)] = new IntrinsicTypeConverterData((type) => new DateTimeConverter()),
                    [typeof(DateTimeOffset)] = new IntrinsicTypeConverterData((type) => new DateTimeOffsetConverter()),
                    [typeof(decimal)] = new IntrinsicTypeConverterData((type) => new DecimalConverter()),
                    [typeof(TimeOnly)] = new IntrinsicTypeConverterData((type) => new TimeOnlyConverter()),
                    [typeof(TimeSpan)] = new IntrinsicTypeConverterData((type) => new TimeSpanConverter()),
                    [typeof(Guid)] = new IntrinsicTypeConverterData((type) => new GuidConverter()),
                    [typeof(Uri)] = new IntrinsicTypeConverterData((type) => new UriTypeConverter()),
                    [typeof(Version)] = new IntrinsicTypeConverterData((type) => new VersionConverter()),
                    // Special cases for things that are not bound to a specific type
                    //
                    [typeof(Array)] = new IntrinsicTypeConverterData((type) => new ArrayConverter()),
                    [typeof(ICollection)] = new IntrinsicTypeConverterData((type) => new CollectionConverter()),
                    [typeof(Enum)] = new IntrinsicTypeConverterData((type) => new EnumConverter(type), cacheConverterInstance: false),
                    [s_intrinsicNullableKey] = new IntrinsicTypeConverterData((type) => CreateNullableConverter(type), cacheConverterInstance: false),
                    [s_intrinsicReferenceKey] = new IntrinsicTypeConverterData((type) => new ReferenceConverter(type), cacheConverterInstance: false),
                });
            }
        }

        private static NullableConverter CreateNullableConverter(Type type) => new NullableConverter(type);

        /// <summary>
        /// Adds an editor table for the given editor base type.
        /// Typically, editors are specified as metadata on an object. If no metadata for a
        /// requested editor base type can be found on an object, however, the
        /// TypeDescriptor will search an editor
        /// table for the editor type, if one can be found.
        /// </summary>
        [RequiresUnreferencedCode("The Types specified in table may be trimmed, or have their static constructors trimmed.")]
        internal static void AddEditorTable(Type editorBaseType, Hashtable table)
        {
            ArgumentNullException.ThrowIfNull(editorBaseType);

            Debug.Assert(table != null, "COMPAT: Editor table should not be null"); // don't throw; RTM didn't so we can't do it either.

            lock (TypeDescriptor.s_commonSyncObject)
            {
                Hashtable editorTables = EditorTables;
                if (!editorTables.ContainsKey(editorBaseType))
                {
                    editorTables[editorBaseType] = table;
                }
            }
        }

        /// <summary>
        /// CreateInstance implementation. NetWasm has no Activator fallback; a caller-supplied
        /// TypeDescriptionProvider performs explicit construction.
        /// </summary>
        public override object? CreateInstance(
            IServiceProvider? provider,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type objectType,
            Type[]? argTypes,
            object?[]? args)
        {
            Debug.Assert(objectType != null, "Should have arg-checked before coming in here");

            throw new PlatformNotSupportedException(SR.TypeDescriptorTypeActivationNotSupported);
        }

        public override bool? RequireRegisteredTypes => true;
        public override bool IsRegisteredType(Type type)
        {
            if (_typeData.TryGetValue(type, out ReflectedTypeData? data) &&
                data.IsRegistered)
            {
                return true;
            }

            return IsIntrinsicType(type);
        }

        /// <summary>
        /// Retrieves custom attributes. Attribute discovery is unavailable.
        /// </summary>
        internal AttributeCollection GetAttributes([DynamicallyAccessedMembers(TypeDescriptor.AllMembersAndInterfaces)] Type type)
        {
            GetTypeData(type, createIfNeeded: true);
            throw new PlatformNotSupportedException(SR.TypeDescriptorMetadataDiscoveryNotSupported);
        }

        /// <summary>
        /// Our implementation of GetCache sits on top of IDictionaryService.
        /// </summary>
        public override IDictionary? GetCache(object instance)
        {
            IComponent? comp = instance as IComponent;
            if (comp != null && comp.Site != null)
            {
                IDictionaryService? ds = comp.Site.GetService(typeof(IDictionaryService)) as IDictionaryService;
                if (ds != null)
                {
                    IDictionary? dict = ds.GetValue(s_dictionaryKey) as IDictionary;
                    if (dict == null)
                    {
                        dict = new Hashtable();
                        ds.SetValue(s_dictionaryKey, dict);
                    }
                    return dict;
                }
            }

            return null;
        }

        /// <summary>
        /// Retrieves the class name for our type.
        /// </summary>
        internal string? GetClassName([DynamicallyAccessedMembers(TypeDescriptor.AllMembersAndInterfaces)] Type type)
        {
            ReflectedTypeData td = GetTypeData(type, true)!;
            return td.GetClassName();
        }

        /// <summary>
        /// Retrieves the class name for our type.
        /// </summary>
        internal string? GetClassNameFromRegisteredType(Type type)
        {
            ReflectedTypeData td = GetTypeDataFromRegisteredType(type);
            return td.GetClassName();
        }

        /// <summary>
        /// Retrieves the component name from the site.
        /// </summary>
        internal static string? GetComponentName(object? instance)
        {
            return ReflectedTypeData.GetComponentName(instance);
        }


        /// <summary>
        /// Retrieves the type converter from the intrinsic converter table.
        /// </summary>
        internal TypeConverter GetConverter([DynamicallyAccessedMembers(TypeDescriptor.AllMembersAndInterfaces)] Type type, object? instance)
        {
            ReflectedTypeData td = GetTypeData(type, createIfNeeded: true)!;
            return td.GetConverter();
        }

        internal TypeConverter GetConverterFromRegisteredType(Type type, object? instance)
        {
            ReflectedTypeData td = GetTypeDataFromRegisteredType(type);
            return td.GetConverter();
        }

        /// <summary>
        /// Return the default event. Finding it needs DefaultEventAttribute and event discovery.
        /// </summary>
        internal EventDescriptor? GetDefaultEvent([DynamicallyAccessedMembers(TypeDescriptor.AllMembersAndInterfaces)] Type type, object? instance)
        {
            GetTypeData(type, true);
            throw new PlatformNotSupportedException(SR.TypeDescriptorMetadataDiscoveryNotSupported);
        }

        /// <summary>
        /// Return the default property. Finding it needs DefaultPropertyAttribute and property discovery.
        /// </summary>
        internal PropertyDescriptor? GetDefaultProperty([DynamicallyAccessedMembers(TypeDescriptor.AllMembersAndInterfaces)] Type type, object? instance)
        {
            GetTypeData(type, createIfNeeded: true);
            throw new PlatformNotSupportedException(SR.TypeDescriptorMetadataDiscoveryNotSupported);
        }

        /// <summary>
        /// Retrieves the editor for the given base type from the editor tables.
        /// </summary>
        internal object? GetEditor([DynamicallyAccessedMembers(TypeDescriptor.AllMembersAndInterfaces)] Type type, object? instance, Type editorBaseType)
        {
            GetTypeData(type, createIfNeeded: true);

            object? editor = null;
            Hashtable? intrinsicEditors = GetEditorTable(editorBaseType);
            if (intrinsicEditors != null)
            {
                editor = GetIntrinsicTypeEditor(intrinsicEditors, type);
            }

            // As a quick sanity check, check to see that the editor we got back is of
            // the correct type.
            if (editor != null && !editorBaseType.IsInstanceOfType(editor))
            {
                Debug.Fail($"Editor {editor.GetType().FullName} is not an instance of {editorBaseType.FullName} but it is in that base types table.");
                editor = null;
            }

            return editor;
        }

        /// <summary>
        /// Retrieves a default editor table for the given editor base type.
        /// </summary>
        private static Hashtable? GetEditorTable(Type editorBaseType)
        {
            Hashtable editorTables = EditorTables;
            lock (TypeDescriptor.s_commonSyncObject)
            {
                return editorTables[editorBaseType] as Hashtable;
            }
        }

        /// <summary>
        /// Retrieves the events for a type. Event discovery is unavailable.
        /// </summary>
        internal EventDescriptorCollection GetEvents([DynamicallyAccessedMembers(TypeDescriptor.AllMembersAndInterfaces)] Type type)
        {
            GetTypeData(type, createIfNeeded: true);
            throw new PlatformNotSupportedException(SR.TypeDescriptorMetadataDiscoveryNotSupported);
        }

        internal EventDescriptorCollection GetEventsFromRegisteredType(Type type)
        {
            GetTypeDataFromRegisteredType(type);
            throw new PlatformNotSupportedException(SR.TypeDescriptorMetadataDiscoveryNotSupported);
        }

        /// <summary>
        /// Retrieves custom extender attributes. We don't support
        /// extender attributes, so we always return an empty collection.
        /// </summary>
        internal static AttributeCollection GetExtendedAttributes()
        {
            return AttributeCollection.Empty;
        }

        /// <summary>
        /// Retrieves the class name for our type.
        /// </summary>
        [RequiresUnreferencedCode("The Type of instance cannot be statically discovered.")]
        internal string? GetExtendedClassName(object instance)
        {
            return GetClassName(instance.GetType());
        }

        /// <summary>
        /// Retrieves the component name from the site.
        /// </summary>
        internal static string? GetExtendedComponentName(object instance)
        {
            return GetComponentName(instance);
        }

        /// <summary>
        /// Retrieves the type converter. If instance is non-null,
        /// it will be used to retrieve attributes. Otherwise, _type
        /// will be used.
        /// </summary>
        [RequiresUnreferencedCode("The Type of instance cannot be statically discovered. NullableConverter's UnderlyingType cannot be statically discovered.")]
        internal TypeConverter GetExtendedConverter(object instance)
        {
            return GetConverter(instance.GetType(), instance);
        }

        /// <summary>
        /// Return the default event. The default event is determined by the
        /// presence of a DefaultEventAttribute on the class.
        /// </summary>
        internal static EventDescriptor? GetExtendedDefaultEvent()
        {
            return null; // we don't support extended events.
        }

        /// <summary>
        /// Return the default property.
        /// </summary>
        internal static PropertyDescriptor? GetExtendedDefaultProperty()
        {
            return null; // extender properties are never the default.
        }

        /// <summary>
        /// Retrieves the editor for the given base type.
        /// </summary>
        [RequiresUnreferencedCode(TypeDescriptor.DesignTimeAttributeTrimmed + " The Type of instance cannot be statically discovered.")]
        internal object? GetExtendedEditor(object instance, Type editorBaseType)
        {
            return GetEditor(instance.GetType(), instance, editorBaseType);
        }

        /// <summary>
        /// Retrieves the events for this type.
        /// </summary>
        internal static EventDescriptorCollection GetExtendedEvents()
        {
            return EventDescriptorCollection.Empty;
        }

        /// <summary>
        /// Retrieves the properties for this type.
        /// </summary>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
            Justification = "Instance is verified at run-time to be registered.")]
        internal PropertyDescriptorCollection GetExtendedPropertiesFromRegisteredType(object instance) => GetExtendedProperties(instance);

        /// <summary>
        /// Retrieves the properties for this type.
        /// </summary>
        [RequiresUnreferencedCode("The Type of instance and its IExtenderProviders cannot be statically discovered.")]
        internal PropertyDescriptorCollection GetExtendedProperties(object instance)
        {
            // Is this object a sited component?  If not, then it
            // doesn't have any extender properties.
            //
            Type componentType = instance.GetType();

            // Check the component for extender providers. We prefer
            // IExtenderListService, but will use the container if that's
            // all we have.
            //
            IExtenderProvider[] extenders = GetExtenderProviders(instance);

            if (extenders.Length == 0)
            {
                return PropertyDescriptorCollection.Empty;
            }

            // The extended properties come from each extender's ProvidePropertyAttribute
            // and its Get/Set methods, which need reflection.
            throw new PlatformNotSupportedException(SR.TypeDescriptorMetadataDiscoveryNotSupported);
        }

        protected internal override IExtenderProvider[] GetExtenderProviders(object instance)
        {
            ArgumentNullException.ThrowIfNull(instance);

            IComponent? component = instance as IComponent;
            if (component != null && component.Site != null)
            {
                IExtenderListService? extenderList = component.Site.GetService(typeof(IExtenderListService)) as IExtenderListService;
                IDictionary? cache = TypeDescriptor.GetCache(instance);

                if (extenderList != null)
                {
                    return GetExtenders(extenderList.GetExtenderProviders(), instance, cache);
                }
                else
                {
                    IContainer? cont = component.Site.Container;
                    if (cont != null)
                    {
                        return GetExtenders(cont.Components, instance, cache);
                    }
                }
            }
            return Array.Empty<IExtenderProvider>();
        }

        /// <summary>
        /// GetExtenders builds a list of extender providers from
        /// a collection of components. It validates the extenders
        /// against any cached collection of extenders in the
        /// cache. If there is a discrepancy, this will erase
        /// any cached extender properties from the cache and
        /// save the updated extender list. If there is no
        /// discrepancy this will simply return the cached list.
        /// </summary>
        private static IExtenderProvider[] GetExtenders(ICollection components, object instance, IDictionary? cache)
        {
            bool newExtenders = false;
            int extenderCount = 0;
            IExtenderProvider[]? existingExtenders = null;

            //CanExtend is expensive. We will remember results of CanExtend for the first 64 extenders and using "long canExtend" as a bit vector.
            // we want to avoid memory allocation as well so we don't use some more sophisticated data structure like an array of booleans
            ulong canExtend = 0;
            int maxCanExtendResults = 64;
            // currentExtenders is what we intend to return. If the caller passed us
            // the return value from IExtenderListService, components will already be
            // an IExtenderProvider[]. If not, then we must treat components as an
            // opaque collection. We spend a great deal of energy here to avoid
            // copying or allocating memory because this method is called every
            // time a component is asked for its properties.
            IExtenderProvider[]? currentExtenders = components as IExtenderProvider[];

            if (cache != null)
            {
                existingExtenders = cache[ExtenderProviderKey] as IExtenderProvider[];
            }

            if (existingExtenders == null)
            {
                newExtenders = true;
            }

            int curIdx = 0;
            int idx = 0;

            if (currentExtenders != null)
            {
                for (curIdx = 0; curIdx < currentExtenders.Length; curIdx++)
                {
                    if (currentExtenders[curIdx].CanExtend(instance))
                    {
                        extenderCount++;
                        // Performance:We would like to call CanExtend as little as possible therefore we remember its result
                        if (curIdx < maxCanExtendResults)
                            canExtend |= (ulong)1 << curIdx;
                        if (!newExtenders && (idx >= existingExtenders!.Length || currentExtenders[curIdx] != existingExtenders[idx++]))
                        {
                            newExtenders = true;
                        }
                    }
                }
            }
            else if (components != null)
            {
                foreach (object obj in components)
                {
                    IExtenderProvider? prov = obj as IExtenderProvider;
                    if (prov != null && prov.CanExtend(instance))
                    {
                        extenderCount++;
                        if (curIdx < maxCanExtendResults)
                            canExtend |= (ulong)1 << curIdx;
                        if (!newExtenders && (idx >= existingExtenders!.Length || prov != existingExtenders[idx++]))
                        {
                            newExtenders = true;
                        }
                    }
                    curIdx++;
                }
            }
            if (existingExtenders != null && extenderCount != existingExtenders.Length)
            {
                newExtenders = true;
            }
            if (newExtenders)
            {
                if (currentExtenders == null || extenderCount != currentExtenders.Length)
                {
                    IExtenderProvider[] newExtenderArray = new IExtenderProvider[extenderCount];

                    curIdx = 0;
                    idx = 0;

                    if (currentExtenders != null && extenderCount > 0)
                    {
                        while (curIdx < currentExtenders.Length)
                        {
                            if ((curIdx < maxCanExtendResults && (canExtend & ((ulong)1 << curIdx)) != 0) ||
                                            (curIdx >= maxCanExtendResults && currentExtenders[curIdx].CanExtend(instance)))
                            {
                                Debug.Assert(idx < extenderCount, "There are more extenders than we expect");
                                newExtenderArray[idx++] = currentExtenders[curIdx];
                            }
                            curIdx++;
                        }
                        Debug.Assert(idx == extenderCount, "Wrong number of extenders");
                    }
                    else if (extenderCount > 0)
                    {
                        foreach (var component in components!)
                        {
                            IExtenderProvider? p = component as IExtenderProvider;

                            if (p != null && ((curIdx < maxCanExtendResults && (canExtend & ((ulong)1 << curIdx)) != 0) ||
                                                (curIdx >= maxCanExtendResults && p.CanExtend(instance))))
                            {
                                Debug.Assert(idx < extenderCount, "There are more extenders than we expect");
                                newExtenderArray[idx++] = p;
                            }
                            curIdx++;
                        }
                        Debug.Assert(idx == extenderCount, "Wrong number of extenders");
                    }
                    currentExtenders = newExtenderArray;
                }

                if (cache != null)
                {
                    cache[ExtenderProviderKey] = currentExtenders;
                    cache.Remove(s_extenderPropertiesKey);
                }
            }
            else
            {
                currentExtenders = existingExtenders!;
            }
            return currentExtenders;
        }

        /// <summary>
        /// Retrieves the owner for a property.
        /// </summary>
        internal static object GetExtendedPropertyOwner(object instance)
        {
            return GetPropertyOwner(instance.GetType(), instance);
        }

        //////////////////////////////////////////////////////////
        /// <summary>
        /// Provides a type descriptor for the given object. We only support this
        /// if the object is a component that
        /// </summary>
        [RequiresUnreferencedCode("The Type of instance cannot be statically discovered.")]
        public override ICustomTypeDescriptor GetExtendedTypeDescriptor(object instance)
        {
            Debug.Fail("This should never be invoked. TypeDescriptionNode should wrap for us.");
            return null;
        }

        public override ICustomTypeDescriptor? GetTypeDescriptorFromRegisteredType(Type objectType, object? instance)
        {
            Debug.Fail("This should never be invoked. TypeDescriptionNode should wrap for us.");
            return null;
        }

        /// <summary>
        /// The name of the specified component, or null if the component has no name.
        /// In many cases this will return the same value as GetComponentName. If the
        /// component resides in a nested container or has other nested semantics, it may
        /// return a different fully qualified name.
        ///
        /// If not overridden, the default implementation of this method will call
        /// GetComponentName.
        /// </summary>
        [RequiresUnreferencedCode("The Type of component cannot be statically discovered.")]
        public override string? GetFullComponentName(object component)
        {
            IComponent? comp = component as IComponent;
            INestedSite? ns = comp?.Site as INestedSite;
            if (ns != null)
            {
                return ns.FullName;
            }

            return TypeDescriptor.GetComponentName(component);
        }

        /// <summary>
        /// Retrieves the properties for a type. Property discovery is unavailable.
        /// </summary>
        internal PropertyDescriptorCollection GetProperties([DynamicallyAccessedMembers(TypeDescriptor.AllMembersAndInterfaces)] Type type)
        {
            GetTypeData(type, createIfNeeded: true);
            throw new PlatformNotSupportedException(SR.TypeDescriptorMetadataDiscoveryNotSupported);
        }

        internal PropertyDescriptorCollection GetPropertiesFromRegisteredType(Type type)
        {
            GetTypeDataFromRegisteredType(type);
            throw new PlatformNotSupportedException(SR.TypeDescriptorMetadataDiscoveryNotSupported);
        }

        /// <summary>
        /// Retrieves the owner for a property.
        /// </summary>
        internal static object GetPropertyOwner(Type type, object instance)
        {
            return TypeDescriptor.GetAssociation(type, instance);
        }

        /// <summary>
        /// Returns an Type for the given type. Since type implements IReflect,
        /// we just return objectType.
        /// </summary>
        [return: DynamicallyAccessedMembers(TypeDescriptor.ReflectTypesDynamicallyAccessedMembers)]
        public override Type GetReflectionType(
            [DynamicallyAccessedMembers(TypeDescriptor.ReflectTypesDynamicallyAccessedMembers)] Type objectType,
            object? instance)
        {
            Debug.Assert(objectType != null, "Should have arg-checked before coming in here");
            return objectType;
        }

        /// <summary>
        /// Returns the type data for the given type, or
        /// null if there is no type data for the type yet and
        /// createIfNeeded is false.
        /// </summary>
        private ReflectedTypeData? GetTypeData([DynamicallyAccessedMembers(TypeDescriptor.AllMembersAndInterfaces)] Type type, bool createIfNeeded)
        {
            if (_typeData.TryGetValue(type, out ReflectedTypeData? td))
            {
                Debug.Assert(td != null);
                return td;
            }

            lock (TypeDescriptor.s_commonSyncObject)
            {
                if (_typeData.TryGetValue(type, out td))
                {
                    Debug.Assert(td != null);

                    if (TypeDescriptor.RequireRegisteredTypes && !td.IsRegistered && !IsIntrinsicType(type))
                    {
                        TypeDescriptor.ThrowHelper.ThrowInvalidOperationException_RegisterTypeRequired(type);
                    }

                    return td;
                }

                if (TypeDescriptor.RequireRegisteredTypes && !IsIntrinsicType(type))
                {
                    // Since registering a type adds it to _typeData, this means the type was not registered.
                    TypeDescriptor.ThrowHelper.ThrowInvalidOperationException_RegisterTypeRequired(type);
                }

                if (createIfNeeded)
                {
                    td = new ReflectedTypeData(type, isRegisteredType: false);
                    _typeData[type] = td;
                }
            }

            return td;
        }

        private ReflectedTypeData GetTypeDataFromRegisteredType(Type type)
        {
            if (!_typeData.TryGetValue(type, out ReflectedTypeData? td))
            {
                if (IsIntrinsicType(type))
                {
                    return GetOrRegisterType(type);
                }

                // Since registering a type adds it to _typeData, this means the type was not registered.
                TypeDescriptor.ThrowHelper.ThrowInvalidOperationException_RegisterTypeRequired(type);
                td = null;
            }

            if (!td.IsRegistered && !IsIntrinsicType(type))
            {
                TypeDescriptor.ThrowHelper.ThrowInvalidOperationException_RegisterTypeRequired(type);
            }

            return td;
        }

        public override void RegisterType<[DynamicallyAccessedMembers(TypeDescriptor.RegisteredTypesDynamicallyAccessedMembers)] T>()
        {
            Type componentType = typeof(T);

            if (_typeData.ContainsKey(componentType))
            {
                return;
            }

            lock (TypeDescriptor.s_commonSyncObject)
            {
                if (_typeData.ContainsKey(componentType))
                {
                    return;
                }

                ReflectedTypeData td = new ReflectedTypeData(componentType, isRegisteredType: true);
                _typeData[componentType] = td;
            }
        }

        private ReflectedTypeData GetOrRegisterType(Type type)
        {
            if (_typeData.TryGetValue(type, out ReflectedTypeData? td))
            {
                return td;
            }

            lock (TypeDescriptor.s_commonSyncObject)
            {
                if (_typeData.TryGetValue(type, out td))
                {
                    return td;
                }

                if (td == null)
                {
                    td = new ReflectedTypeData(type, isRegisteredType: true);
                    _typeData[type] = td;
                }
            }

            return td;
        }

        /// <summary>
        /// This method returns a custom type descriptor for the given type / object.
        /// The objectType parameter is always valid, but the instance parameter may
        /// be null if no instance was passed to TypeDescriptor. The method should
        /// return a custom type descriptor for the object. If the method is not
        /// interested in providing type information for the object it should
        /// return null.
        /// </summary>
        public override ICustomTypeDescriptor GetTypeDescriptor([DynamicallyAccessedMembers(TypeDescriptor.AllMembersAndInterfaces)] Type objectType, object? instance)
        {
            Debug.Fail("This should never be invoked. TypeDescriptionNode should wrap for us.");
            return null;
        }


        /// <summary>
        /// This method returns true if the data cache in this reflection
        /// type descriptor has data in it. No reflected metadata is cached on NetWasm.
        /// </summary>
        internal bool IsPopulated(Type type)
        {
            return false;
        }

        /// <summary>
        /// Refreshes the cached data for the given type.
        /// </summary>
        internal void Refresh(Type type)
        {
            if (_typeData.TryGetValue(type, out ReflectedTypeData? td))
            {
                td.Refresh();
            }
        }

        /// <summary>
        /// Searches the provided intrinsic hashtable for a match with the object type.
        /// At the beginning, the hashtable contains types for the various editors.
        /// As this table is searched, the types for these objects
        /// are replaced with instances, so we only create as needed. This method
        /// does the search up the base class hierarchy and will create instances
        /// for types as needed. These instances are stored back into the table
        /// for the base type, and for the original component type, for fast access.
        /// </summary>
        private static object? GetIntrinsicTypeEditor(Hashtable table, Type callingType)
        {
            object? hashEntry = null;

            // We take a lock on this table. Nothing in this code calls out to
            // other methods that lock, so it should be fairly safe to grab this
            // lock. Also, this allows multiple intrinsic tables to be searched
            // at once.
            //
            lock (table)
            {
                Type? baseType = callingType;
                while (baseType != null && baseType != typeof(object))
                {
                    hashEntry = table[baseType];

                    // If the entry is a late-bound type, then try to
                    // resolve it.
                    //
                    if (hashEntry is string)
                    {
                        throw new PlatformNotSupportedException(SR.TypeDescriptorTypeActivationNotSupported);
                    }

                    if (hashEntry != null)
                    {
                        break;
                    }

                    baseType = baseType.BaseType;
                }

                // Now make a scan through each value in the table, looking for interfaces.
                // If we find one, see if the object implements the interface.
                //
                if (hashEntry == null)
                {
                    // Manual use of IDictionaryEnumerator instead of foreach to avoid DictionaryEntry box allocations.
                    IDictionaryEnumerator e = table.GetEnumerator();
                    while (e.MoveNext())
                    {
                        DictionaryEntry de = e.Entry;
                        Type? keyType = de.Key as Type;

                        if (keyType != null && keyType.IsInterface && keyType.IsAssignableFrom(callingType))
                        {
                            hashEntry = de.Value;
                            if (hashEntry is string)
                            {
                                throw new PlatformNotSupportedException(SR.TypeDescriptorTypeActivationNotSupported);
                            }

                            if (hashEntry != null)
                            {
                                break;
                            }
                        }
                    }
                }

                // Special case converters
                //
                if (hashEntry == null)
                {
                    if (Nullable.GetUnderlyingType(callingType) != null)
                    {
                        // Check if it is a nullable value
                        hashEntry = table[s_intrinsicNullableKey];
                    }
                    else if (callingType.IsInterface)
                    {
                        // Finally, check to see if the component type is some unknown interface.
                        // We have a custom converter for that.
                        hashEntry = table[s_intrinsicReferenceKey];
                    }
                }

                // Interfaces do not derive from object, so we
                // must handle the case of no hash entry here.
                //
                hashEntry ??= table[typeof(object)];

                // If the entry is a type, create an instance of it and then
                // replace the entry. This way we only need to create once.
                // We can only do this if the object doesn't want a type
                // in its constructor.
                //
                if (hashEntry is Type || hashEntry is string)
                {
                    throw new PlatformNotSupportedException(SR.TypeDescriptorTypeActivationNotSupported);
                }
            }

            return hashEntry;
        }


        /// <summary>
        /// Searches the intrinsic converter dictionary for a match with the object type.
        /// The strongly-typed dictionary maps object types to converter data objects which lazily
        /// creates (and caches for re-use, where applicable) converter instances.
        /// </summary>
        private static TypeConverter GetIntrinsicTypeConverter(Type callingType)
        {
            TypeConverter converter;

            // We take a lock on this dictionary. Nothing in this code calls out to
            // other methods that lock, so it should be fairly safe to grab this lock.
            lock (IntrinsicTypeConverters)
            {
                if (!IntrinsicTypeConverters.TryGetValue(callingType, out IntrinsicTypeConverterData? converterData))
                {
                    if (callingType.IsEnum)
                    {
                        converterData = IntrinsicTypeConverters[typeof(Enum)];
                    }
                    else if (callingType.IsArray)
                    {
                        converterData = IntrinsicTypeConverters[typeof(Array)];
                    }
                    else if (Nullable.GetUnderlyingType(callingType) != null)
                    {
                        converterData = IntrinsicTypeConverters[s_intrinsicNullableKey];
                    }
                    else if (typeof(ICollection).IsAssignableFrom(callingType))
                    {
                        converterData = IntrinsicTypeConverters[typeof(ICollection)];
                    }
                    else if (callingType.IsInterface)
                    {
                        converterData = IntrinsicTypeConverters[s_intrinsicReferenceKey];
                    }
                    else
                    {
                        // Uri and CultureInfo are the only types that can be derived from for which we have intrinsic converters.
                        // Check if the calling type derives from either and return the appropriate converter.

                        // We should have fetched converters for these types above.
                        Debug.Assert(callingType != typeof(Uri) && callingType != typeof(CultureInfo));

                        Type? key = null;

                        Type? baseType = callingType.BaseType;
                        while (baseType != null && baseType != typeof(object))
                        {
                            if (baseType == typeof(Uri) || baseType == typeof(CultureInfo))
                            {
                                key = baseType;
                                break;
                            }

                            baseType = baseType.BaseType;
                        }

                        // Handle other reference and value types. An instance of TypeConverter itself created and returned below.
                        key ??= typeof(object);

                        converterData = IntrinsicTypeConverters[key];
                    }
                }

                // This needs to be done within the lock as the dictionary value may be mutated in the following method call.
                converter = converterData.GetOrCreateConverterInstance(callingType);
            }

            return converter;
        }

        private static bool IsIntrinsicType(Type callingType)
        {
            TypeConverter converter = GetIntrinsicTypeConverter(callingType);

            // If TypeConverter is returned, it fell back to the System.Object converter.
            return (converter.GetType() != typeof(TypeConverter));
        }

        /// <summary>
        /// Returns the converter that a CoreLib type's [TypeConverter] attribute names upstream.
        /// NetWasm cannot read the attribute of a Type chosen at run time, so the mapping is static.
        /// </summary>
        private static TypeConverter? GetAttributedTypeConverter(Type type)
        {
            if (type == typeof(Color))
            {
                return new ColorConverter();
            }
            if (type == typeof(Point))
            {
                return new PointConverter();
            }
            if (type == typeof(Rectangle))
            {
                return new RectangleConverter();
            }
            if (type == typeof(Size))
            {
                return new SizeConverter();
            }
            if (type == typeof(SizeF))
            {
                return new SizeFConverter();
            }
            if (type == typeof(DesignerOptionService.DesignerOptionCollection))
            {
                return new DesignerOptionService.DesignerOptionConverter();
            }
            if (typeof(IComponent).IsAssignableFrom(type))
            {
                return new ComponentConverter(type);
            }

            return null;
        }

        /// <summary>
        /// The type data held for one type: its registration and its converter.
        /// </summary>
        private sealed class ReflectedTypeData
        {
            private readonly Type _type;
            private TypeConverter? _converter;
            private readonly bool _isRegistered;

            internal ReflectedTypeData(Type type, bool isRegisteredType)
            {
                _type = type;
                _isRegistered = isRegisteredType;
            }

            internal bool IsRegistered => _isRegistered;

            internal string? GetClassName() => _type.FullName;

            internal static string? GetComponentName(object? instance)
            {
                IComponent? comp = instance as IComponent;
                ISite? site = comp?.Site;
                if (site != null)
                {
                    INestedSite? nestedSite = site as INestedSite;
                    return (nestedSite?.FullName) ?? site.Name;
                }

                return null;
            }

            internal TypeConverter GetConverter()
            {
                return _converter ??= GetAttributedTypeConverter(_type) ?? GetIntrinsicTypeConverter(_type);
            }

            internal void Refresh()
            {
                _converter = null;
            }
        }
    }
}
