// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

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
