// Adapted from dotnet/runtime System.Security metadata declarations.
// The upstream implementation is licensed under MIT.

namespace System.Security
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class AllowPartiallyTrustedCallersAttribute : Attribute
    {
        public AllowPartiallyTrustedCallersAttribute() { }
        public PartialTrustVisibilityLevel PartialTrustVisibilityLevel { get; set; }
    }

    public enum PartialTrustVisibilityLevel
    {
        VisibleToAllHosts = 0,
        NotVisibleByDefault = 1
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct |
        AttributeTargets.Enum | AttributeTargets.Constructor | AttributeTargets.Method |
        AttributeTargets.Field | AttributeTargets.Interface | AttributeTargets.Delegate,
        AllowMultiple = false, Inherited = false)]
    public sealed class SecurityCriticalAttribute : Attribute
    {
        public SecurityCriticalAttribute() { }
        public SecurityCriticalAttribute(SecurityCriticalScope scope) => Scope = scope;
        public SecurityCriticalScope Scope { get; }
    }

    public enum SecurityCriticalScope
    {
        Explicit = 0,
        Everything = 0x1
    }

    public enum SecurityRuleSet : byte
    {
        None = 0,
        Level1 = 1,
        Level2 = 2
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class SecurityRulesAttribute : Attribute
    {
        public SecurityRulesAttribute(SecurityRuleSet ruleSet) => RuleSet = ruleSet;
        public bool SkipVerificationInFullTrust { get; set; }
        public SecurityRuleSet RuleSet { get; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum |
        AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Field |
        AttributeTargets.Interface | AttributeTargets.Delegate, AllowMultiple = false, Inherited = false)]
    public sealed class SecuritySafeCriticalAttribute : Attribute
    {
        public SecuritySafeCriticalAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class SecurityTransparentAttribute : Attribute
    {
        public SecurityTransparentAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct |
        AttributeTargets.Enum | AttributeTargets.Constructor | AttributeTargets.Method |
        AttributeTargets.Field | AttributeTargets.Interface | AttributeTargets.Delegate,
        AllowMultiple = false, Inherited = false)]
    public sealed class SecurityTreatAsSafeAttribute : Attribute
    {
        public SecurityTreatAsSafeAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Interface |
        AttributeTargets.Delegate, AllowMultiple = true, Inherited = false)]
    public sealed class SuppressUnmanagedCodeSecurityAttribute : Attribute
    {
        public SuppressUnmanagedCodeSecurityAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Module, AllowMultiple = true, Inherited = false)]
    public sealed class UnverifiableCodeAttribute : Attribute
    {
        public UnverifiableCodeAttribute() { }
    }
}
