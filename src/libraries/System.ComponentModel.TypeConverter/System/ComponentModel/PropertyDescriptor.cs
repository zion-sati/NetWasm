// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;

namespace System.ComponentModel
{
    /// <summary>
    /// Provides a description of a property.
    /// </summary>
    public abstract class PropertyDescriptor : MemberDescriptor
    {
        internal const string PropertyDescriptorPropertyTypeMessage = "PropertyDescriptor's PropertyType cannot be statically discovered.";

        private ConcurrentDictionary<object, EventHandler?>? _valueChangedHandlers;
        private object? _syncObject;

        /// <summary>
        /// Initializes a new instance of the <see cref='System.ComponentModel.PropertyDescriptor'/> class with the specified name and
        /// attributes.
        /// </summary>
        protected PropertyDescriptor(string name, Attribute[]? attrs) : base(name, attrs)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref='System.ComponentModel.PropertyDescriptor'/> class with
        /// the name and attributes in the specified <see cref='System.ComponentModel.MemberDescriptor'/>.
        /// </summary>
        protected PropertyDescriptor(MemberDescriptor descr) : base(descr)
        {
        }

        /// <summary>
        ///
        /// Initializes a new instance of the <see cref='System.ComponentModel.PropertyDescriptor'/> class with
        /// the name in the specified <see cref='System.ComponentModel.MemberDescriptor'/> and the
        /// attributes in both the <see cref='System.ComponentModel.MemberDescriptor'/> and the
        /// <see cref='System.Attribute'/> array.
        ///
        /// </summary>
        protected PropertyDescriptor(MemberDescriptor descr, Attribute[]? attrs) : base(descr, attrs)
        {
        }

        private object SyncObject => LazyInitializer.EnsureInitialized(ref _syncObject);

        /// <summary>
        /// When overridden in a derived class, gets the type of the
        /// component this property is bound to.
        /// </summary>
        public abstract Type ComponentType { get; }

        /// <summary>
        /// Gets the type converter for this property.
        /// </summary>
        public virtual TypeConverter Converter
        {
            get => throw new PlatformNotSupportedException("Automatic property converter discovery is not supported on NetWasm.");
        }

        /// <summary>
        /// Gets a value
        /// indicating whether this property should be localized, as
        /// specified in the <see cref='System.ComponentModel.LocalizableAttribute'/>.
        /// </summary>
        public virtual bool IsLocalizable => (LocalizableAttribute.Yes.Equals(Attributes[typeof(LocalizableAttribute)]));

        /// <summary>
        /// When overridden in a derived class, gets a value indicating whether this
        /// property is read-only.
        /// </summary>
        public abstract bool IsReadOnly { get; }

        /// <summary>
        /// Gets a value indicating whether this property should be serialized as specified
        /// in the <see cref='System.ComponentModel.DesignerSerializationVisibilityAttribute'/>.
        /// </summary>
        public DesignerSerializationVisibility SerializationVisibility
        {
            get
            {
                DesignerSerializationVisibilityAttribute attr = (DesignerSerializationVisibilityAttribute)Attributes[typeof(DesignerSerializationVisibilityAttribute)]!;
                return attr.Visibility;
            }
        }

        /// <summary>
        /// When overridden in a derived class, gets the type of the property.
        /// </summary>
        public abstract Type PropertyType { get; }

        /// <summary>
        /// Allows interested objects to be notified when this property changes.
        /// </summary>
        public virtual void AddValueChanged(object component, EventHandler handler)
        {
            ArgumentNullException.ThrowIfNull(component);
            ArgumentNullException.ThrowIfNull(handler);

            lock (SyncObject)
            {
                _valueChangedHandlers ??= new ConcurrentDictionary<object, EventHandler?>(concurrencyLevel: 1, capacity: 0);
                _valueChangedHandlers.AddOrUpdate(component, handler, (k, v) => (EventHandler?)Delegate.Combine(v, handler));
            }
        }

        /// <summary>
        /// When overridden in a derived class, indicates whether
        /// resetting the <paramref name="component "/>will change the value of the
        /// <paramref name="component"/>.
        /// </summary>
        public abstract bool CanResetValue(object component);

        /// <summary>
        /// Compares this to another <see cref='System.ComponentModel.PropertyDescriptor'/>
        /// to see if they are equivalent.
        /// NOTE: If you make a change here, you likely need to change GetHashCode() as well.
        /// </summary>
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            try
            {
                if (obj == this)
                {
                    return true;
                }

                if (obj == null)
                {
                    return false;
                }

                // Assume that 90% of the time we will only do a .Equals(...) for
                // propertydescriptor vs. propertydescriptor... avoid the overhead
                // of an instanceof call.

                if (obj is PropertyDescriptor pd && pd.NameHashCode == NameHashCode
                    && pd.PropertyType == PropertyType
                    && pd.Name.Equals(Name))
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Creates an instance of the specified type.
        /// </summary>
        protected object? CreateInstance(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
        {
            throw new PlatformNotSupportedException("Dynamic component construction is not supported on NetWasm.");
        }

        /// <summary>
        /// In an inheriting class, adds the attributes of the inheriting class to the
        /// specified list of attributes in the parent class. For duplicate attributes,
        /// the last one added to the list will be kept.
        /// </summary>
        protected override void FillAttributes(IList attributeList)
        {
            // Each time we fill our attributes, we should clear our cached
            // stuff.

            base.FillAttributes(attributeList);
        }

        [RequiresUnreferencedCode(PropertyDescriptorPropertyTypeMessage)]
        public PropertyDescriptorCollection GetChildProperties() => GetChildProperties(null, null);

        [RequiresUnreferencedCode(PropertyDescriptorPropertyTypeMessage + " " + AttributeCollection.FilterRequiresUnreferencedCodeMessage)]
        public PropertyDescriptorCollection GetChildProperties(Attribute[] filter) => GetChildProperties(null, filter);

        [RequiresUnreferencedCode(PropertyDescriptorPropertyTypeMessage + " The Type of instance cannot be statically discovered.")]
        public PropertyDescriptorCollection GetChildProperties(object instance) => GetChildProperties(instance, null);

        /// <summary>
        /// Retrieves the properties
        /// </summary>
        [RequiresUnreferencedCode(PropertyDescriptorPropertyTypeMessage + " The Type of instance cannot be statically discovered. " + AttributeCollection.FilterRequiresUnreferencedCodeMessage)]
        public virtual PropertyDescriptorCollection GetChildProperties(object? instance, Attribute[]? filter)
        {
            throw new PlatformNotSupportedException("Child property discovery is not supported on NetWasm.");
        }

        /// <summary>
        /// Gets an editor of the specified type.
        /// </summary>
        [RequiresUnreferencedCode("Design-time metadata discovery is unsupported." + " " + PropertyDescriptorPropertyTypeMessage)]
        public virtual object? GetEditor(Type editorBaseType)
        {
            throw new PlatformNotSupportedException("Property editor discovery is not supported on NetWasm.");
        }

        /// <summary>
        /// Try to keep this reasonable in [....] with Equals(). Specifically,
        /// if A.Equals(B) returns true, A &amp; B should have the same hash code.
        /// </summary>
        public override int GetHashCode() => NameHashCode ^ PropertyType.GetHashCode();

        /// <summary>
        /// This method returns the object that should be used during invocation of members.
        /// Normally the return value will be the same as the instance passed in. If
        /// someone associated another object with this instance, or if the instance is a
        /// custom type descriptor, GetInvocationTarget may return a different value.
        /// </summary>
        protected override object? GetInvocationTarget(Type type, object instance)
        {
            return base.GetInvocationTarget(type, instance);
        }

        /// <summary>
        /// Gets a type using its name.
        /// </summary>
        [RequiresUnreferencedCode("Calls ComponentType.Assembly.GetType on the non-fully qualified typeName, which the trimmer cannot recognize.")]
        [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        protected Type? GetTypeFromName(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] string? typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            throw new PlatformNotSupportedException("Component type-name discovery is not supported on NetWasm.");
        }

        /// <summary>
        /// When overridden in a derived class, gets the current value of the property on a component.
        /// </summary>
        public abstract object? GetValue(object? component);

        /// <summary>
        /// This should be called by your property descriptor implementation
        /// when the property value has changed.
        /// </summary>
        protected virtual void OnValueChanged(object? component, EventArgs e)
        {
            if (component != null)
            {
                _valueChangedHandlers?.GetValueOrDefault(component, defaultValue: null)?.Invoke(component, e);
            }
        }

        /// <summary>
        /// Allows interested objects to be notified when this property changes.
        /// </summary>
        public virtual void RemoveValueChanged(object component, EventHandler handler)
        {
            ArgumentNullException.ThrowIfNull(component);
            ArgumentNullException.ThrowIfNull(handler);

            if (_valueChangedHandlers != null)
            {
                lock (SyncObject)
                {
                    EventHandler? h = _valueChangedHandlers.GetValueOrDefault(component, defaultValue: null);
                    h = (EventHandler?)Delegate.Remove(h, handler);
                    if (h != null)
                    {
                        _valueChangedHandlers[component] = h;
                    }
                    else
                    {
                        _valueChangedHandlers.Remove(component, out EventHandler? _);
                    }
                }
            }
        }

        /// <summary>
        /// Return current set of ValueChanged event handlers for a specific
        /// component, in the form of a combined multicast event handler.
        /// Returns null if no event handlers currently assigned to component.
        /// </summary>
        protected internal EventHandler? GetValueChangedHandler(object component)
        {
            if (component != null && _valueChangedHandlers != null)
            {
                return _valueChangedHandlers.GetValueOrDefault(component, defaultValue: null);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// When overridden in a derived class, resets the value for this property of the component.
        /// </summary>
        public abstract void ResetValue(object component);

        /// <summary>
        /// When overridden in a derived class, sets the value of
        /// the component to a different value.
        /// </summary>
        public abstract void SetValue(object? component, object? value);

        /// <summary>
        /// When overridden in a derived class, indicates whether the
        /// value of this property needs to be persisted.
        /// </summary>
        public abstract bool ShouldSerializeValue(object component);

        /// <summary>
        /// Indicates whether value change notifications for this property may originate from outside the property
        /// descriptor, such as from the component itself (value=true), or whether notifications will only originate
        /// from direct calls made to PropertyDescriptor.SetValue (value=false). For example, the component may
        /// implement the INotifyPropertyChanged interface, or may have an explicit '{name}Changed' event for this property.
        /// </summary>
        public virtual bool SupportsChangeEvents => false;
    }
}
