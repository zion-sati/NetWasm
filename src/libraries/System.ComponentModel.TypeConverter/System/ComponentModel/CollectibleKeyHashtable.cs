// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
//
// NetWasm deviation: MemberInfo.IsCollectible is unavailable and NetWasm loads no collectible
// assemblies, so IsCollectible is a local helper that answers false.

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.ComponentModel
{
    /// <summary>
    /// Hashtable that maps a <see cref="MemberInfo"/> object key to an associated value.
    /// <para>
    /// For keys where <see cref="MemberInfo.IsCollectible"/> is <c>false</c>, a standard <see cref="Hashtable"/> is used.
    /// For keys where <see cref="MemberInfo.IsCollectible"/> is <c>true</c>, a <see cref="ConditionalWeakTable{TKey, TValue}"/> is used.
    /// This ensures that collectible <see cref="MemberInfo"/> instances (such as those from collectible assemblies) do not prevent their assemblies from being unloaded.
    /// </para>
    /// </summary>
    internal sealed class CollectibleKeyHashtable
    {
        private readonly Hashtable _defaultTable = new Hashtable();
        private readonly ConditionalWeakTable<object, object?> _collectibleTable = new ConditionalWeakTable<object, object?>();

        public object? this[MemberInfo key]
        {
            get
            {
                return !IsCollectible(key) ? _defaultTable[key] : (_collectibleTable.TryGetValue(key, out object? value) ? value : null);
            }

            set
            {
                if (!IsCollectible(key))
                {
                    _defaultTable[key] = value;
                }
                else
                {
                    _collectibleTable.AddOrUpdate(key, value);
                }
            }
        }

        // NetWasm loads no collectible assemblies, so every key lives in the strong table.
        private static bool IsCollectible(MemberInfo key) => false;
    }
}
