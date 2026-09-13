// Adapted from dotnet/runtime System.Runtime.Versioning metadata declarations.
// The upstream implementation is licensed under MIT.

namespace System.Runtime.Versioning
{
    [Flags]
    public enum ComponentGuaranteesOptions
    {
        None = 0,
        Exchange = 0x1,
        Stable = 0x2,
        SideBySide = 0x4
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Module | AttributeTargets.Class |
        AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Delegate |
        AttributeTargets.Enum | AttributeTargets.Method | AttributeTargets.Property |
        AttributeTargets.Constructor | AttributeTargets.Event, AllowMultiple = false, Inherited = false)]
    public sealed class ComponentGuaranteesAttribute : Attribute
    {
        public ComponentGuaranteesAttribute(ComponentGuaranteesOptions guarantees) => Guarantees = guarantees;
        public ComponentGuaranteesOptions Guarantees { get; }
    }

    public abstract class OSPlatformAttribute : Attribute
    {
        protected OSPlatformAttribute(string platformName) => PlatformName = platformName;
        public string PlatformName { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class TargetPlatformAttribute : OSPlatformAttribute
    {
        public TargetPlatformAttribute(string platformName) : base(platformName) { }
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Constructor |
        AttributeTargets.Enum | AttributeTargets.Event | AttributeTargets.Field | AttributeTargets.Interface |
        AttributeTargets.Method | AttributeTargets.Module | AttributeTargets.Property | AttributeTargets.Struct,
        AllowMultiple = true, Inherited = false)]
    public sealed class SupportedOSPlatformAttribute : OSPlatformAttribute
    {
        public SupportedOSPlatformAttribute(string platformName) : base(platformName) { }
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Constructor |
        AttributeTargets.Enum | AttributeTargets.Event | AttributeTargets.Field | AttributeTargets.Interface |
        AttributeTargets.Method | AttributeTargets.Module | AttributeTargets.Property | AttributeTargets.Struct,
        AllowMultiple = true, Inherited = false)]
    public sealed class UnsupportedOSPlatformAttribute : OSPlatformAttribute
    {
        public UnsupportedOSPlatformAttribute(string platformName) : base(platformName) { }
        public UnsupportedOSPlatformAttribute(string platformName, string? message) : base(platformName) => Message = message;
        public string? Message { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Constructor |
        AttributeTargets.Enum | AttributeTargets.Event | AttributeTargets.Field | AttributeTargets.Interface |
        AttributeTargets.Method | AttributeTargets.Module | AttributeTargets.Property | AttributeTargets.Struct,
        AllowMultiple = true, Inherited = false)]
    public sealed class ObsoletedOSPlatformAttribute : OSPlatformAttribute
    {
        public ObsoletedOSPlatformAttribute(string platformName) : base(platformName) { }
        public ObsoletedOSPlatformAttribute(string platformName, string? message) : base(platformName) => Message = message;
        public string? Message { get; }
        public string? Url { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
    public sealed class SupportedOSPlatformGuardAttribute : OSPlatformAttribute
    {
        public SupportedOSPlatformGuardAttribute(string platformName) : base(platformName) { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
    public sealed class UnsupportedOSPlatformGuardAttribute : OSPlatformAttribute
    {
        public UnsupportedOSPlatformGuardAttribute(string platformName) : base(platformName) { }
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Module | AttributeTargets.Class |
        AttributeTargets.Interface | AttributeTargets.Delegate | AttributeTargets.Struct | AttributeTargets.Enum |
        AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field |
        AttributeTargets.Event, Inherited = false)]
    public sealed class RequiresPreviewFeaturesAttribute : Attribute
    {
        public RequiresPreviewFeaturesAttribute() { }
        public RequiresPreviewFeaturesAttribute(string? message) => Message = message;
        public string? Message { get; }
        public string? Url { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor, Inherited = false)]
    public sealed class ResourceConsumptionAttribute : Attribute
    {
        public ResourceConsumptionAttribute(ResourceScope resourceScope)
        {
            ResourceScope = resourceScope;
            ConsumptionScope = resourceScope;
        }

        public ResourceConsumptionAttribute(ResourceScope resourceScope, ResourceScope consumptionScope)
        {
            ResourceScope = resourceScope;
            ConsumptionScope = consumptionScope;
        }

        public ResourceScope ResourceScope { get; }
        public ResourceScope ConsumptionScope { get; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Constructor, Inherited = false)]
    public sealed class ResourceExposureAttribute : Attribute
    {
        public ResourceExposureAttribute(ResourceScope exposureLevel) => ResourceExposureLevel = exposureLevel;
        public ResourceScope ResourceExposureLevel { get; }
    }

    [Flags]
    public enum ResourceScope
    {
        None = 0,
        Machine = 0x1,
        Process = 0x2,
        AppDomain = 0x4,
        Library = 0x8,
        Private = 0x10,
        Assembly = 0x20
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class TargetFrameworkAttribute : Attribute
    {
        public TargetFrameworkAttribute(string frameworkName) => FrameworkName = frameworkName;
        public string FrameworkName { get; }
        public string? FrameworkDisplayName { get; set; }
    }
}
