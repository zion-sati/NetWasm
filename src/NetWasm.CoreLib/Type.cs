using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System
{
    // NetWasm: every Type object is a RuntimeType, as on .NET, so a type is its own TypeInfo.
    internal sealed class RuntimeType : TypeInfo
    {
        private RuntimeType() { }
    }

    public abstract partial class Type : MemberInfo
    {
        public static readonly Type[] EmptyTypes = Array.Empty<Type>();

        internal int SemanticTypeId = 0;

        // Canonical runtime Type objects are created directly by get_type_object.
        // User-defined metadata providers need a separate supported construction model.
        protected Type()
        {
            throw new PlatformNotSupportedException("Constructing custom Type metadata providers is not supported on NetWasm.");
        }

        // The compiler lowers every call, this one included, to the handle's type; the body is
        // what a delegate or function pointer to the method runs.
        public static Type? GetTypeFromHandle(RuntimeTypeHandle handle) => GetTypeFromHandle(handle);

        // RuntimeTypeHandle uses the compiler's 32-bit semantic type identity on
        // both Wasm targets, matching ldtoken and GetTypeFromHandle.
        public RuntimeTypeHandle TypeHandle => Unsafe.As<int, RuntimeTypeHandle>(ref SemanticTypeId);


        public static RuntimeTypeHandle GetTypeHandle(object o)
        {
            ArgumentNullException.ThrowIfNull(o);
            return o.GetType().TypeHandle;
        }

        // Lowered like GetTypeFromHandle.
        internal static Type GetTypeFromSemanticId(int semanticTypeId) => GetTypeFromSemanticId(semanticTypeId);
        public bool IsValueType => IsValueTypeImpl();
        public bool IsEnum => HasFlag(RuntimeTypeFactsFlags.Enum);
        public bool IsInterface => HasFlag(RuntimeTypeFactsFlags.Interface);
        public bool IsByRef => IsByRefImpl();
        public bool IsPointer => IsPointerImpl();
        public bool ContainsGenericParameters =>
            HasFlag(RuntimeTypeFactsFlags.ContainsGenericParameters);
        public bool IsGenericTypeDefinition =>
            HasFlag(RuntimeTypeFactsFlags.GenericTypeDefinition);
        public bool IsGenericType => HasFlag(RuntimeTypeFactsFlags.GenericType);
        public bool IsArray => IsArrayImpl();
        public bool IsSZArray => HasFlag(RuntimeTypeFactsFlags.SzArray);
        public bool IsClass => HasFlag(RuntimeTypeFactsFlags.Class);
        public bool IsSealed => HasFlag(RuntimeTypeFactsFlags.Sealed);
        public bool IsAbstract => HasFlag(RuntimeTypeFactsFlags.Abstract);

        public Type[] GetGenericArguments()
        {
            int[]? typeIds = RuntimeTypeTable.GenericArguments(SemanticTypeId);
            if (typeIds is null)
            {
                return [];
            }

            var arguments = new Type[typeIds.Length];
            for (int index = 0; index < arguments.Length; index++)
            {
                arguments[index] = GetTypeFromSemanticId(typeIds[index]);
            }
            return arguments;
        }

        internal bool IsNullable => HasFlag(RuntimeTypeFactsFlags.Nullable);

        public Type? BaseType
        {
            get
            {
                int typeId = InternalGetBaseTypeId(SemanticTypeId);
                return typeId == 0 ? null : GetTypeFromSemanticId(typeId);
            }
        }

        public static TypeCode GetTypeCode(Type? type) =>
            type is null ? TypeCode.Empty : type.GetTypeCodeImpl();

        public bool IsAssignableTo([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] Type? targetType) =>
            targetType?.IsAssignableFrom(this) ?? false;

        public bool IsAssignableFrom(Type? candidate)
        {
            if (candidate is null)
            {
                return false;
            }
            if (this == typeof(object) && candidate.IsInterface)
            {
                return true;
            }
            // The runtime walks the candidate's base chain and its casting lists, as a cast does.
            return InternalIsTypeAssignable(candidate.SemanticTypeId, SemanticTypeId) != 0;
        }

        public bool IsSubclassOf(Type? type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (this == type)
            {
                return false;
            }
            for (Type? current = BaseType; current is not null; current = current.BaseType)
            {
                if (current == type)
                {
                    return true;
                }
            }
            return type == typeof(object);
        }

        public bool IsEquivalentTo(Type? other) => this == other;

        public bool IsInstanceOfType(object? value) =>
            value is not null && IsAssignableFrom(value.GetType());

        public override object[] GetCustomAttributes(Type attributeType, bool inherit) =>
            Runtime.CompilerServices.RuntimeCustomAttributes.GetMany(
                this,
                attributeType,
                inherit);

        public override bool IsDefined(Type attributeType, bool inherit) =>
            Runtime.CompilerServices.RuntimeCustomAttributes.IsDefined(
                this,
                attributeType,
                inherit);

        public static bool operator ==(Type? left, Type? right) =>
            object.ReferenceEquals(left, right);
        public static bool operator !=(Type? left, Type? right) =>
            !object.ReferenceEquals(left, right);
        public override bool Equals(object? value) =>
            value is Type other && this == other;
        public override int GetHashCode() => SemanticTypeId;

        private bool HasFlag(int flag) => (RuntimeTypeTable.Flags(SemanticTypeId) & flag) != 0;

        // The runtime type table's address.
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern nint InternalGetFacts();

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern int InternalGetBaseTypeId(int semanticTypeId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern int InternalIsTypeAssignable(int candidateTypeId, int targetTypeId);
    }

    internal static class RuntimeTypeFactsFlags
    {
        internal const int ValueType = 1 << 0;
        internal const int Enum = 1 << 1;
        internal const int Interface = 1 << 2;
        internal const int ByReference = 1 << 3;
        internal const int Pointer = 1 << 4;
        internal const int ContainsGenericParameters = 1 << 5;
        internal const int GenericTypeDefinition = 1 << 6;
        internal const int GenericType = 1 << 7;
        internal const int Array = 1 << 8;
        internal const int SzArray = 1 << 9;
        internal const int Class = 1 << 10;
        internal const int Sealed = 1 << 11;
        internal const int Nullable = 1 << 12;
        internal const int Abstract = 1 << 13;
        internal const int GenericTypeParameter = 1 << 14;
        internal const int GenericMethodParameter = 1 << 15;
        internal const int ByRefLike = 1 << 16;
        internal const int TypeDefinition = 1 << 23;
        internal const int ArrayRankShift = 17;
        internal const int ArrayRankMask = 0x3f;
    }


    public readonly partial struct RuntimeTypeHandle : IEquatable<RuntimeTypeHandle>
    {
        // Handles are opaque semantic IDs, not dereferenceable host addresses: Type.TypeHandle
        // reinterprets the type's ID as its handle. The ID is the struct's one field, so a copy of
        // the handle keeps all of it, as the compiler's 4-byte view of the type assumes.
#pragma warning disable CS0649 // Written only through Type.TypeHandle's reinterpretation.
        private readonly int _semanticId;
#pragma warning restore CS0649

        private int SemanticId => _semanticId;

        public nint Value => SemanticId;
        public override int GetHashCode() => SemanticId;
        public override bool Equals(object? obj) => obj is RuntimeTypeHandle handle && Equals(handle);
        public bool Equals(RuntimeTypeHandle handle) => SemanticId == handle.SemanticId;
        public static bool operator ==(RuntimeTypeHandle left, object? right) => left.Equals(right);
        public static bool operator ==(object? left, RuntimeTypeHandle right) => right.Equals(left);
        public static bool operator !=(RuntimeTypeHandle left, object? right) => !left.Equals(right);
        public static bool operator !=(object? left, RuntimeTypeHandle right) => !right.Equals(left);
    }

    // Method and field handles carry the same opaque native-width IDs; identity and hashing use
    // them as upstream uses its runtime pointers. They are never dereferenced.
    public readonly partial struct RuntimeMethodHandle : IEquatable<RuntimeMethodHandle>
    {
        private readonly nint _value;

        internal RuntimeMethodHandle(nint value) => _value = value;

        public nint Value => _value;
        public override int GetHashCode() => HashCode.Combine(Value);
        public override bool Equals([NotNullWhen(true)] object? obj) => obj is RuntimeMethodHandle handle && Equals(handle);
        public bool Equals(RuntimeMethodHandle handle) => _value == handle._value;
        public static bool operator ==(RuntimeMethodHandle left, RuntimeMethodHandle right) => left.Equals(right);
        public static bool operator !=(RuntimeMethodHandle left, RuntimeMethodHandle right) => !left.Equals(right);
    }

    public readonly partial struct RuntimeFieldHandle : IEquatable<RuntimeFieldHandle>
    {
        private readonly nint _value;

        internal RuntimeFieldHandle(nint value) => _value = value;

        public nint Value => _value;
        public override int GetHashCode() => HashCode.Combine(Value);
        public override bool Equals([NotNullWhen(true)] object? obj) => obj is RuntimeFieldHandle handle && Equals(handle);
        public bool Equals(RuntimeFieldHandle handle) => _value == handle._value;
        public static bool operator ==(RuntimeFieldHandle left, RuntimeFieldHandle right) => left.Equals(right);
        public static bool operator !=(RuntimeFieldHandle left, RuntimeFieldHandle right) => !left.Equals(right);
    }
}
