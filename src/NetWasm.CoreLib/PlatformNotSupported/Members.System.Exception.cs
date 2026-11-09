// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de reference sources
// by compiler-qualification/netstandard21/pnse/PnseGenerator.cs. NetWasm classifies these APIs as
// intentionally unsupported: they bind, and every member throws PlatformNotSupportedException.

#pragma warning disable CS0067, CS0169, CS0618, CS0809, CS8597, CS8618, CS8625, SYSLIB0050, SYSLIB0051

namespace System
{
    partial class Exception
    {
        public System.Reflection.MethodBase? TargetSite { [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Metadata for the method might be incomplete or removed")] get { throw new global::System.PlatformNotSupportedException("Exception.TargetSite reflection-based member access is not available on this NetWasm platform."); } }
    }
}
