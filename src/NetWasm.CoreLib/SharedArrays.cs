// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Reflection;

namespace System
{
    // The element type of the shared arrays (SharedArrayElement[], [*], [,] and [,,]). Array.CreateInstance makes an
    // array of a reference element the program has no array type for as the shared array of its shape, with the
    // element's type in the array's header, where any array of references keeps it, so stores, casts and copies check
    // that element as they check any array's. As Native AOT shares one canonical form across arrays of references, a
    // program pays once for the shape, not once for every element type it could pass. No object of this type exists.
    internal sealed class SharedArrayElement
    {
        private SharedArrayElement() { }
    }

    // The Type of a shared array, or a type MakeArrayType makes for an element the program has no array type of: the
    // array of its element and the shared array's shape. Its shape facts (array, rank, base type) are the shared
    // array's; what depends on the element (the element itself, the names, the generic interfaces, assignability) is
    // its element's. One object per element and shape, so types compare by reference as every Type does.
    internal sealed class SharedArrayType : RuntimeType
    {
        private readonly Type _element;

        internal SharedArrayType(int sharedTypeId, Type element) : base(sharedTypeId) => _element = element;

        public override Type GetElementType() => _element;

        public override string Name => _element.Name + Suffix;

        internal string? SharedNamespace => _element.Namespace;

        internal string? SharedFullName => _element.FullName is { } name ? name + Suffix : null;

        public override string ToString() => _element.ToString() + Suffix;

        public override Assembly Assembly => _element.Assembly;

        public override int GetHashCode() => _element.GetHashCode() * 31 + SemanticTypeId;

        // The shared array's interfaces, each generic one (IList<T> and the rest) over the element instead, where the
        // program has that instantiation.
        public override Type[] GetInterfaces()
        {
            var shared = base.GetInterfaces();
            var interfaces = new List<Type>(shared.Length);
            foreach (var type in shared)
            {
                if (!type.IsGenericType)
                {
                    interfaces.Add(type);
                }
                else if (FindConstructedType(type.GetGenericTypeDefinition().SemanticTypeId,
                    [_element.SemanticTypeId]) is { } constructed)
                {
                    interfaces.Add(constructed);
                }
            }
            return interfaces.ToArray();
        }

        private string Suffix => IsSZArray ? "[]" : GetArrayRank() switch
        {
            1 => "[*]",
            var rank => "[" + new string(',', rank - 1) + "]",
        };
    }

    internal static class SharedArrays
    {
        // The array types answered so far, by shape (the vector, then ranks one to three) and element type ID: an
        // array per shape the program asks of, which a program that never asks allocates none of.
        private static Type?[]?[]? s_types;

        // Whether an array of the element is a shared array when the program has no array type of it: a closed
        // reference type that is not a pointer, with a type ID of its own (a shared array type's element is not one,
        // as its ID is the shared type's).
        internal static bool Holds(Type element) =>
            !element.IsValueType && !element.IsPointer && !element.IsByRef && !element.IsFunctionPointer &&
            !element.ContainsGenericParameters && element is not SharedArrayType;

        // GetType of an object of a shared array type (the compiled GetType calls this for those types only).
        internal static Type TypeOf(Array array, int sharedTypeId) => Of(Array.ElementTypeOf(array), sharedTypeId);

        // The array type of the element in the shared array type's shape: the program's own when it has one, so it is
        // the object typeof names, and otherwise the shared type's for the element.
        internal static Type Of(Type element, int sharedTypeId)
        {
            var flags = RuntimeTypeTable.Flags(sharedTypeId);
            var vector = (flags & RuntimeTypeFactsFlags.SzArray) != 0;
            var rank = flags >> RuntimeTypeFactsFlags.ArrayRankShift & RuntimeTypeFactsFlags.ArrayRankMask;
            var types = (s_types ??= new Type?[4][])[vector ? 0 : rank] ??= new Type?[RuntimeTypeTable.Capacity()];
            ref var type = ref types[element.SemanticTypeId];
            if (type is null)
            {
                var id = vector
                    ? Find(RuntimeTypeFactsFlags.SzArray, 0, element.SemanticTypeId)
                    : Find(RuntimeTypeFactsFlags.Array, rank, element.SemanticTypeId, RuntimeTypeFactsFlags.SzArray);
                type = id != 0 ? Type.GetTypeFromSemanticId(id) : new SharedArrayType(sharedTypeId, element);
            }
            return type;
        }

        // MakeArrayType's type for an element the program has no array type of, in the shape of a shared array type
        // the program has; null without one.
        internal static Type? Made(Type element, int kind, int rank, int excluded)
        {
            if (element.IsByRef || element.ContainsGenericParameters || element is SharedArrayType)
            {
                return null;
            }
            var shared = Find(kind, rank, typeof(SharedArrayElement).SemanticTypeId, excluded);
            return shared == 0 ? null : Of(element, shared);
        }

        // Assignability where either type is a shared array's: by shape and element, as for any array type.
        internal static bool IsAssignable(Type target, Type candidate)
        {
            if (target == candidate)
            {
                return true;
            }
            if (candidate is SharedArrayType)
            {
                var element = candidate.GetElementType()!;
                if (target.IsArray)
                {
                    return SameShape(target, candidate) && Covariant(target.GetElementType()!, element);
                }
                if (candidate.IsSZArray && target.IsGenericType && IsArrayInterface(target.GetGenericTypeDefinition()))
                {
                    return target.GenericTypeArguments[0].IsAssignableFrom(element);
                }
                // The rest (object, Array, the non-generic interfaces) hold for every array of the shape.
                return Type.InternalIsTypeAssignable(candidate.SemanticTypeId, target.SemanticTypeId) != 0;
            }
            return candidate.IsArray && SameShape(target, candidate) &&
                Covariant(target.GetElementType()!, candidate.GetElementType()!);
        }

        private static bool SameShape(Type target, Type candidate) =>
            target.IsSZArray == candidate.IsSZArray && target.GetArrayRank() == candidate.GetArrayRank();

        // An array of the candidate element converts to one of the target element: the same element, or a reference
        // element assignable to the target's.
        private static bool Covariant(Type target, Type candidate) =>
            target == candidate || !candidate.IsValueType && !target.IsValueType && target.IsAssignableFrom(candidate);

        private static bool IsArrayInterface(Type definition) =>
            definition == typeof(IEnumerable<>) || definition == typeof(ICollection<>) || definition == typeof(IList<>) ||
            definition == typeof(IReadOnlyCollection<>) || definition == typeof(IReadOnlyList<>);

        // The program's type of the given kind (and rank, for an array) whose element is the given type, or 0.
        private static int Find(int kind, int rank, int element, int excluded = 0)
        {
            for (var id = 1; id < RuntimeTypeTable.Capacity(); id++)
            {
                var flags = RuntimeTypeTable.Flags(id);
                if ((flags & (kind | excluded)) == kind &&
                    (rank == 0 || (flags >> RuntimeTypeFactsFlags.ArrayRankShift & RuntimeTypeFactsFlags.ArrayRankMask) == rank) &&
                    RuntimeTypeTable.ElementType(id) == element)
                {
                    return id;
                }
            }
            return 0;
        }
    }
}
