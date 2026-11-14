// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: the subset the ported expression types use; switch (type.GetTypeCode()) reads
// Type.GetTypeCode(type), since upstream's TypeExtensions are not ported. IsNullableType reads the
// compiler's Nullable fact, because NetWasm cannot discover a constructed type's generic definition.
// LiftPrimitiveOrThrow keeps only upstream's no-dynamic-code path.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace System.Dynamic.Utils
{
    internal static class TypeUtils
    {
        public static Type GetNonNullableType(this Type type) => IsNullableType(type) ? type.GetGenericArguments()[0] : type;

        public static bool IsNullableType(this Type type) => type.IsNullable;

        public static bool IsInteger(this Type type)
        {
            type = GetNonNullableType(type);
            if (!type.IsEnum)
            {
                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.Byte:
                    case TypeCode.SByte:
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                    case TypeCode.Int64:
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                    case TypeCode.UInt64:
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// This is an alternative to <see cref="GetNullableType" /> that will throw
        /// if dynamic code is required. Some common primitive types are special-cased.
        /// </summary>
        public static Type LiftPrimitiveOrThrow(this Type type)
        {
            // NetWasm: dynamic code is never supported, so only upstream's special-cased path remains.
            if (!type.IsValueType || IsNullableType(type))
            {
                return type;
            }
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                    return typeof(bool?);
                case TypeCode.Int32:
                    return typeof(int?);
                case TypeCode.Int64:
                    return typeof(long?);
                case TypeCode.Single:
                    return typeof(float?);
                case TypeCode.Double:
                    return typeof(double?);
                case TypeCode.UInt32:
                    return typeof(uint?);
                case TypeCode.UInt64:
                    return typeof(ulong?);
                case TypeCode.Byte:
                    return typeof(byte?);
                case TypeCode.SByte:
                    return typeof(sbyte?);
                case TypeCode.Int16:
                    return typeof(short?);
                case TypeCode.UInt16:
                    return typeof(ushort?);
                case TypeCode.Char:
                    return typeof(char?);
                case TypeCode.Decimal:
                    return typeof(decimal?);
                case TypeCode.DateTime:
                    return typeof(DateTime?);
                default:
                    if (type == typeof(DateTimeOffset))
                    {
                        return typeof(DateTimeOffset?);
                    }
                    else if (type == typeof(TimeSpan))
                    {
                        return typeof(TimeSpan?);
                    }
                    // Upstream's MakeGenericType: the type table holds Nullable<T> for each T the program names
                    // nullable, as a lifted operator's tree does.
                    return Type.FindConstructedType(typeof(Nullable<>).SemanticTypeId, [type.SemanticTypeId]) ??
                        throw new NotSupportedException(
                            "Lifting needs the nullable form of a value type the program does not use, and NetWasm " +
                            "cannot create types at run time.");
            }
        }

        public static bool IsBool(this Type type) => GetNonNullableType(type) == typeof(bool);

        public static bool IsNumeric(this Type type)
        {
            type = GetNonNullableType(type);
            if (!type.IsEnum)
            {
                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.Char:
                    case TypeCode.SByte:
                    case TypeCode.Byte:
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                    case TypeCode.Int64:
                    case TypeCode.Double:
                    case TypeCode.Single:
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                    case TypeCode.UInt64:
                        return true;
                }
            }

            return false;
        }

        public static bool IsArithmetic(this Type type)
        {
            type = GetNonNullableType(type);
            if (!type.IsEnum)
            {
                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                    case TypeCode.Int64:
                    case TypeCode.Double:
                    case TypeCode.Single:
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                    case TypeCode.UInt64:
                        return true;
                }
            }

            return false;
        }

        public static bool IsUnsignedInt(this Type type)
        {
            type = GetNonNullableType(type);
            if (!type.IsEnum)
            {
                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                    case TypeCode.UInt64:
                        return true;
                }
            }

            return false;
        }

        public static bool IsIntegerOrBool(this Type type)
        {
            type = GetNonNullableType(type);
            if (!type.IsEnum)
            {
                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.Int64:
                    case TypeCode.Int32:
                    case TypeCode.Int16:
                    case TypeCode.UInt64:
                    case TypeCode.UInt32:
                    case TypeCode.UInt16:
                    case TypeCode.Boolean:
                    case TypeCode.SByte:
                    case TypeCode.Byte:
                        return true;
                }
            }

            return false;
        }

        public static bool HasReferenceEquality(Type left, Type right)
        {
            if (left.IsValueType || right.IsValueType)
            {
                return false;
            }

            // If we have an interface and a reference type then we can do
            // reference equality.

            // If we have two reference types and one is assignable to the
            // other then we can do reference equality.

            return left.IsInterface || right.IsInterface || AreReferenceAssignable(left, right)
                   || AreReferenceAssignable(right, left);
        }

        public static bool HasBuiltInEqualityOperator(Type left, Type right)
        {
            // If we have an interface and a reference type then we can do
            // reference equality.
            if (left.IsInterface && !right.IsValueType)
            {
                return true;
            }

            if (right.IsInterface && !left.IsValueType)
            {
                return true;
            }

            // If we have two reference types and one is assignable to the
            // other then we can do reference equality.
            if (!left.IsValueType && !right.IsValueType)
            {
                if (AreReferenceAssignable(left, right) || AreReferenceAssignable(right, left))
                {
                    return true;
                }
            }

            // Otherwise, if the types are not the same then we definitely
            // do not have a built-in equality operator.
            if (!AreEquivalent(left, right))
            {
                return false;
            }

            // We have two identical value types, modulo nullability.  (If they were both the
            // same reference type then we would have returned true earlier.)
            Debug.Assert(left.IsValueType);

            // Equality between struct types is only defined for numerics, bools, enums,
            // and their nullable equivalents.
            Type nnType = GetNonNullableType(left);
            return nnType == typeof(bool) || IsNumeric(nnType) || nnType.IsEnum;
        }

        public static bool IsImplicitlyConvertibleTo(this Type source, Type destination) =>
            AreEquivalent(source, destination) // identity conversion
            || IsImplicitNumericConversion(source, destination)
            || IsImplicitReferenceConversion(source, destination)
            || IsImplicitBoxingConversion(source, destination)
            || IsImplicitNullableConversion(source, destination);

        private static bool IsImplicitNumericConversion(Type source, Type destination)
        {
            TypeCode tcSource = Type.GetTypeCode(source);
            TypeCode tcDest = Type.GetTypeCode(destination);

            switch (tcSource)
            {
                case TypeCode.SByte:
                    switch (tcDest)
                    {
                        case TypeCode.Int16:
                        case TypeCode.Int32:
                        case TypeCode.Int64:
                        case TypeCode.Single:
                        case TypeCode.Double:
                        case TypeCode.Decimal:
                            return true;
                    }

                    break;
                case TypeCode.Byte:
                    switch (tcDest)
                    {
                        case TypeCode.Int16:
                        case TypeCode.UInt16:
                        case TypeCode.Int32:
                        case TypeCode.UInt32:
                        case TypeCode.Int64:
                        case TypeCode.UInt64:
                        case TypeCode.Single:
                        case TypeCode.Double:
                        case TypeCode.Decimal:
                            return true;
                    }

                    break;
                case TypeCode.Int16:
                    switch (tcDest)
                    {
                        case TypeCode.Int32:
                        case TypeCode.Int64:
                        case TypeCode.Single:
                        case TypeCode.Double:
                        case TypeCode.Decimal:
                            return true;
                    }

                    break;
                case TypeCode.UInt16:
                    switch (tcDest)
                    {
                        case TypeCode.Int32:
                        case TypeCode.UInt32:
                        case TypeCode.Int64:
                        case TypeCode.UInt64:
                        case TypeCode.Single:
                        case TypeCode.Double:
                        case TypeCode.Decimal:
                            return true;
                    }

                    break;
                case TypeCode.Int32:
                    switch (tcDest)
                    {
                        case TypeCode.Int64:
                        case TypeCode.Single:
                        case TypeCode.Double:
                        case TypeCode.Decimal:
                            return true;
                    }

                    break;
                case TypeCode.UInt32:
                    switch (tcDest)
                    {
                        case TypeCode.Int64:
                        case TypeCode.UInt64:
                        case TypeCode.Single:
                        case TypeCode.Double:
                        case TypeCode.Decimal:
                            return true;
                    }

                    break;
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    switch (tcDest)
                    {
                        case TypeCode.Single:
                        case TypeCode.Double:
                        case TypeCode.Decimal:
                            return true;
                    }

                    break;
                case TypeCode.Char:
                    switch (tcDest)
                    {
                        case TypeCode.UInt16:
                        case TypeCode.Int32:
                        case TypeCode.UInt32:
                        case TypeCode.Int64:
                        case TypeCode.UInt64:
                        case TypeCode.Single:
                        case TypeCode.Double:
                        case TypeCode.Decimal:
                            return true;
                    }

                    break;
                case TypeCode.Single:
                    return tcDest == TypeCode.Double;
            }

            return false;
        }

        private static bool IsImplicitReferenceConversion(Type source, Type destination) =>
            destination.IsAssignableFrom(source);

        private static bool IsImplicitBoxingConversion(Type source, Type destination) =>
            source.IsValueType && (destination == typeof(object) || destination == typeof(ValueType)) || source.IsEnum && destination == typeof(Enum);

        private static bool IsImplicitNullableConversion(Type source, Type destination) =>
            IsNullableType(destination) && IsImplicitlyConvertibleTo(GetNonNullableType(source), GetNonNullableType(destination));

        // Checks if the type is a valid target for an instance call
        public static bool IsValidInstanceType(MemberInfo member, Type instanceType)
        {
            Type? targetType = member.DeclaringType;
            if (targetType == null)
            {
                return false;
            }

            if (AreReferenceAssignable(targetType, instanceType))
            {
                return true;
            }

            if (instanceType.IsValueType)
            {
                if (AreReferenceAssignable(targetType, typeof(object)))
                {
                    return true;
                }

                if (AreReferenceAssignable(targetType, typeof(ValueType)))
                {
                    return true;
                }

                if (instanceType.IsEnum && AreReferenceAssignable(targetType, typeof(Enum)))
                {
                    return true;
                }

                // A call to an interface implemented by a struct is legal whether the struct has
                // been boxed or not.
                if (targetType.IsInterface)
                {
                    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2070:UnrecognizedReflectionPattern",
                        Justification = "The targetType must be preserved (since we have an instance of it here)," +
                            "So if it's an interface that interface will be preserved everywhere" +
                            "So if it was implemented by the instanceType, it will be kept even after trimming." +
                            "The fact that GetInterfaces may return fewer interfaces doesn't matter as long" +
                            "as it returns the one we're looking for.")]
                    static Type[] GetTypeInterfaces(Type instanceType) => instanceType.GetInterfaces();
                    foreach (Type interfaceType in GetTypeInterfaces(instanceType))
                    {
                        if (AreReferenceAssignable(targetType, interfaceType))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public static Type GetNonRefType(this Type type) => type.IsByRef ? type.GetElementType()! : type;

        public static bool AreEquivalent(Type? t1, Type? t2) => t1 != null && t1.IsEquivalentTo(t2);

        public static bool AreReferenceAssignable(Type dest, Type src)
        {
            // This actually implements "Is this identity assignable and/or reference assignable?"
            if (AreEquivalent(dest, src))
            {
                return true;
            }

            return !dest.IsValueType && !src.IsValueType && dest.IsAssignableFrom(src);
        }

        public static bool IsSameOrSubclass(Type type, Type subType) =>
            AreEquivalent(type, subType) || subType.IsSubclassOf(type);

        public static void ValidateType(Type type, string? paramName) => ValidateType(type, paramName, false, false);

        public static void ValidateType(Type type, string? paramName, bool allowByRef, bool allowPointer)
        {
            if (ValidateType(type, paramName, -1))
            {
                if (!allowByRef && type.IsByRef)
                {
                    throw Error.TypeMustNotBeByRef(paramName);
                }

                if (!allowPointer && type.IsPointer)
                {
                    throw Error.TypeMustNotBePointer(paramName);
                }
            }
        }

        public static bool ValidateType(Type type, string? paramName, int index)
        {
            if (type == typeof(void))
            {
                return false; // Caller can skip further checks.
            }

            if (type.ContainsGenericParameters)
            {
                throw type.IsGenericTypeDefinition
                    ? Error.TypeIsGeneric(type, paramName, index)
                    : Error.TypeContainsGenericParameters(type, paramName, index);
            }

            return true;
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2070:UnrecognizedReflectionPattern",
            Justification = "The Array 'Get' method is dynamically constructed and is not included in IL. It is not subject to trimming.")]
        public static MethodInfo GetArrayGetMethod(Type arrayType)
        {
            Debug.Assert(arrayType.IsArray);
            return arrayType.GetMethod("Get", BindingFlags.Public | BindingFlags.Instance)!;
        }
    }
}
