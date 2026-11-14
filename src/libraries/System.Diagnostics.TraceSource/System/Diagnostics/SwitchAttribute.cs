// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: GetAll keeps its argument check and then throws PlatformNotSupportedException.
// Assembly-wide switch discovery walks runtime attributes and members, and NetWasm has no reflection.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace System.Diagnostics
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Constructor |
                    AttributeTargets.Event | AttributeTargets.Method | AttributeTargets.Property)]
    public sealed class SwitchAttribute : Attribute
    {
        private Type _type;
        private string _name;

        public SwitchAttribute(string switchName, Type switchType)
        {
            SwitchName = switchName;
            SwitchType = switchType;
        }

        public string SwitchName
        {
            get { return _name; }
            [MemberNotNull(nameof(_name))]
            set
            {
                ArgumentException.ThrowIfNullOrEmpty(value);

                _name = value;
            }
        }

        public Type SwitchType
        {
            get { return _type; }
            [MemberNotNull(nameof(_type))]
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _type = value;
            }
        }

        public string? SwitchDescription { get; set; }

        [RequiresUnreferencedCode("Types may be trimmed from the assembly.")]
        public static SwitchAttribute[] GetAll(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);

            throw new PlatformNotSupportedException("Assembly-wide switch discovery requires reflection, which NetWasm does not support.");
        }
    }
}
