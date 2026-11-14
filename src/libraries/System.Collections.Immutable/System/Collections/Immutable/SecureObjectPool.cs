// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace System.Collections.Immutable
{
    internal static class SecureObjectPool
    {
        private static int s_poolUserIdCounter;

        internal const int UnassignedId = -1;

        internal static int NewId()
        {
            int result;
            do
            {
                result = Interlocked.Increment(ref s_poolUserIdCounter);
            }
            while (result == UnassignedId);

            return result;
        }
    }

    internal static class SecureObjectPool<T, TCaller>
        where TCaller : ISecurePooledObjectUser
    {
        public static void TryAdd(TCaller caller, SecurePooledObject<T> item)
        {
            if (caller.PoolUserId == item.Owner)
            {
                item.Owner = SecureObjectPool.UnassignedId;
                AllocFreeConcurrentStack<SecurePooledObject<T>>.TryAdd(item);
            }
        }

        public static bool TryTake(TCaller caller, out SecurePooledObject<T>? item)
        {
            if (caller.PoolUserId != SecureObjectPool.UnassignedId && AllocFreeConcurrentStack<SecurePooledObject<T>>.TryTake(out item))
            {
                item.Owner = caller.PoolUserId;
                return true;
            }
            else
            {
                item = null;
                return false;
            }
        }

        public static SecurePooledObject<T> PrepNew(TCaller caller, T newValue)
        {
            Requires.NotNullAllowStructs(newValue, nameof(newValue));
            var pooledObject = new SecurePooledObject<T>(newValue);
            pooledObject.Owner = caller.PoolUserId;
            return pooledObject;
        }
    }

    internal interface ISecurePooledObjectUser
    {
        int PoolUserId { get; }
    }

    internal sealed class SecurePooledObject<T>
    {
        private readonly T _value;
        private int _owner;

        internal SecurePooledObject(T newValue)
        {
            Requires.NotNullAllowStructs(newValue, nameof(newValue));
            _value = newValue;
        }

        internal int Owner
        {
            get { return _owner; }
            set { _owner = value; }
        }

        internal T Use<TCaller>(ref TCaller caller)
            where TCaller : struct, ISecurePooledObjectUser
        {
            if (!IsOwned(ref caller))
                Requires.FailObjectDisposed(caller);
            return _value;
        }

        internal bool TryUse<TCaller>(ref TCaller caller, [MaybeNullWhen(false)] out T value)
            where TCaller : struct, ISecurePooledObjectUser
        {
            if (IsOwned(ref caller))
            {
                value = _value;
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsOwned<TCaller>(ref TCaller caller)
            where TCaller : struct, ISecurePooledObjectUser
        {
            return caller.PoolUserId == _owner;
        }
    }
}
