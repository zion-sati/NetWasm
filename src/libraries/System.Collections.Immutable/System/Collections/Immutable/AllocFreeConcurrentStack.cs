// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace System.Collections.Immutable
{
    internal static class AllocFreeConcurrentStack<T>
    {
        private const int MaxSize = 35;

#if NET
        [ThreadStatic]
        private static Stack<RefAsValueType<T>>? t_stack;
#else
        private static readonly Type s_typeOfT = typeof(T);
#endif

        public static void TryAdd(T item)
        {
            Stack<RefAsValueType<T>> localStack =
#if NET
                t_stack ??= new Stack<RefAsValueType<T>>(MaxSize);
#else
                ThreadLocalStack;
#endif

            if (localStack.Count < MaxSize)
            {
                localStack.Push(new RefAsValueType<T>(item));
            }
        }

        public static bool TryTake([MaybeNullWhen(false)] out T item)
        {
#if NET
            Stack<RefAsValueType<T>>? localStack = t_stack;
#else
            Stack<RefAsValueType<T>> localStack = ThreadLocalStack;
#endif
            if (localStack != null && localStack.Count > 0)
            {
                item = localStack.Pop().Value;
                return true;
            }

            item = default;
            return false;
        }

#if !NET
        private static Stack<RefAsValueType<T>> ThreadLocalStack
        {
            get
            {
                Dictionary<Type, object>? typesToStacks = AllocFreeConcurrentStack.t_stacks ??= new Dictionary<Type, object>();
                if (!typesToStacks.TryGetValue(s_typeOfT, out object? stackObj))
                {
                    stackObj = new Stack<RefAsValueType<T>>(MaxSize);
                    typesToStacks.Add(s_typeOfT, stackObj);
                }

                return (Stack<RefAsValueType<T>>)stackObj;
            }
        }
#endif
    }

#if !NET
    internal static class AllocFreeConcurrentStack
    {
        [ThreadStatic]
        internal static Dictionary<Type, object>? t_stacks;
    }
#endif
}
