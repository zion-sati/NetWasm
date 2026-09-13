// Adapted from dotnet/runtime System.Private.CoreLib compiler-service contracts.
// These types carry compiler metadata; they intentionally have no runtime machinery.

using System.ComponentModel;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class AccessedThroughPropertyAttribute : Attribute
    {
        public AccessedThroughPropertyAttribute(string propertyName) => PropertyName = propertyName;

        public string PropertyName { get; }
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class CallerArgumentExpressionAttribute : Attribute
    {
        public CallerArgumentExpressionAttribute(string parameterName) => ParameterName = parameterName;

        public string ParameterName { get; }
    }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class CallerFilePathAttribute : Attribute
    {
        public CallerFilePathAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class CallerLineNumberAttribute : Attribute
    {
        public CallerLineNumberAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class CallerMemberNameAttribute : Attribute
    {
        public CallerMemberNameAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Method)]
    public class CompilationRelaxationsAttribute : Attribute
    {
        public CompilationRelaxationsAttribute(int relaxations) => CompilationRelaxations = relaxations;

        public CompilationRelaxationsAttribute(CompilationRelaxations relaxations) =>
            CompilationRelaxations = (int)relaxations;

        public int CompilationRelaxations { get; }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public sealed class CompilerGeneratedAttribute : Attribute
    {
        public CompilerGeneratedAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class CompilerGlobalScopeAttribute : Attribute
    {
        public CompilerGlobalScopeAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CompilerLoweringPreserveAttribute : Attribute
    {
        public CompilerLoweringPreserveAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class CreateNewOnMetadataUpdateAttribute : Attribute
    {
        public CreateNewOnMetadataUpdateAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter, Inherited = false)]
    public abstract class CustomConstantAttribute : Attribute
    {
        public abstract object? Value { get; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter, Inherited = false)]
    public sealed class DateTimeConstantAttribute : CustomConstantAttribute
    {
        private readonly DateTime _date;

        public DateTimeConstantAttribute(long ticks) => _date = new DateTime(ticks);

        public override object Value
        {
            get { return _date; }
        }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter, Inherited = false)]
    public sealed class DecimalConstantAttribute : Attribute
    {
        private readonly decimal _dec;

        public DecimalConstantAttribute(byte scale, byte sign, uint hi, uint mid, uint low) =>
            _dec = new decimal((int)low, (int)mid, (int)hi, sign != 0, scale);

        public DecimalConstantAttribute(byte scale, byte sign, int hi, int mid, int low) =>
            _dec = new decimal(low, mid, hi, sign != 0, scale);

        public decimal Value
        {
            get { return _dec; }
        }
    }

    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class DefaultDependencyAttribute : Attribute
    {
        public DefaultDependencyAttribute(LoadHint loadHintArgument) => LoadHint = loadHintArgument;

        public LoadHint LoadHint { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class DependencyAttribute : Attribute
    {
        public DependencyAttribute(string dependentAssemblyArgument, LoadHint loadHintArgument)
        {
            DependentAssembly = dependentAssemblyArgument;
            LoadHint = loadHintArgument;
        }

        public string DependentAssembly { get; }
        public LoadHint LoadHint { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class DisablePrivateReflectionAttribute : Attribute
    {
        public DisablePrivateReflectionAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
    public sealed class DisableRuntimeMarshallingAttribute : Attribute
    {
        public DisableRuntimeMarshallingAttribute() { }
    }

    [AttributeUsage(AttributeTargets.All)]
    public class DiscardableAttribute : Attribute
    {
        public DiscardableAttribute() { }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum |
                    AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field |
                    AttributeTargets.Event | AttributeTargets.Interface | AttributeTargets.Delegate,
                    Inherited = false)]
    public sealed class ExtensionMarkerAttribute : Attribute
    {
        public ExtensionMarkerAttribute(string name) => Name = name;

        public string Name { get; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FixedAddressValueTypeAttribute : Attribute
    {
        public FixedAddressValueTypeAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class FixedBufferAttribute : Attribute
    {
        public FixedBufferAttribute(Type elementType, int length)
        {
            ElementType = elementType;
            Length = length;
        }

        public Type ElementType { get; }
        public int Length { get; }
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class InterpolatedStringHandlerArgumentAttribute : Attribute
    {
        public InterpolatedStringHandlerArgumentAttribute(string argument) => Arguments = new[] { argument };

        public InterpolatedStringHandlerArgumentAttribute(params string[] arguments) => Arguments = arguments;

        public string[] Arguments { get; }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class IsClosedTypeAttribute : Attribute
    {
        private Type[] _derivedTypes = new Type[0];

        public IsClosedTypeAttribute() { }

        public Type[] DerivedTypes
        {
            get => _derivedTypes;
            set => _derivedTypes = value ?? new Type[0];
        }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.All)]
    public sealed class IsUnmanagedAttribute : Attribute
    {
        public IsUnmanagedAttribute() { }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Module, Inherited = false, AllowMultiple = false)]
    public sealed class MemorySafetyRulesAttribute : Attribute
    {
        public MemorySafetyRulesAttribute(int version) => Version = version;

        public int Version { get; }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
    public sealed class MetadataUpdateDeletedAttribute : Attribute
    {
        public MetadataUpdateDeletedAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public class MetadataUpdateOriginalTypeAttribute : Attribute
    {
        public MetadataUpdateOriginalTypeAttribute(Type originalType) => OriginalType = originalType;

        public Type OriginalType { get; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, Inherited = false)]
    public sealed class MethodImplAttribute : Attribute
    {
        public MethodCodeType MethodCodeType;

        public MethodImplAttribute(MethodImplOptions methodImplOptions) => Value = methodImplOptions;

        public MethodImplAttribute(short value) => Value = (MethodImplOptions)value;

        public MethodImplAttribute() { }

        public MethodImplOptions Value { get; }
    }

    [Flags]
    public enum MethodImplOptions
    {
        Unmanaged = 0x0004,
        NoInlining = 0x0008,
        ForwardRef = 0x0010,
        Synchronized = 0x0020,
        NoOptimization = 0x0040,
        PreserveSig = 0x0080,
        AggressiveInlining = 0x0100,
        AggressiveOptimization = 0x0200,
        Async = 0x2000,
        InternalCall = 0x1000
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class ModuleInitializerAttribute : Attribute
    {
        public ModuleInitializerAttribute() { }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field |
                    AttributeTargets.Event | AttributeTargets.Parameter | AttributeTargets.ReturnValue |
                    AttributeTargets.GenericParameter, Inherited = false)]
    public sealed class NullableAttribute : Attribute
    {
        public readonly byte[] NullableFlags;

        public NullableAttribute(byte value) => NullableFlags = new[] { value };

        public NullableAttribute(byte[] value) => NullableFlags = value;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method |
                    AttributeTargets.Interface | AttributeTargets.Delegate, Inherited = false)]
    public sealed class NullableContextAttribute : Attribute
    {
        public readonly byte Flag;

        public NullableContextAttribute(byte value) => Flag = value;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Module, Inherited = false)]
    public sealed class NullablePublicOnlyAttribute : Attribute
    {
        public readonly bool IncludesInternals;

        public NullablePublicOnlyAttribute(bool value) => IncludesInternals = value;
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property,
                    AllowMultiple = false, Inherited = false)]
    public sealed class OverloadResolutionPriorityAttribute : Attribute
    {
        public OverloadResolutionPriorityAttribute(int priority) => Priority = priority;

        public int Priority { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class ReferenceAssemblyAttribute : Attribute
    {
        public ReferenceAssemblyAttribute() { }

        public ReferenceAssemblyAttribute(string? description) => Description = description;

        public string? Description { get; }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class RequiresLocationAttribute : Attribute
    {
        public RequiresLocationAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
    public sealed class RuntimeCompatibilityAttribute : Attribute
    {
        public RuntimeCompatibilityAttribute() { }

        public bool WrapNonExceptionThrows { get; set; }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class ScopedRefAttribute : Attribute
    {
        public ScopedRefAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct |
                    AttributeTargets.Interface | AttributeTargets.Constructor | AttributeTargets.Method |
                    AttributeTargets.Property | AttributeTargets.Event, Inherited = false)]
    public sealed class SkipLocalsInitAttribute : Attribute
    {
        public SkipLocalsInitAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property |
                    AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Struct)]
    public sealed class SpecialNameAttribute : Attribute
    {
        public SpecialNameAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class StringFreezingAttribute : Attribute
    {
        public StringFreezingAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Module)]
    public sealed class SuppressIldasmAttribute : Attribute
    {
        public SuppressIldasmAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum |
                    AttributeTargets.Interface | AttributeTargets.Delegate, Inherited = false, AllowMultiple = false)]
    public sealed class TypeForwardedFromAttribute : Attribute
    {
        public TypeForwardedFromAttribute(string assemblyFullName)
        {
            ArgumentException.ThrowIfNullOrEmpty(assemblyFullName);
            AssemblyFullName = assemblyFullName;
        }

        public string AssemblyFullName { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class TypeForwardedToAttribute : Attribute
    {
        public TypeForwardedToAttribute(Type destination) => Destination = destination;

        public Type Destination { get; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class UnionAttribute : Attribute
    {
        public UnionAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class UnsafeAccessorAttribute : Attribute
    {
        public UnsafeAccessorAttribute(UnsafeAccessorKind kind) => Kind = kind;

        public UnsafeAccessorKind Kind { get; }

        public string? Name { get; set; }
    }

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false, Inherited = false)]
    public sealed class UnsafeAccessorTypeAttribute : Attribute
    {
        public UnsafeAccessorTypeAttribute(string typeName) => TypeName = typeName;

        public string TypeName { get; }
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class UnsafeValueTypeAttribute : Attribute
    {
        public UnsafeValueTypeAttribute() { }
    }
}
