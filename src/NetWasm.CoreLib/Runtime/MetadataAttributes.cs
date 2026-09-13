// Adapted from dotnet/runtime System.Runtime metadata declarations.
// The upstream implementation is licensed under MIT.

namespace System.Runtime
{
    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class AssemblyTargetedPatchBandAttribute : Attribute
    {
        public string TargetedPatchBand { get; }

        public AssemblyTargetedPatchBandAttribute(string targetedPatchBand)
        {
            TargetedPatchBand = targetedPatchBand;
        }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    public sealed class TargetedPatchingOptOutAttribute : Attribute
    {
        public string Reason { get; }

        public TargetedPatchingOptOutAttribute(string reason)
        {
            Reason = reason;
        }
    }
}
