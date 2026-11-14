// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Net.Security uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Net.Security's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string security_ExtendedProtectionPolicy_NoEmptyServiceNameCollection = "The ServiceNameCollection must contain at least one service name.";
        internal const string security_ExtendedProtectionPolicy_UseDifferentConstructorForNever = "To construct a policy with PolicyEnforcement.Never, the single-parameter constructor must be used.";
    }
}
