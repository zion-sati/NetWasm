// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: MakeDelegateType has no delegate-type cache (its TypeInfo tree exists to reuse
// emitted custom delegates), and a custom delegate type (by-ref, pointer or by-ref-like types, or more
// than MaximumArity) throws PNSE instead of being emitted. Func and Action instantiation goes through
// Type.MakeGenericType, which throws PNSE on NetWasm; only the parameterless Action resolves.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic.Utils;

namespace System.Linq.Expressions.Compiler
{
    internal static partial class DelegateHelpers
    {
        private const int MaximumArity = 17;

        [RequiresDynamicCode(Expression.DelegateCreationRequiresDynamicCode)]
        internal static Type MakeDelegateType(Type[] types)
        {
            return MakeNewDelegate(types);
        }

        [RequiresDynamicCode(Expression.DelegateCreationRequiresDynamicCode)]
        internal static Type MakeNewDelegate(Type[] types)
        {
            Debug.Assert(types != null && types.Length > 0);

            // Can only used predefined delegates if we have no byref types and
            // the arity is small enough to fit in Func<...> or Action<...>
            bool needCustom;

            if (types.Length > MaximumArity)
            {
                needCustom = true;
            }
            else
            {
                needCustom = false;

                for (int i = 0; i < types.Length; i++)
                {
                    Type type = types[i];
                    if (type.IsByRef || type.IsByRefLike || type.IsPointer)
                    {
                        needCustom = true;
                        break;
                    }
                }
            }

            if (needCustom)
            {
                // NetWasm: a custom delegate type needs Reflection.Emit.
                throw new PlatformNotSupportedException(SR.PlatformNotSupported_CustomDelegateType);
            }

            Type? result;
            if (types[types.Length - 1] == typeof(void))
            {
                result = GetActionType(types.RemoveLast());
            }
            else
            {
                result = GetFuncType(types);
            }

            Debug.Assert(result != null);
            return result!;
        }

        [RequiresDynamicCode(Expression.DelegateCreationRequiresDynamicCode)]
        internal static Type? GetFuncType(Type[] types)
        {
            switch (types.Length)
            {
                case 1:
                    return typeof(Func<>).MakeGenericType(types);
                case 2:
                    return typeof(Func<,>).MakeGenericType(types);
                case 3:
                    return typeof(Func<,,>).MakeGenericType(types);
                case 4:
                    return typeof(Func<,,,>).MakeGenericType(types);
                case 5:
                    return typeof(Func<,,,,>).MakeGenericType(types);
                case 6:
                    return typeof(Func<,,,,,>).MakeGenericType(types);
                case 7:
                    return typeof(Func<,,,,,,>).MakeGenericType(types);
                case 8:
                    return typeof(Func<,,,,,,,>).MakeGenericType(types);
                case 9:
                    return typeof(Func<,,,,,,,,>).MakeGenericType(types);
                case 10:
                    return typeof(Func<,,,,,,,,,>).MakeGenericType(types);
                case 11:
                    return typeof(Func<,,,,,,,,,,>).MakeGenericType(types);
                case 12:
                    return typeof(Func<,,,,,,,,,,,>).MakeGenericType(types);
                case 13:
                    return typeof(Func<,,,,,,,,,,,,>).MakeGenericType(types);
                case 14:
                    return typeof(Func<,,,,,,,,,,,,,>).MakeGenericType(types);
                case 15:
                    return typeof(Func<,,,,,,,,,,,,,,>).MakeGenericType(types);
                case 16:
                    return typeof(Func<,,,,,,,,,,,,,,,>).MakeGenericType(types);
                case 17:
                    return typeof(Func<,,,,,,,,,,,,,,,,>).MakeGenericType(types);

                default:
                    return null;
            }
        }

        [RequiresDynamicCode(Expression.DelegateCreationRequiresDynamicCode)]
        internal static Type? GetActionType(Type[] types)
        {
            switch (types.Length)
            {
                case 0:
                    return typeof(Action);

                case 1:
                    return typeof(Action<>).MakeGenericType(types);
                case 2:
                    return typeof(Action<,>).MakeGenericType(types);
                case 3:
                    return typeof(Action<,,>).MakeGenericType(types);
                case 4:
                    return typeof(Action<,,,>).MakeGenericType(types);
                case 5:
                    return typeof(Action<,,,,>).MakeGenericType(types);
                case 6:
                    return typeof(Action<,,,,,>).MakeGenericType(types);
                case 7:
                    return typeof(Action<,,,,,,>).MakeGenericType(types);
                case 8:
                    return typeof(Action<,,,,,,,>).MakeGenericType(types);
                case 9:
                    return typeof(Action<,,,,,,,,>).MakeGenericType(types);
                case 10:
                    return typeof(Action<,,,,,,,,,>).MakeGenericType(types);
                case 11:
                    return typeof(Action<,,,,,,,,,,>).MakeGenericType(types);
                case 12:
                    return typeof(Action<,,,,,,,,,,,>).MakeGenericType(types);
                case 13:
                    return typeof(Action<,,,,,,,,,,,,>).MakeGenericType(types);
                case 14:
                    return typeof(Action<,,,,,,,,,,,,,>).MakeGenericType(types);
                case 15:
                    return typeof(Action<,,,,,,,,,,,,,,>).MakeGenericType(types);
                case 16:
                    return typeof(Action<,,,,,,,,,,,,,,,>).MakeGenericType(types);

                default:
                    return null;
            }
        }
    }
}
