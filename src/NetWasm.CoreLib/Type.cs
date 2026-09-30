using System.Reflection;
using System.Runtime.CompilerServices;

namespace System
{
    public sealed partial class Type : MemberInfo
    {
        internal int SemanticTypeId = 0;

        private Type()
        {
        }

        public static Type? GetTypeFromHandle(RuntimeTypeHandle handle) => null;
        internal static Type GetTypeFromSemanticId(int semanticTypeId) =>
            throw new PlatformNotSupportedException(
                "Only compiler-emitted semantic type identities are supported.");
        public bool IsValueType => HasFlag(RuntimeTypeFactsFlags.ValueType);
        public bool IsEnum => HasFlag(RuntimeTypeFactsFlags.Enum);
        public bool IsInterface => HasFlag(RuntimeTypeFactsFlags.Interface);
        public bool IsByRef => HasFlag(RuntimeTypeFactsFlags.ByReference);
        public bool IsPointer => HasFlag(RuntimeTypeFactsFlags.Pointer);
        public bool ContainsGenericParameters =>
            HasFlag(RuntimeTypeFactsFlags.ContainsGenericParameters);
        public bool IsGenericTypeDefinition =>
            HasFlag(RuntimeTypeFactsFlags.GenericTypeDefinition);
        public bool IsGenericType => HasFlag(RuntimeTypeFactsFlags.GenericType);
        public bool IsArray => HasFlag(RuntimeTypeFactsFlags.Array);
        public bool IsSZArray => HasFlag(RuntimeTypeFactsFlags.SzArray);
        public bool IsClass => HasFlag(RuntimeTypeFactsFlags.Class);
        public bool IsSealed => HasFlag(RuntimeTypeFactsFlags.Sealed);

        internal bool IsNullable => HasFlag(RuntimeTypeFactsFlags.Nullable);

        public Type? BaseType
        {
            get
            {
                int typeId = GetFacts().BaseTypeId;
                return typeId == 0 ? null : GetTypeFromSemanticId(typeId);
            }
        }

        public static TypeCode GetTypeCode(Type? type) =>
            type is null ? TypeCode.Empty : (TypeCode)type.GetFacts().TypeCode;

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
            while (candidate is not null)
            {
                if (this == candidate || candidate.IsAssignableTo(SemanticTypeId))
                {
                    return true;
                }
                candidate = candidate.BaseType;
            }
            return false;
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

        internal MethodInfo GetDelegateInvokeMethod()
        {
            nint address = GetFacts().DelegateInvoke;
            if (address == 0)
            {
                throw new InvalidOperationException(
                    "Compiler-generated delegate Invoke metadata is missing.");
            }
            return Runtime.CompilerServices.Unsafe.As<nint, MethodInfo?>(ref address) ??
                throw new InvalidOperationException(
                    "Compiler-generated delegate Invoke metadata is invalid.");
        }

        public static bool operator ==(Type? left, Type? right) =>
            object.ReferenceEquals(left, right);
        public static bool operator !=(Type? left, Type? right) =>
            !object.ReferenceEquals(left, right);
        public override bool Equals(object? value) =>
            value is Type other && this == other;
        public override int GetHashCode() => SemanticTypeId;

        private bool HasFlag(int flag) => (GetFacts().Flags & flag) != 0;

        private unsafe bool IsAssignableTo(int targetTypeId)
        {
            RuntimeTypeFacts facts = GetFacts();
            int* typeIds = (int*)facts.AssignableTypeIds;
            for (int index = 0; index < facts.AssignableTypeIdCount; index++)
            {
                if (typeIds[index] == targetTypeId)
                {
                    return true;
                }
            }
            return false;
        }

        private unsafe RuntimeTypeFacts GetFacts()
        {
            nint address = InternalGetFacts(SemanticTypeId);
            if (address == 0)
            {
                throw new InvalidOperationException(
                    "Compiler-generated runtime type facts are missing.");
            }
            return *(RuntimeTypeFacts*)address;
        }

        private unsafe RuntimeTypeNameFacts GetNameFacts(int requiredPayload)
        {
            nint address = GetFacts().NameFacts;
            if (address == 0)
            {
                throw new InvalidOperationException(
                    "Compiler-generated runtime type-name facts are missing.");
            }
            RuntimeTypeNameFacts facts = *(RuntimeTypeNameFacts*)address;
            if ((facts.Payload & requiredPayload) == 0)
            {
                throw new InvalidOperationException(
                    "Compiler-generated runtime type-name facts are incomplete.");
            }
            return facts;
        }

        private string GetRuntimeName() =>
            GetString(GetNameFacts(RuntimeTypeNamePayload.Name).Name) ??
            throw new InvalidOperationException(
                "The compiler-generated runtime type name is invalid.");

        private string? GetRuntimeNamespace() =>
            GetString(GetNameFacts(RuntimeTypeNamePayload.Namespace).Namespace);

        private string? GetRuntimeFullName() =>
            GetString(GetNameFacts(RuntimeTypeNamePayload.FullName).FullName);

        private string GetRuntimeDisplayName() =>
            GetString(GetNameFacts(RuntimeTypeNamePayload.DisplayName).DisplayName) ??
            throw new InvalidOperationException(
                "The compiler-generated runtime type display name is invalid.");

        private static string? GetString(nint address)
        {
            if (address == 0)
            {
                return null;
            }
            return Runtime.CompilerServices.Unsafe.As<nint, string?>(ref address);
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern nint InternalGetFacts(int semanticTypeId);
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
    }

    internal static class RuntimeTypeNamePayload
    {
        internal const int Name = 1 << 0;
        internal const int Namespace = 1 << 1;
        internal const int FullName = 1 << 2;
        internal const int DisplayName = 1 << 3;
    }

    internal readonly struct RuntimeTypeFacts
    {
        internal readonly int Flags;
        internal readonly int TypeCode;
        internal readonly int BaseTypeId;
        internal readonly int AssignableTypeIdCount;
        internal readonly nint AssignableTypeIds;
        internal readonly nint DelegateInvoke;
        internal readonly nint NameFacts;
    }

    internal readonly struct RuntimeTypeNameFacts
    {
        internal readonly int Payload;
        internal readonly nint Name;
        internal readonly nint Namespace;
        internal readonly nint FullName;
        internal readonly nint DisplayName;
    }

    public readonly struct RuntimeTypeHandle
    {
    }

    public readonly struct RuntimeMethodHandle
    {
        private readonly nint _value;

        internal RuntimeMethodHandle(nint value) => _value = value;

        internal nint Value => _value;
    }

    public readonly struct RuntimeFieldHandle
    {
        private readonly nint _value;

        internal RuntimeFieldHandle(nint value) => _value = value;

        internal nint Value => _value;
    }
}
