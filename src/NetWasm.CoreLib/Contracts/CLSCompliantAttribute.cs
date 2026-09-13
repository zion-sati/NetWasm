// Contract adapted from dotnet/runtime System.Private.CoreLib.
// Licensed under the MIT license; see the upstream repository for the
// complete copyright notice.
namespace System
{
    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = false)]
    public sealed class CLSCompliantAttribute : Attribute
    {
        private readonly bool _compliant;

        public CLSCompliantAttribute(bool isCompliant) => _compliant = isCompliant;

        public bool IsCompliant { get => _compliant; }
    }
}
