// Adapted from dotnet/runtime System.Diagnostics.CodeAnalysis metadata
// declarations. The upstream implementation is licensed under MIT.

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
    public sealed class AllowNullAttribute : Attribute
    {
        public AllowNullAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
    public sealed class DisallowNullAttribute : Attribute
    {
        public DisallowNullAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue, Inherited = false)]
    public sealed class MaybeNullAttribute : Attribute
    {
        public MaybeNullAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue, Inherited = false)]
    public sealed class NotNullAttribute : Attribute
    {
        public NotNullAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue, AllowMultiple = true, Inherited = false)]
    public sealed class NotNullIfNotNullAttribute : Attribute
    {
        public NotNullIfNotNullAttribute(string parameterName) => ParameterName = parameterName;
        public string ParameterName { get; }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class DoesNotReturnAttribute : Attribute
    {
        public DoesNotReturnAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class DoesNotReturnIfAttribute : Attribute
    {
        public DoesNotReturnIfAttribute(bool parameterValue) => ParameterValue = parameterValue;
        public bool ParameterValue { get; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
    public sealed class MemberNotNullAttribute : Attribute
    {
        public MemberNotNullAttribute(string member) => Members = [member];
        public MemberNotNullAttribute(params string[] members) => Members = members;
        public string[] Members { get; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
    public sealed class MemberNotNullWhenAttribute : Attribute
    {
        public MemberNotNullWhenAttribute(bool returnValue, string member)
        {
            ReturnValue = returnValue;
            Members = [member];
        }

        public MemberNotNullWhenAttribute(bool returnValue, params string[] members)
        {
            ReturnValue = returnValue;
            Members = members;
        }

        public bool ReturnValue { get; }
        public string[] Members { get; }
    }

    [Flags]
    public enum DynamicallyAccessedMemberTypes
    {
        None = 0,
        PublicParameterlessConstructor = 0x0001,
        PublicConstructors = 0x0002 | PublicParameterlessConstructor,
        NonPublicConstructors = 0x0004,
        PublicMethods = 0x0008,
        NonPublicMethods = 0x0010,
        PublicFields = 0x0020,
        NonPublicFields = 0x0040,
        PublicNestedTypes = 0x0080,
        NonPublicNestedTypes = 0x0100,
        PublicProperties = 0x0200,
        NonPublicProperties = 0x0400,
        PublicEvents = 0x0800,
        NonPublicEvents = 0x1000,
        Interfaces = 0x2000,
        NonPublicConstructorsWithInherited = NonPublicConstructors | 0x4000,
        NonPublicMethodsWithInherited = NonPublicMethods | 0x8000,
        NonPublicFieldsWithInherited = NonPublicFields | 0x10000,
        NonPublicNestedTypesWithInherited = NonPublicNestedTypes | 0x20000,
        NonPublicPropertiesWithInherited = NonPublicProperties | 0x40000,
        NonPublicEventsWithInherited = NonPublicEvents | 0x80000,
        PublicConstructorsWithInherited = PublicConstructors | 0x100000,
        PublicNestedTypesWithInherited = PublicNestedTypes | 0x200000,
        AllConstructors = PublicConstructorsWithInherited | NonPublicConstructorsWithInherited,
        AllMethods = PublicMethods | NonPublicMethodsWithInherited,
        AllFields = PublicFields | NonPublicFieldsWithInherited,
        AllNestedTypes = PublicNestedTypesWithInherited | NonPublicNestedTypesWithInherited,
        AllProperties = PublicProperties | NonPublicPropertiesWithInherited,
        AllEvents = PublicEvents | NonPublicEventsWithInherited,
        All = ~None
    }

    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.ReturnValue | AttributeTargets.GenericParameter |
        AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Method |
        AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct,
        Inherited = false)]
    public sealed class DynamicallyAccessedMembersAttribute : Attribute
    {
        public DynamicallyAccessedMembersAttribute(DynamicallyAccessedMemberTypes memberTypes) => MemberTypes = memberTypes;
        public DynamicallyAccessedMemberTypes MemberTypes { get; }
    }

    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class DynamicDependencyAttribute : Attribute
    {
        public DynamicDependencyAttribute(string memberSignature) => MemberSignature = memberSignature;

        public DynamicDependencyAttribute(string memberSignature, Type type)
        {
            MemberSignature = memberSignature;
            Type = type;
        }

        public DynamicDependencyAttribute(string memberSignature, string typeName, string assemblyName)
        {
            MemberSignature = memberSignature;
            TypeName = typeName;
            AssemblyName = assemblyName;
        }

        public DynamicDependencyAttribute(DynamicallyAccessedMemberTypes memberTypes, Type type)
        {
            MemberTypes = memberTypes;
            Type = type;
        }

        public DynamicDependencyAttribute(DynamicallyAccessedMemberTypes memberTypes, string typeName, string assemblyName)
        {
            MemberTypes = memberTypes;
            TypeName = typeName;
            AssemblyName = assemblyName;
        }

        public string? MemberSignature { get; }
        public DynamicallyAccessedMemberTypes MemberTypes { get; }
        public Type? Type { get; }
        public string? TypeName { get; }
        public string? AssemblyName { get; }
        public string? Condition { get; set; }
    }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class ConstantExpectedAttribute : Attribute
    {
        public ConstantExpectedAttribute() { }
        public object? Min { get; set; }
        public object? Max { get; set; }
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Constructor |
        AttributeTargets.Event | AttributeTargets.Interface | AttributeTargets.Method |
        AttributeTargets.Property | AttributeTargets.Struct, Inherited = false)]
    public sealed class ExcludeFromCodeCoverageAttribute : Attribute
    {
        public ExcludeFromCodeCoverageAttribute() { }
        public string? Justification { get; set; }
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Module | AttributeTargets.Class |
        AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Constructor |
        AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field |
        AttributeTargets.Event | AttributeTargets.Interface | AttributeTargets.Delegate, Inherited = false)]
    public sealed class ExperimentalAttribute : Attribute
    {
        public ExperimentalAttribute(string diagnosticId) => DiagnosticId = diagnosticId;
        public string DiagnosticId { get; }
        public string? Message { get; set; }
        public string? UrlFormat { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
    public sealed class FeatureGuardAttribute : Attribute
    {
        public FeatureGuardAttribute(Type featureType) => FeatureType = featureType;
        public Type FeatureType { get; }
    }

    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    public sealed class FeatureSwitchDefinitionAttribute : Attribute
    {
        public FeatureSwitchDefinitionAttribute(string switchName) => SwitchName = switchName;
        public string SwitchName { get; }
    }

    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    public sealed class SetsRequiredMembersAttribute : Attribute
    {
        public SetsRequiredMembersAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class UnscopedRefAttribute : Attribute
    {
        public UnscopedRefAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Event | AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
    public sealed class RequiresUnsafeAttribute : Attribute
    {
        public RequiresUnsafeAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Event | AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
    public sealed class RequiresAssemblyFilesAttribute : Attribute
    {
        public RequiresAssemblyFilesAttribute() { }
        public RequiresAssemblyFilesAttribute(string message) => Message = message;
        public string? Message { get; }
        public string? Url { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class, Inherited = false)]
    public sealed class RequiresDynamicCodeAttribute : Attribute
    {
        public RequiresDynamicCodeAttribute(string message) => Message = message;
        public bool ExcludeStatics { get; set; }
        public string Message { get; }
        public string? Url { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class, Inherited = false)]
    public sealed class RequiresUnreferencedCodeAttribute : Attribute
    {
        public RequiresUnreferencedCodeAttribute(string message) => Message = message;
        public bool ExcludeStatics { get; set; }
        public string Message { get; }
        public string? Url { get; set; }
    }

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class StringSyntaxAttribute : Attribute
    {
        public StringSyntaxAttribute(string syntax)
        {
            Syntax = syntax;
            Arguments = [];
        }

        public StringSyntaxAttribute(string syntax, params object?[] arguments)
        {
            Syntax = syntax;
            Arguments = arguments;
        }

        public string Syntax { get; }
        public object?[] Arguments { get; }
        public const string CompositeFormat = "CompositeFormat";
        public const string CSharp = "C#";
        public const string DateOnlyFormat = "DateOnlyFormat";
        public const string DateTimeFormat = "DateTimeFormat";
        public const string EnumFormat = "EnumFormat";
        public const string FSharp = "F#";
        public const string GuidFormat = "GuidFormat";
        public const string Json = "Json";
        public const string NumericFormat = "NumericFormat";
        public const string Regex = "Regex";
        public const string TimeOnlyFormat = "TimeOnlyFormat";
        public const string TimeSpanFormat = "TimeSpanFormat";
        public const string Uri = "Uri";
        public const string VisualBasic = "VisualBasic";
        public const string Xml = "Xml";
    }

    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    public sealed class SuppressMessageAttribute : Attribute
    {
        public SuppressMessageAttribute(string category, string checkId)
        {
            Category = category;
            CheckId = checkId;
        }

        public string Category { get; }
        public string CheckId { get; }
        public string? Scope { get; set; }
        public string? Target { get; set; }
        public string? MessageId { get; set; }
        public string? Justification { get; set; }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    public sealed class UnconditionalSuppressMessageAttribute : Attribute
    {
        public UnconditionalSuppressMessageAttribute(string category, string checkId)
        {
            Category = category;
            CheckId = checkId;
        }

        public string Category { get; }
        public string CheckId { get; }
        public string? Scope { get; set; }
        public string? Target { get; set; }
        public string? MessageId { get; set; }
        public string? Justification { get; set; }
    }
}
