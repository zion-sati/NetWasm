// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Reflection
{
    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class AssemblyFlagsAttribute : Attribute
    {
        private readonly AssemblyNameFlags _flags;

        [Obsolete("This constructor has been deprecated. Use AssemblyFlagsAttribute(AssemblyNameFlags) instead.")]
        [CLSCompliant(false)]
        public AssemblyFlagsAttribute(uint flags)
        {
            _flags = (AssemblyNameFlags)flags;
        }

        [Obsolete("AssemblyFlagsAttribute.Flags has been deprecated. Use AssemblyFlags instead.")]
        [CLSCompliant(false)]
        public uint Flags => (uint)_flags;

        public int AssemblyFlags => (int)_flags;

        [Obsolete("This constructor has been deprecated. Use AssemblyFlagsAttribute(AssemblyNameFlags) instead.")]
        public AssemblyFlagsAttribute(int assemblyFlags)
        {
            _flags = (AssemblyNameFlags)assemblyFlags;
        }

        public AssemblyFlagsAttribute(AssemblyNameFlags assemblyFlags)
        {
            _flags = assemblyFlags;
        }
    }
}
