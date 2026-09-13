namespace System
{
    public abstract class Attribute
    {
        public override bool Equals(object? value) => ReferenceEquals(this, value);
        public override int GetHashCode() => 0;
        public virtual bool IsDefaultAttribute() => false;
        public virtual bool Match(object? value) => Equals(value);
        public virtual object TypeId
        {
            get => GetType();
        }
    }

    public enum AttributeTargets
    {
        Assembly = 1,
        Module = 2,
        Class = 4,
        Struct = 8,
        Enum = 16,
        Constructor = 32,
        Method = 64,
        Property = 128,
        Field = 256,
        Event = 512,
        Interface = 1024,
        Parameter = 2048,
        Delegate = 4096,
        ReturnValue = 8192,
        GenericParameter = 16384,
        All = 32767
    }

    public sealed class AttributeUsageAttribute : Attribute
    {
        public AttributeUsageAttribute(AttributeTargets validOn)
        {
            ValidOn = validOn;
        }

        public AttributeTargets ValidOn { get; }
        public bool AllowMultiple { get; set; }
        public bool Inherited { get; set; }
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
    public enum CompilationRelaxations
    {
        NoStringInterning = 8,
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
        Default = 0,
        Always = 1,
        Sometimes = 2,
    }

    public enum MethodCodeType
    {
        IL = 0,
        Native = 1,
        OPTIL = 2,
        Runtime = 3,
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
        public ExtensionAttribute()
        {
        }
    }

    public static class RuntimeFeature
    {
        public const string ByRefFields = "ByRefFields";
        public const string ByRefLikeGenerics = "ByRefLikeGenerics";
        public const string CovariantReturnsOfClasses = "CovariantReturnsOfClasses";
        public const string DefaultImplementationsOfInterfaces =
            "DefaultImplementationsOfInterfaces";
        public const string NumericIntPtr = "NumericIntPtr";
        public const string PortablePdb = "PortablePdb";
        public const string UnmanagedSignatureCallingConvention =
            "UnmanagedSignatureCallingConvention";
        public const string VirtualStaticsInInterfaces = "VirtualStaticsInInterfaces";

        public static bool IsDynamicCodeCompiled
        {
            get => false;
        }
        public static bool IsDynamicCodeSupported
        {
            get => false;
        }
        public static bool IsMultithreadingSupported
        {
            get => false;
        }

        public static bool IsSupported(string feature) => feature switch
        {
            ByRefFields or ByRefLikeGenerics or CovariantReturnsOfClasses or
            DefaultImplementationsOfInterfaces or NumericIntPtr or PortablePdb or
            UnmanagedSignatureCallingConvention or VirtualStaticsInInterfaces => true,
            _ => false,
        };
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

    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class PreserveBaseOverridesAttribute : Attribute
    {
        public PreserveBaseOverridesAttribute()
        {
        }
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class InlineArrayAttribute : Attribute
    {
        public InlineArrayAttribute(int length) => Length = length;
        public int Length { get; }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class ParamCollectionAttribute : Attribute
    {
        public ParamCollectionAttribute()
        {
        }
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
        AttributeTargets.Property)]
    public sealed class RequiredMemberAttribute : Attribute
    {
        public RequiredMemberAttribute()
        {
        }
    }

    [AttributeUsage(AttributeTargets.Constructor)]
    public sealed class SetsRequiredMembersAttribute : Attribute
    {
    }

    // RuntimeHelpers compatibility members are adapted from dotnet/runtime
    // System.Private.CoreLib at commit 811225a482702af7ecc35d817966bc70b88a3a23.
    // The upstream implementation is licensed under the MIT license.
    public static class RuntimeHelpers
    {
        public delegate void CleanupCode(object? userData, bool exceptionThrown);
        public delegate void TryCode(object? userData);

        public static bool IsReferenceOrContainsReferences<T>()
            where T : allows ref struct => false;

        public static int GetHashCode(object? value) => ObjectIdentityRuntime.GetHashCode(value);

        public static int OffsetToStringData => IntPtr.Size + sizeof(int);
        public static void InitializeArray(
            Array array,
            RuntimeFieldHandle fieldHandle)
        {
        }

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

        public static new bool Equals(object? o1, object? o2) => object.Equals(o1, o2);

        [return: Diagnostics.CodeAnalysis.NotNullIfNotNull("obj")]
        public static object? GetObjectValue(object? obj) => obj;

        public static T[] GetSubArray<T>(T[] array, Range range)
        {
            ArgumentNullException.ThrowIfNull(array);
            var offsets = range.GetOffsetAndLength(array.Length);
            var result = new T[offsets.Item2];
            Array.Copy(array, offsets.Item1, result, 0, offsets.Item2);
            return result;
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
            ArgumentNullException.ThrowIfNull(d);
        }

        public static void EnsureSufficientExecutionStack()
        {
        }

        [Obsolete("The Constrained Execution Region (CER) feature is not supported.",
            DiagnosticId = "SYSLIB0004",
            UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        public static void ProbeForSufficientStack()
        {
        }
    }

    public class StrongBox<T> : IStrongBox
    {
        public T Value;

        public StrongBox() => Value = default!;

        public StrongBox(T value) => Value = value;

        object? IStrongBox.Value
        {
            get => Value;
            set => Value = (T)value!;
        }
    }

    public enum UnsafeAccessorKind
    {
        Constructor = 0,
        Method = 1,
        StaticMethod = 2,
        Field = 3,
        StaticField = 4,
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class IndexerNameAttribute : Attribute
    {
        public IndexerNameAttribute(string indexerName)
        {
            Value = indexerName;
        }

        public string Value { get; }
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class IsByRefLikeAttribute : Attribute
    {
        public IsByRefLikeAttribute()
        {
        }
    }

    [AttributeUsage(AttributeTargets.Struct)]
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

    [AttributeUsage(AttributeTargets.Module)]
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
        public TupleElementNamesAttribute(string?[] transformNames)
        {
            TransformNames = transformNames;
        }

        public System.Collections.Generic.IList<string?> TransformNames { get; }
    }
}
