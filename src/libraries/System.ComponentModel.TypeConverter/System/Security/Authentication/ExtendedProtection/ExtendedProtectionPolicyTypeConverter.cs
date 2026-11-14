// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptations: the InstanceDescriptor conversion finds the policy's constructor through
// reflection, which NetWasm does not provide, so CanConvertTo no longer reports it and ConvertTo
// throws PlatformNotSupportedException for it. Upstream's blanket OperatingSystem.IsWasi()
// rejection and the browser UnsupportedOSPlatform annotation are not ported: string and other
// base conversions work on every NetWasm host.

using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace System.Security.Authentication.ExtendedProtection
{
    public class ExtendedProtectionPolicyTypeConverter : TypeConverter
    {
        public override bool CanConvertTo(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? destinationType)
        {
            return base.CanConvertTo(context, destinationType);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(InstanceDescriptor))
            {
                if (value is ExtendedProtectionPolicy)
                {
                    throw new PlatformNotSupportedException(SR.ExtendedProtectionPolicyInstanceDescriptorNotSupported);
                }
            }

            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}
