// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Diagnostics.CodeAnalysis;

namespace System.Diagnostics
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class SwitchLevelAttribute : Attribute
    {
        private Type _type;

        public SwitchLevelAttribute(Type switchLevelType)
        {
            SwitchLevelType = switchLevelType;
        }

        public Type SwitchLevelType
        {
            get { return _type; }
            [MemberNotNull(nameof(_type))]
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                _type = value;
            }
        }
    }
}
