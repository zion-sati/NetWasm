// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: NetWasm has no dynamic language runtime, so GetMetaObject throws
// PlatformNotSupportedException instead of returning MetaExpando, and MetaExpando, its reflected
// RuntimeOps method fields and the RuntimeOps helpers are not ported. The dictionary,
// notification and class-transition behaviour is upstream's.

using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Dynamic.Utils;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Dynamic
{
    /// <summary>
    /// Represents an object with members that can be dynamically added and removed at runtime.
    /// </summary>
    public sealed class ExpandoObject : IDynamicMetaObjectProvider, IDictionary<string, object?>, INotifyPropertyChanged
    {
        internal readonly object LockObject;                          // the read-only field is used for locking the Expando object
        private ExpandoData _data;                                    // the data currently being held by the Expando object
        private int _count;                                           // the count of available members

        internal static readonly object Uninitialized = new object(); // A marker object used to identify that a value is uninitialized.

        internal const int AmbiguousMatchFound = -2;        // The value is used to indicate there exists ambiguous match in the Expando object
        internal const int NoMatch = -1;                    // The value is used to indicate there is no matching member

        private PropertyChangedEventHandler? _propertyChanged;

        /// <summary>
        /// Creates a new ExpandoObject with no members.
        /// </summary>
        public ExpandoObject()
        {
            _data = ExpandoData.Empty;
            LockObject = new object();
        }

        #region Get/Set/Delete Helpers

        /// <summary>
        /// Try to get the data stored for the specified class at the specified index.  If the
        /// class has changed a full lookup for the slot will be performed and the correct
        /// value will be retrieved.
        /// </summary>
        internal bool TryGetValue(object? indexClass, int index, string name, bool ignoreCase, out object? value)
        {
            // read the data now.  The data is immutable so we get a consistent view.
            // If there's a concurrent writer they will replace data and it just appears
            // that we won the race
            ExpandoData data = _data;
            if (data.Class != indexClass || ignoreCase)
            {
                /* Re-search for the index matching the name here if
                 *  1) the class has changed, we need to get the correct index and return
                 *  the value there.
                 *  2) the search is case insensitive:
                 *      a. the member specified by index may be deleted, but there might be other
                 *      members matching the name if the binder is case insensitive.
                 *      b. the member that exactly matches the name didn't exist before and exists now,
                 *      need to find the exact match.
                 */
                index = data.Class.GetValueIndex(name, ignoreCase, this);
                if (index == ExpandoObject.AmbiguousMatchFound)
                {
                    throw System.Linq.Expressions.Error.AmbiguousMatchInExpandoObject(name);
                }
            }

            if (index == ExpandoObject.NoMatch)
            {
                value = null;
                return false;
            }

            // Capture the value into a temp, so it doesn't get mutated after we check
            // for Uninitialized.
            object? temp = data[index];
            if (temp == Uninitialized)
            {
                value = null;
                return false;
            }

            // index is now known to be correct
            value = temp;
            return true;
        }

        /// <summary>
        /// Sets the data for the specified class at the specified index.  If the class has
        /// changed then a full look for the slot will be performed.  If the new class does
        /// not have the provided slot then the Expando's class will change. Only case sensitive
        /// setter is supported in ExpandoObject.
        /// </summary>
        internal void TrySetValue(object? indexClass, int index, object? value, string name, bool ignoreCase, bool add)
        {
            ExpandoData data;
            object? oldValue;

            lock (LockObject)
            {
                data = _data;

                if (data.Class != indexClass || ignoreCase)
                {
                    // The class has changed or we are doing a case-insensitive search,
                    // we need to get the correct index and set the value there.  If we
                    // don't have the value then we need to promote the class - that
                    // should only happen when we have multiple concurrent writers.
                    index = data.Class.GetValueIndex(name, ignoreCase, this);
                    if (index == ExpandoObject.AmbiguousMatchFound)
                    {
                        throw System.Linq.Expressions.Error.AmbiguousMatchInExpandoObject(name);
                    }
                    if (index == ExpandoObject.NoMatch)
                    {
                        // Before creating a new class with the new member, need to check
                        // if there is the exact same member but is deleted. We should reuse
                        // the class if there is such a member.
                        int exactMatch = ignoreCase ?
                            data.Class.GetValueIndexCaseSensitive(name) :
                            index;
                        if (exactMatch != ExpandoObject.NoMatch)
                        {
                            Debug.Assert(data[exactMatch] == Uninitialized);
                            index = exactMatch;
                        }
                        else
                        {
                            ExpandoClass newClass = data.Class.FindNewClass(name);
                            data = PromoteClassCore(data.Class, newClass);
                            // After the class promotion, there must be an exact match,
                            // so we can do case-sensitive search here.
                            index = data.Class.GetValueIndexCaseSensitive(name);
                            Debug.Assert(index != ExpandoObject.NoMatch);
                        }
                    }
                }

                // Setting an uninitialized member increases the count of available members
                oldValue = data[index];
                if (oldValue == Uninitialized)
                {
                    _count++;
                }
                else if (add)
                {
                    throw System.Linq.Expressions.Error.SameKeyExistsInExpando(name);
                }

                data[index] = value;
            }

            // Notify property changed outside the lock
            PropertyChangedEventHandler? propertyChanged = _propertyChanged;
            if (propertyChanged != null && value != oldValue)
            {
                propertyChanged(this, new PropertyChangedEventArgs(data.Class.Keys[index]));
            }
        }

        /// <summary>
        /// Deletes the data stored for the specified class at the specified index.
        /// </summary>
        internal bool TryDeleteValue(object? indexClass, int index, string name, bool ignoreCase, object? deleteValue)
        {
            ExpandoData data;
            lock (LockObject)
            {
                data = _data;

                if (data.Class != indexClass || ignoreCase)
                {
                    // the class has changed or we are doing a case-insensitive search,
                    // we need to get the correct index.  If there is no associated index
                    // we simply can't have the value and we return false.
                    index = data.Class.GetValueIndex(name, ignoreCase, this);
                    if (index == ExpandoObject.AmbiguousMatchFound)
                    {
                        throw System.Linq.Expressions.Error.AmbiguousMatchInExpandoObject(name);
                    }
                }
                if (index == ExpandoObject.NoMatch)
                {
                    return false;
                }

                object? oldValue = data[index];
                if (oldValue == Uninitialized)
                {
                    return false;
                }

                // Make sure the value matches, if requested.
                //
                // It's a shame we have to call Equals with the lock held but
                // there doesn't seem to be a good way around that, and
                // ConcurrentDictionary in mscorlib does the same thing.
                if (deleteValue != Uninitialized && !object.Equals(oldValue, deleteValue))
                {
                    return false;
                }

                data[index] = Uninitialized;

                // Deleting an available member decreases the count of available members
                _count--;
            }

            // Notify property changed outside the lock
            _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(data.Class.Keys[index]));

            return true;
        }

        /// <summary>
        /// Returns true if the member at the specified index has been deleted,
        /// otherwise false. Call this function holding the lock.
        /// </summary>
        internal bool IsDeletedMember(int index)
        {
            ContractUtils.AssertLockHeld(LockObject);
            Debug.Assert(index >= 0 && index <= _data.Length);

            if (index == _data.Length)
            {
                // The member is a newly added by SetMemberBinder and not in data yet
                return false;
            }

            return _data[index] == ExpandoObject.Uninitialized;
        }

        /// <summary>
        /// Exposes the ExpandoClass which we've associated with this
        /// Expando object.  Used for type checks in rules.
        /// </summary>
        internal ExpandoClass Class => _data.Class;

        /// <summary>
        /// Promotes the class from the old type to the new type and returns the new
        /// ExpandoData object.
        /// </summary>
        private ExpandoData PromoteClassCore(ExpandoClass oldClass, ExpandoClass newClass)
        {
            Debug.Assert(oldClass != newClass);
            ContractUtils.AssertLockHeld(LockObject);

            if (_data.Class == oldClass)
            {
                _data = _data.UpdateClass(newClass);
            }

            return _data;
        }

        /// <summary>
        /// Internal helper to promote a class.  Called from our RuntimeOps helper.  This
        /// version simply doesn't expose the ExpandoData object which is a private
        /// data structure.
        /// </summary>
        internal void PromoteClass(object oldClass, object newClass)
        {
            lock (LockObject)
            {
                PromoteClassCore((ExpandoClass)oldClass, (ExpandoClass)newClass);
            }
        }

        #endregion

        #region IDynamicMetaObjectProvider Members

        DynamicMetaObject IDynamicMetaObjectProvider.GetMetaObject(Expression parameter)
        {
            throw new PlatformNotSupportedException(SR.PlatformNotSupported_DynamicBinding);
        }

        #endregion

        #region Helper methods
        private void TryAddMember(string key, object? value)
        {
            ArgumentNullException.ThrowIfNull(key);
            // Pass null to the class, which forces lookup.
            TrySetValue(null, -1, value, key, ignoreCase: false, add: true);
        }

        private bool TryGetValueForKey(string key, out object? value)
        {
            // Pass null to the class, which forces lookup.
            return TryGetValue(null, -1, key, ignoreCase: false, value: out value);
        }

        private bool ExpandoContainsKey(string key)
        {
            ContractUtils.AssertLockHeld(LockObject);
            return _data.Class.GetValueIndexCaseSensitive(key) >= 0;
        }

        // We create a non-generic type for the debug view for each different collection type
        // that uses DebuggerTypeProxy, instead of defining a generic debug view type and
        // using different instantiations. The reason for this is that support for generics
        // with using DebuggerTypeProxy is limited. For C#, DebuggerTypeProxy supports only
        // open types (from MSDN https://learn.microsoft.com/visualstudio/debugger/using-debuggertypeproxy-attribute).
        private sealed class KeyCollectionDebugView
        {
            private readonly ICollection<string> _collection;

            public KeyCollectionDebugView(ICollection<string> collection)
            {
                ArgumentNullException.ThrowIfNull(collection);
                _collection = collection;
            }

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public string[] Items
            {
                get
                {
                    string[] items = new string[_collection.Count];
                    _collection.CopyTo(items, 0);
                    return items;
                }
            }
        }

        [DebuggerTypeProxy(typeof(KeyCollectionDebugView))]
        [DebuggerDisplay("Count = {Count}")]
        private sealed class KeyCollection : ICollection<string>
        {
            private readonly ExpandoObject _expando;
            private readonly int _expandoVersion;
            private readonly int _expandoCount;
            private readonly ExpandoData _expandoData;

            internal KeyCollection(ExpandoObject expando)
            {
                lock (expando.LockObject)
                {
                    _expando = expando;
                    _expandoVersion = expando._data.Version;
                    _expandoCount = expando._count;
                    _expandoData = expando._data;
                }
            }

            private void CheckVersion()
            {
                if (_expando._data.Version != _expandoVersion || _expandoData != _expando._data)
                {
                    //the underlying expando object has changed
                    throw System.Linq.Expressions.Error.CollectionModifiedWhileEnumerating();
                }
            }

            #region ICollection<string> Members

            public void Add(string item)
            {
                throw System.Linq.Expressions.Error.CollectionReadOnly();
            }

            public void Clear()
            {
                throw System.Linq.Expressions.Error.CollectionReadOnly();
            }

            public bool Contains(string item)
            {
                lock (_expando.LockObject)
                {
                    CheckVersion();
                    return _expando.ExpandoContainsKey(item);
                }
            }

            public void CopyTo(string[] array, int arrayIndex)
            {
                ArgumentNullException.ThrowIfNull(array);
                ContractUtils.RequiresArrayRange(array, arrayIndex, _expandoCount, nameof(arrayIndex), nameof(Count));
                lock (_expando.LockObject)
                {
                    CheckVersion();
                    ExpandoData data = _expando._data;
                    for (int i = 0; i < data.Class.Keys.Length; i++)
                    {
                        if (data[i] != Uninitialized)
                        {
                            array[arrayIndex++] = data.Class.Keys[i];
                        }
                    }
                }
            }

            public int Count
            {
                get
                {
                    CheckVersion();
                    return _expandoCount;
                }
            }

            public bool IsReadOnly => true;

            public bool Remove(string item)
            {
                throw System.Linq.Expressions.Error.CollectionReadOnly();
            }

            #endregion

            #region IEnumerable<string> Members

            public IEnumerator<string> GetEnumerator()
            {
                for (int i = 0, n = _expandoData.Class.Keys.Length; i < n; i++)
                {
                    CheckVersion();
                    if (_expandoData[i] != Uninitialized)
                    {
                        yield return _expandoData.Class.Keys[i];
                    }
                }
            }

            #endregion

            #region IEnumerable Members

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            #endregion
        }

        // We create a non-generic type for the debug view for each different collection type
        // that uses DebuggerTypeProxy, instead of defining a generic debug view type and
        // using different instantiations. The reason for this is that support for generics
        // with using DebuggerTypeProxy is limited. For C#, DebuggerTypeProxy supports only
        // open types (from MSDN https://learn.microsoft.com/visualstudio/debugger/using-debuggertypeproxy-attribute).
        private sealed class ValueCollectionDebugView
        {
            private readonly ICollection<object> _collection;

            public ValueCollectionDebugView(ICollection<object> collection)
            {
                ArgumentNullException.ThrowIfNull(collection);
                _collection = collection;
            }

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public object[] Items
            {
                get
                {
                    object[] items = new object[_collection.Count];
                    _collection.CopyTo(items, 0);
                    return items;
                }
            }
        }

        [DebuggerTypeProxy(typeof(ValueCollectionDebugView))]
        [DebuggerDisplay("Count = {Count}")]
        private sealed class ValueCollection : ICollection<object?>
        {
            private readonly ExpandoObject _expando;
            private readonly int _expandoVersion;
            private readonly int _expandoCount;
            private readonly ExpandoData _expandoData;

            internal ValueCollection(ExpandoObject expando)
            {
                lock (expando.LockObject)
                {
                    _expando = expando;
                    _expandoVersion = expando._data.Version;
                    _expandoCount = expando._count;
                    _expandoData = expando._data;
                }
            }

            private void CheckVersion()
            {
                if (_expando._data.Version != _expandoVersion || _expandoData != _expando._data)
                {
                    //the underlying expando object has changed
                    throw System.Linq.Expressions.Error.CollectionModifiedWhileEnumerating();
                }
            }

            #region ICollection<string> Members

            public void Add(object? item)
            {
                throw System.Linq.Expressions.Error.CollectionReadOnly();
            }

            public void Clear()
            {
                throw System.Linq.Expressions.Error.CollectionReadOnly();
            }

            public bool Contains(object? item)
            {
                lock (_expando.LockObject)
                {
                    CheckVersion();

                    ExpandoData data = _expando._data;
                    for (int i = 0; i < data.Class.Keys.Length; i++)
                    {
                        // See comment in TryDeleteValue; it's okay to call
                        // object.Equals with the lock held.
                        if (object.Equals(data[i], item))
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }

            public void CopyTo(object?[] array, int arrayIndex)
            {
                ArgumentNullException.ThrowIfNull(array);
                ContractUtils.RequiresArrayRange(array, arrayIndex, _expandoCount, nameof(arrayIndex), nameof(Count));
                lock (_expando.LockObject)
                {
                    CheckVersion();
                    ExpandoData data = _expando._data;
                    for (int i = 0; i < data.Class.Keys.Length; i++)
                    {
                        if (data[i] != Uninitialized)
                        {
                            array[arrayIndex++] = data[i];
                        }
                    }
                }
            }

            public int Count
            {
                get
                {
                    CheckVersion();
                    return _expandoCount;
                }
            }

            public bool IsReadOnly => true;

            public bool Remove(object? item)
            {
                throw System.Linq.Expressions.Error.CollectionReadOnly();
            }

            #endregion

            #region IEnumerable<string> Members

            public IEnumerator<object?> GetEnumerator()
            {
                ExpandoData data = _expando._data;
                for (int i = 0; i < data.Class.Keys.Length; i++)
                {
                    CheckVersion();
                    // Capture the value into a temp so we don't inadvertently
                    // return Uninitialized.
                    object? temp = data[i];
                    if (temp != Uninitialized)
                    {
                        yield return temp;
                    }
                }
            }

            #endregion

            #region IEnumerable Members

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            #endregion
        }

        #endregion

        #region IDictionary<string, object> Members

        ICollection<string> IDictionary<string, object?>.Keys => new KeyCollection(this);

        ICollection<object?> IDictionary<string, object?>.Values => new ValueCollection(this);

        object? IDictionary<string, object?>.this[string key]
        {
            get
            {
                if (!TryGetValueForKey(key, out object? value))
                {
                    throw System.Linq.Expressions.Error.KeyDoesNotExistInExpando(key);
                }
                return value;
            }
            set
            {
                ArgumentNullException.ThrowIfNull(key);
                // Pass null to the class, which forces lookup.
                TrySetValue(null, -1, value, key, ignoreCase: false, add: false);
            }
        }

        void IDictionary<string, object?>.Add(string key, object? value)
        {
            this.TryAddMember(key, value);
        }

        bool IDictionary<string, object?>.ContainsKey(string key)
        {
            ArgumentNullException.ThrowIfNull(key);

            ExpandoData data = _data;
            int index = data.Class.GetValueIndexCaseSensitive(key);
            return index >= 0 && data[index] != Uninitialized;
        }

        bool IDictionary<string, object?>.Remove(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            // Pass null to the class, which forces lookup.
            return TryDeleteValue(null, -1, key, ignoreCase: false, deleteValue: Uninitialized);
        }

        bool IDictionary<string, object?>.TryGetValue(string key, out object? value)
        {
            return TryGetValueForKey(key, out value);
        }

        #endregion

        #region ICollection<KeyValuePair<string, object>> Members

        int ICollection<KeyValuePair<string, object?>>.Count => _count;

        bool ICollection<KeyValuePair<string, object?>>.IsReadOnly => false;

        void ICollection<KeyValuePair<string, object?>>.Add(KeyValuePair<string, object?> item)
        {
            TryAddMember(item.Key, item.Value);
        }

        void ICollection<KeyValuePair<string, object?>>.Clear()
        {
            // We remove both class and data!
            ExpandoData data;
            lock (LockObject)
            {
                data = _data;
                _data = ExpandoData.Empty;
                _count = 0;
            }

            // Notify property changed for all properties.
            var propertyChanged = _propertyChanged;
            if (propertyChanged != null)
            {
                for (int i = 0, n = data.Class.Keys.Length; i < n; i++)
                {
                    if (data[i] != Uninitialized)
                    {
                        propertyChanged(this, new PropertyChangedEventArgs(data.Class.Keys[i]));
                    }
                }
            }
        }

        bool ICollection<KeyValuePair<string, object?>>.Contains(KeyValuePair<string, object?> item)
        {
            if (!TryGetValueForKey(item.Key, out object? value))
            {
                return false;
            }

            return object.Equals(value, item.Value);
        }

        void ICollection<KeyValuePair<string, object?>>.CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);

            // We want this to be atomic and not throw, though we must do the range checks inside this lock.
            lock (LockObject)
            {
                ContractUtils.RequiresArrayRange(array, arrayIndex, _count, nameof(arrayIndex), nameof(ICollection<KeyValuePair<string, object>>.Count));
                foreach (KeyValuePair<string, object?> item in this)
                {
                    array[arrayIndex++] = item;
                }
            }
        }

        bool ICollection<KeyValuePair<string, object?>>.Remove(KeyValuePair<string, object?> item)
        {
            return TryDeleteValue(null, -1, item.Key, ignoreCase: false, deleteValue: item.Value);
        }

        #endregion

        #region IEnumerable<KeyValuePair<string, object>> Member

        IEnumerator<KeyValuePair<string, object?>> IEnumerable<KeyValuePair<string, object?>>.GetEnumerator()
        {
            ExpandoData data = _data;
            return GetExpandoEnumerator(data, data.Version);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            ExpandoData data = _data;
            return GetExpandoEnumerator(data, data.Version);
        }

        // Note: takes the data and version as parameters so they will be
        // captured before the first call to MoveNext().
        private IEnumerator<KeyValuePair<string, object?>> GetExpandoEnumerator(ExpandoData data, int version)
        {
            for (int i = 0; i < data.Class.Keys.Length; i++)
            {
                if (_data.Version != version || data != _data)
                {
                    // The underlying expando object has changed:
                    // 1) the version of the expando data changed
                    // 2) the data object is changed
                    throw System.Linq.Expressions.Error.CollectionModifiedWhileEnumerating();
                }
                // Capture the value into a temp so we don't inadvertently
                // return Uninitialized.
                object? temp = data[i];
                if (temp != Uninitialized)
                {
                    yield return new KeyValuePair<string, object?>(data.Class.Keys[i], temp);
                }
            }
        }

        #endregion

        #region ExpandoData

        /// <summary>
        /// Stores the class and the data associated with the class as one atomic
        /// pair.  This enables us to do a class check in a thread safe manner w/o
        /// requiring locks.
        /// </summary>
        private sealed class ExpandoData
        {
            internal static readonly ExpandoData Empty = new ExpandoData();

            /// <summary>
            /// the dynamically assigned class associated with the Expando object
            /// </summary>
            internal readonly ExpandoClass Class;

            /// <summary>
            /// data stored in the expando object, key names are stored in the class.
            ///
            /// Expando._data must be locked when mutating the value.  Otherwise a copy of it
            /// could be made and lose values.
            /// </summary>
            private readonly object?[] _dataArray;

            /// <summary>
            /// Indexer for getting/setting the data
            /// </summary>
            internal object? this[int index]
            {
                get
                {
                    return _dataArray[index];
                }
                set
                {
                    //when the array is updated, version increases, even the new value is the same
                    //as previous. Dictionary type has the same behavior.
                    _version++;
                    _dataArray[index] = value;
                }
            }

            internal int Version => _version;

            internal int Length => _dataArray.Length;

            /// <summary>
            /// Constructs an empty ExpandoData object with the empty class and no data.
            /// </summary>
            private ExpandoData()
            {
                Class = ExpandoClass.Empty;
                _dataArray = Array.Empty<object>();
            }

            /// <summary>
            /// the version of the ExpandoObject that tracks set and delete operations
            /// </summary>
            private int _version;

            /// <summary>
            /// Constructs a new ExpandoData object with the specified class and data.
            /// </summary>
            internal ExpandoData(ExpandoClass klass, object?[] data, int version)
            {
                Class = klass;
                _dataArray = data;
                _version = version;
            }

            /// <summary>
            /// Update the associated class and increases the storage for the data array if needed.
            /// </summary>
            internal ExpandoData UpdateClass(ExpandoClass newClass)
            {
                if (_dataArray.Length >= newClass.Keys.Length)
                {
                    // we have extra space in our buffer, just initialize it to Uninitialized.
                    this[newClass.Keys.Length - 1] = ExpandoObject.Uninitialized;
                    return new ExpandoData(newClass, _dataArray, _version);
                }
                else
                {
                    // we've grown too much - we need a new object array
                    int oldLength = _dataArray.Length;
                    object[] arr = new object[GetAlignedSize(newClass.Keys.Length)];
                    Array.Copy(_dataArray, arr, _dataArray.Length);
                    ExpandoData newData = new ExpandoData(newClass, arr, _version);
                    newData[oldLength] = ExpandoObject.Uninitialized;
                    return newData;
                }
            }

            private static int GetAlignedSize(int len)
            {
                // the alignment of the array for storage of values (must be a power of two)
                const int DataArrayAlignment = 8;

                // round up and then mask off lower bits
                return (len + (DataArrayAlignment - 1)) & (~(DataArrayAlignment - 1));
            }
        }

        #endregion

        #region INotifyPropertyChanged

        event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
        {
            add { _propertyChanged += value; }
            remove { _propertyChanged -= value; }
        }

        #endregion
    }
}

