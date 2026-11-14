// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
//
// NetWasm deviation: outside design mode upstream sets each matching property through reflection;
// NetWasm throws PlatformNotSupportedException when a resource key matches. In design mode the
// TypeDescriptor property path is unchanged.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace System.ComponentModel
{
    /// <summary>
    /// The ComponentResourceManager is a resource manager object that
    /// provides simple functionality for enumerating resources for
    /// a component or object.
    /// </summary>
    public class ComponentResourceManager : ResourceManager
    {
        private Dictionary<CultureInfo, SortedList<string, object?>?>? _resourceSets;
        private CultureInfo? _neutralResourcesCulture;

        public ComponentResourceManager()
        {
        }

        public ComponentResourceManager(Type t) : base(t)
        {
        }

        /// <summary>
        /// The culture of the main assembly's neutral resources. If someone is asking for this culture's resources,
        /// we don't need to walk up the parent chain.
        /// </summary>
        private CultureInfo? NeutralResourcesCulture
        {
            get
            {
                if (_neutralResourcesCulture == null && MainAssembly != null)
                {
                    _neutralResourcesCulture = GetNeutralResourcesLanguage(MainAssembly);
                }

                return _neutralResourcesCulture;
            }
        }

        /// <summary>
        /// This method examines all the resources for the current culture.
        /// When it finds a resource with a key in the format of
        /// &quot;[objectName].[property name]&quot; it will apply that resource's value
        /// to the corresponding property on the object. If there is no matching
        /// property the resource will be ignored.
        /// </summary>
        [RequiresUnreferencedCode("The Type of value cannot be statically discovered.")]
        public void ApplyResources(object value, string objectName) => ApplyResources(value, objectName, null);

        /// <summary>
        /// This method examines all the resources for the provided culture.
        /// When it finds a resource with a key in the format of
        /// &quot;[objectName].[property name]&quot; or &quot;[objectName]-[property name]&quot; it will apply that resource's value
        /// to the corresponding property on the object. If there is no matching
        /// property the resource will be ignored.
        /// </summary>
        [RequiresUnreferencedCode("The Type of value cannot be statically discovered.")]
        public virtual void ApplyResources(object value, string objectName, CultureInfo? culture)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(objectName);
            ApplyResources(value, value.GetType(), objectName, culture);
        }

        /// <summary>
        /// This method examines all the resources for the provided culture.
        /// When it finds a resource with a key in the format of
        /// &quot;[objectName].[property name]&quot; or &quot;[objectName]-[property name]&quot; it will apply that resource's value
        /// to the corresponding property on the object. If there is no matching
        /// property the resource will be ignored.
        /// </summary>
        public virtual void ApplyResourcesToRegisteredType(object value, string objectName, CultureInfo? culture)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(objectName);

            Type typeFromValue = value.GetType();
            TypeDescriptor.ValidateRegisteredType(typeFromValue);
            ApplyResources(value, typeFromValue, objectName, culture);
        }

        private void ApplyResources(object value, Type typeFromValue, string objectName, CultureInfo? culture)
        {
            culture ??= CultureInfo.CurrentUICulture;

            // The general case here will be to always use the same culture, so optimize for
            // that. The resourceSets hashtable uses culture as a key. It's value is
            // a sorted dictionary that contains ALL the culture values (so it traverses up
            // the parent culture chain) for that culture. This means that if ApplyResources
            // is called with different cultures there could be some redundancy in the
            // table, but it allows the normal case of calling with a single culture to
            // be much faster.
            //

            // The reason we use a SortedDictionary here is to ensure the resources are applied
            // in an order consistent with codedom deserialization.
            SortedList<string, object?>? resources;

            if (_resourceSets == null)
            {
                _resourceSets = new Dictionary<CultureInfo, SortedList<string, object?>?>();
                resources = FillResources(culture, out _);
                _resourceSets[culture] = resources;
            }
            else
            {
                resources = _resourceSets.GetValueOrDefault(culture, defaultValue: null);
                if (resources == null || (resources.Comparer.Equals(StringComparer.OrdinalIgnoreCase) != IgnoreCase))
                {
                    resources = FillResources(culture, out _);
                    _resourceSets[culture] = resources;
                }
            }

            bool componentReflect = false;
            if (value is IComponent)
            {
                ISite? site = ((IComponent)value).Site;
                if (site != null && site.DesignMode)
                {
                    componentReflect = true;
                }
            }

            foreach (KeyValuePair<string, object?> kvp in resources)
            {
                // See if this key matches our object.
                string key = kvp.Key;

                if (!key.StartsWith(objectName, IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    continue;
                }

                // Character after objectName.Length should be a "." or a '-', or else we should continue.
                int idx = objectName.Length;
                if (key.Length <= idx || (key[idx] != '.' && key[idx] != '-'))
                {
                    continue;
                }

                // Bypass type descriptor if we are not in design mode. TypeDescriptor does an attribute
                // scan which is quite expensive.
                string propName = key.Substring(idx + 1);

                if (componentReflect)
                {
                    PropertyDescriptor? prop = TypeDescriptorGetProperties(value).Find(propName, IgnoreCase);

                    if (prop != null && !prop.IsReadOnly && (kvp.Value == null || prop.PropertyType.IsInstanceOfType(kvp.Value)))
                    {
                        prop.SetValue(value, kvp.Value);
                    }
                }
                else
                {
                    throw new PlatformNotSupportedException(SR.ComponentResourceManagerReflectionNotSupported);
                }
            }

            [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
                Justification = "Calling method either has RequiresUnreferencedCode or has registered types.")]
            static PropertyDescriptorCollection TypeDescriptorGetProperties(object value) => TypeDescriptor.GetProperties(value);
        }

        /// <summary>
        /// Recursive routine that creates a resource hashtable populated with
        /// resources for culture and all parent cultures.
        /// </summary>
        private SortedList<string, object?> FillResources(CultureInfo culture, out ResourceSet? resourceSet)
        {
            SortedList<string, object?> sd;
            ResourceSet? parentResourceSet = null;

            // Traverse parents first, so we always replace more
            // specific culture values with less specific.
            if (!culture.Equals(CultureInfo.InvariantCulture) && !culture.Equals(NeutralResourcesCulture))
            {
                sd = FillResources(culture.Parent, out parentResourceSet);
            }
            else
            {
                // We're at the bottom, so create the sorted dictionary
                if (IgnoreCase)
                {
                    sd = new SortedList<string, object?>(StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    sd = new SortedList<string, object?>(StringComparer.Ordinal);
                }
            }

            // Now walk culture's resource set. Another thing we
            // do here is ask ResourceManager to traverse up the
            // parent chain. We do NOT want to do this because
            // we are trawling up the parent chain ourselves, but by
            // passing in true for the second parameter the resource
            // manager will cache the culture it did find, so when we
            // do recurse all missing resources will be filled in
            // so we are very fast. That's why we remember what our
            // parent resource set's instance was -- if they are the
            // same, we're looking at a cache we've already applied.
            resourceSet = GetResourceSet(culture, true, true);
            if (resourceSet != null && !ReferenceEquals(resourceSet, parentResourceSet))
            {
                foreach (DictionaryEntry de in resourceSet)
                {
                    sd[(string)de.Key] = de.Value;
                }
            }

            return sd;
        }
    }
}
