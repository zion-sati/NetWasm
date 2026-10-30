// Portions derived from dotnet/runtime System.Private.CoreLib.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = false)]
    public abstract class Attribute
    {
        protected Attribute()
        {
        }

        // Desktop Attribute equality enumerates instance fields through reflection.
        // NetWasm deliberately keeps that discovery surface outside its closed-world
        // profile, so the base implementation retains identity equality instead.
        public override bool Equals(object? value) => ReferenceEquals(this, value);
        public override int GetHashCode() => base.GetHashCode();
        public virtual bool IsDefaultAttribute() => false;
        public virtual bool Match(object? value) => Equals(value);
        public virtual object TypeId
        {
            get => GetType();
        }

        public static Attribute? GetCustomAttribute(
            Reflection.MemberInfo element,
            Type attributeType) =>
            Reflection.CustomAttributeExtensions.GetCustomAttribute(element, attributeType);

        public static Attribute? GetCustomAttribute(
            Reflection.MemberInfo element,
            Type attributeType,
            bool inherit) =>
            Reflection.CustomAttributeExtensions.GetCustomAttribute(
                element,
                attributeType,
                inherit);

        public static Attribute[] GetCustomAttributes(
            Reflection.MemberInfo element,
            Type attributeType) =>
            Reflection.CustomAttributeExtensions.GetCustomAttributesArray(
                element,
                attributeType,
                inherit: true);

        public static Attribute[] GetCustomAttributes(
            Reflection.MemberInfo element,
            Type attributeType,
            bool inherit) =>
            Reflection.CustomAttributeExtensions.GetCustomAttributesArray(
                element,
                attributeType,
                inherit);

        public static bool IsDefined(
            Reflection.MemberInfo element,
            Type attributeType) =>
            Reflection.CustomAttributeExtensions.IsDefined(element, attributeType);

        public static bool IsDefined(
            Reflection.MemberInfo element,
            Type attributeType,
            bool inherit) =>
            Reflection.CustomAttributeExtensions.IsDefined(
                element,
                attributeType,
                inherit);
    }

    public enum AttributeTargets
    {
        Assembly = 0x0001,
        Module = 0x0002,
        Class = 0x0004,
        Struct = 0x0008,
        Enum = 0x0010,
        Constructor = 0x0020,
        Method = 0x0040,
        Property = 0x0080,
        Field = 0x0100,
        Event = 0x0200,
        Interface = 0x0400,
        Parameter = 0x0800,
        Delegate = 0x1000,
        ReturnValue = 0x2000,
        GenericParameter = 0x4000,
        All = Assembly | Module | Class | Struct | Enum | Constructor |
            Method | Property | Field | Event | Interface | Parameter |
            Delegate | ReturnValue | GenericParameter,
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class AttributeUsageAttribute : Attribute
    {
        private readonly AttributeTargets _attributeTarget;
        private bool _allowMultiple;
        private bool _inherited;

        public AttributeUsageAttribute(AttributeTargets validOn)
        {
            _attributeTarget = validOn;
            _inherited = true;
        }

        public AttributeTargets ValidOn => _attributeTarget;

        public bool AllowMultiple
        {
            get => _allowMultiple;
            set => _allowMultiple = value;
        }

        public bool Inherited
        {
            get => _inherited;
            set => _inherited = value;
        }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class ParamArrayAttribute : Attribute
    {
        public ParamArrayAttribute()
        {
        }
    }

    [AttributeUsage(AttributeTargets.Enum)]
    public sealed class FlagsAttribute : Attribute
    {
        public FlagsAttribute()
        {
        }
    }
}

namespace System.Reflection
{
    public sealed class DefaultMemberAttribute : Attribute
    {
        public DefaultMemberAttribute(string memberName)
        {
            MemberName = memberName;
        }

        public string MemberName { get; }
    }
}

namespace System.Runtime.CompilerServices
{
    [Flags]
    public enum CompilationRelaxations : int
    {
        NoStringInterning = 0x0008,
    }

    public static class ContractHelper
    {
    }

    public static class IsConst
    {
    }

    public interface IStrongBox
    {
        object? Value { get; set; }
    }

    public interface IUnion
    {
        object? Value { get; }
    }

    public enum LoadHint
    {
        Default = 0x0000,
        Always = 0x0001,
        Sometimes = 0x0002,
    }

    public enum MethodCodeType
    {
        IL = Reflection.MethodImplAttributes.IL,
        Native = Reflection.MethodImplAttributes.Native,
        OPTIL = Reflection.MethodImplAttributes.OPTIL,
        Runtime = Reflection.MethodImplAttributes.Runtime,
    }

    // Matches the dotnet/runtime modifier type used in volatile field signatures.
    public static class IsVolatile
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class InterpolatedStringHandlerAttribute : Attribute
    {
        public InterpolatedStringHandlerAttribute()
        {
        }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly)]
    public sealed class ExtensionAttribute : Attribute
    {
    }

    public static class RuntimeFeature
    {
        public const string ByRefFields = nameof(ByRefFields);
        public const string ByRefLikeGenerics = nameof(ByRefLikeGenerics);
        public const string CovariantReturnsOfClasses = nameof(CovariantReturnsOfClasses);
        public const string DefaultImplementationsOfInterfaces =
            nameof(DefaultImplementationsOfInterfaces);
        public const string NumericIntPtr = nameof(NumericIntPtr);
        public const string PortablePdb = nameof(PortablePdb);
        public const string UnmanagedSignatureCallingConvention =
            nameof(UnmanagedSignatureCallingConvention);
        public const string VirtualStaticsInInterfaces = nameof(VirtualStaticsInInterfaces);

        public static bool IsDynamicCodeCompiled => false;
        public static bool IsDynamicCodeSupported => false;
        public static bool IsMultithreadingSupported => false;

        public static bool IsSupported(string feature) => feature switch
        {
            ByRefFields or ByRefLikeGenerics or CovariantReturnsOfClasses or
            DefaultImplementationsOfInterfaces or NumericIntPtr or PortablePdb or
            UnmanagedSignatureCallingConvention or VirtualStaticsInInterfaces => true,
            nameof(IsDynamicCodeSupported) => IsDynamicCodeSupported,
            nameof(IsDynamicCodeCompiled) => IsDynamicCodeCompiled,
            nameof(IsMultithreadingSupported) => IsMultithreadingSupported,
            _ => false,
        };

        [Diagnostics.CodeAnalysis.DoesNotReturn]
        internal static void ThrowIfMultithreadingIsNotSupported() =>
            throw new PlatformNotSupportedException();
    }

    [InlineArray(2)] public struct InlineArray2<T> { private T t; }
    [InlineArray(3)] public struct InlineArray3<T> { private T t; }
    [InlineArray(4)] public struct InlineArray4<T> { private T t; }
    [InlineArray(5)] public struct InlineArray5<T> { private T t; }
    [InlineArray(6)] public struct InlineArray6<T> { private T t; }
    [InlineArray(7)] public struct InlineArray7<T> { private T t; }
    [InlineArray(8)] public struct InlineArray8<T> { private T t; }
    [InlineArray(9)] public struct InlineArray9<T> { private T t; }
    [InlineArray(10)] public struct InlineArray10<T> { private T t; }
    [InlineArray(11)] public struct InlineArray11<T> { private T t; }
    [InlineArray(12)] public struct InlineArray12<T> { private T t; }
    [InlineArray(13)] public struct InlineArray13<T> { private T t; }
    [InlineArray(14)] public struct InlineArray14<T> { private T t; }
    [InlineArray(15)] public struct InlineArray15<T> { private T t; }
    [InlineArray(16)] public struct InlineArray16<T> { private T t; }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class PreserveBaseOverridesAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class InlineArrayAttribute : Attribute
    {
        public InlineArrayAttribute(int length) => Length = length;
        public int Length { get; }
    }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = true, AllowMultiple = false)]
    public sealed class ParamCollectionAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct |
        AttributeTargets.Interface)]
    public sealed class CollectionBuilderAttribute : Attribute
    {
        public CollectionBuilderAttribute(Type builderType, string methodName)
        {
            BuilderType = builderType;
            MethodName = methodName;
        }

        public Type BuilderType { get; }
        public string MethodName { get; }
    }

    public static class IsExternalInit
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field |
        AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class RequiredMemberAttribute : Attribute
    {
    }

    // RuntimeHelpers compatibility members are adapted from dotnet/runtime
    // System.Private.CoreLib at commit 474ed963699fc29809015392738a15231aaae2de.
    // The upstream implementation is licensed under the MIT license.
    public static class RuntimeHelpers
    {
        public delegate void CleanupCode(object? userData, bool exceptionThrown);
        public delegate void TryCode(object? userData);

        public static bool IsReferenceOrContainsReferences<T>()
            where T : allows ref struct => false;

        // NetWasm: upstream's runtime supplies this body. Comparing bits is
        // comparing values for the integer primitives, char and bool, and each
        // specialization folds the type tests to a constant. Other types, enums
        // included, take the paths that call Equals, which agree.
        internal static bool IsBitwiseEquatable<T>() =>
            typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte) ||
            typeof(T) == typeof(short) || typeof(T) == typeof(ushort) ||
            typeof(T) == typeof(int) || typeof(T) == typeof(uint) ||
            typeof(T) == typeof(long) || typeof(T) == typeof(ulong) ||
            typeof(T) == typeof(nint) || typeof(T) == typeof(nuint) ||
            typeof(T) == typeof(char) || typeof(T) == typeof(bool);

        internal static bool IsPrimitiveType(this System.Reflection.CorElementType et)
            // COR_ELEMENT_TYPE_I1,I2,I4,I8,U1,U2,U4,U8,R4,R8,I,U,CHAR,BOOLEAN
            => ((1 << (int)et) & 0b_0011_0000_0000_0011_1111_1111_1100) != 0;

        public static int GetHashCode(object? value) => ObjectIdentityRuntime.GetHashCode(value);

        public static int OffsetToStringData => IntPtr.Size + sizeof(int);

        [Intrinsic]
        public static ReadOnlySpan<T> CreateSpan<T>(RuntimeFieldHandle fldHandle) =>
            throw new PlatformNotSupportedException();

        public static void InitializeArray(
            Array array,
            RuntimeFieldHandle fieldHandle) =>
            throw new PlatformNotSupportedException(
                "Only compiler-recognized static array initialization is supported.");

        [Obsolete("The Constrained Execution Region (CER) feature is not supported.",
            DiagnosticId = "SYSLIB0004",
            UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        public static void ExecuteCodeWithGuaranteedCleanup(
            TryCode code,
            CleanupCode backoutCode,
            object? userData)
        {
            ArgumentNullException.ThrowIfNull(code);
            ArgumentNullException.ThrowIfNull(backoutCode);

            var exceptionThrown = true;
            try
            {
                code(userData);
                exceptionThrown = false;
            }
            finally
            {
                backoutCode(userData, exceptionThrown);
            }
        }

        public static new bool Equals(object? o1, object? o2) =>
            RuntimeHelpersRuntime.AreEqual(o1, o2);

        [return: Diagnostics.CodeAnalysis.NotNullIfNotNull("obj")]
        public static object? GetObjectValue(object? obj)
        {
            if (obj is null)
            {
                return null;
            }

            var type = obj.GetType();
            if (!type.IsValueType || IsPrimitive(type))
            {
                return obj;
            }

            return RuntimeHelpersRuntime.CloneBoxedValue(obj) ??
                throw new OutOfMemoryException();
        }

        public static T[] GetSubArray<T>(T[] array, Range range)
        {
            ArgumentNullException.ThrowIfNull(array);
            var (offset, length) = range.GetOffsetAndLength(array.Length);

            if (typeof(T[]) != array.GetType())
            {
                return (T[])Array.InternalCloneRange(array, offset, length);
            }
            if (length == 0)
            {
                return [];
            }

            var destination = new T[length];
            Array.Copy(array, offset, destination, 0, length);
            return destination;
        }

        [Obsolete("The Constrained Execution Region (CER) feature is not supported.",
            DiagnosticId = "SYSLIB0004",
            UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        public static void PrepareConstrainedRegions()
        {
        }

        [Obsolete("The Constrained Execution Region (CER) feature is not supported.",
            DiagnosticId = "SYSLIB0004",
            UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        public static void PrepareConstrainedRegionsNoOP()
        {
        }

        [Obsolete("The Constrained Execution Region (CER) feature is not supported.",
            DiagnosticId = "SYSLIB0004",
            UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        public static void PrepareContractedDelegate(Delegate d)
        {
        }

        public static void PrepareDelegate(Delegate d)
        {
        }

        public static void EnsureSufficientExecutionStack()
        {
        }

        // The compiler calls this for a value type that inherits ToString from Object or ValueType,
        // passing the receiver by reference instead of boxing it.
        internal static string ValueTypeToString<T>(ref T value) where T : struct => typeof(T).ToString();

        [Obsolete("The Constrained Execution Region (CER) feature is not supported.",
            DiagnosticId = "SYSLIB0004",
            UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        public static void ProbeForSufficientStack()
        {
        }

        [Intrinsic]
        internal static bool IsKnownConstant(Type? value) => false;

        [Intrinsic]
        internal static bool IsKnownConstant(string? value) => false;

        [Intrinsic]
        internal static bool IsKnownConstant(char value) => false;

        [Intrinsic]
        internal static bool IsKnownConstant<T>(T value) where T : struct => false;

        [Intrinsic]
        internal static bool IsRuntimeAsync() => false;

        [Intrinsic]
        internal static void WriteBarrier(ref object? destination, object? value) =>
            destination = value;

        private static bool IsPrimitive(Type type) =>
            type.IsEnum ||
            Type.GetTypeCode(type) is >= TypeCode.Boolean and <= TypeCode.Double ||
            type == typeof(nint) ||
            type == typeof(nuint);
    }

    internal static class RuntimeHelpersRuntime
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern object? CloneBoxedValue(object value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern bool AreEqual(object? left, object? right);
    }

    public class StrongBox<T> : IStrongBox
    {
        [Diagnostics.CodeAnalysis.MaybeNull]
        public T Value = default!;

        public StrongBox()
        {
        }

        public StrongBox(T value)
        {
            Value = value;
        }

        object? IStrongBox.Value
        {
            get => Value;
            set => Value = (T)value!;
        }
    }

    public enum UnsafeAccessorKind
    {
        Constructor,
        Method,
        StaticMethod,
        Field,
        StaticField,
    }

    [AttributeUsage(AttributeTargets.Property, Inherited = true)]
    public sealed class IndexerNameAttribute : Attribute
    {
        public IndexerNameAttribute(string indexerName)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class IsByRefLikeAttribute : Attribute
    {
        public IsByRefLikeAttribute()
        {
        }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class IsReadOnlyAttribute : Attribute
    {
        public IsReadOnlyAttribute()
        {
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public const string RefStructs = "RefStructs";
        public const string RequiredMembers = "RequiredMembers";

        public CompilerFeatureRequiredAttribute(string featureName)
        {
            FeatureName = featureName;
        }

        public string FeatureName { get; }
        public bool IsOptional { get; init; }
    }

    [AttributeUsage(AttributeTargets.Module, Inherited = false)]
    public sealed class RefSafetyRulesAttribute : Attribute
    {
        public RefSafetyRulesAttribute(int version)
        {
            Version = version;
        }

        public int Version { get; }
    }

    [AttributeUsage(AttributeTargets.All)]
    public sealed class TupleElementNamesAttribute : Attribute
    {
        private readonly string?[] _transformNames;

        public TupleElementNamesAttribute(string?[] transformNames)
        {
            ArgumentNullException.ThrowIfNull(transformNames);
            _transformNames = transformNames;
        }

        public System.Collections.Generic.IList<string?> TransformNames => _transformNames;
    }
}
