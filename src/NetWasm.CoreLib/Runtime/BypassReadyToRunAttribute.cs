// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Copied from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de src/libraries/System.Private.CoreLib/src/System/Runtime/BypassReadyToRunAttribute.cs

namespace System.Runtime
{
    // Use to disable ReadyToRun compilation for a method.
    internal sealed class BypassReadyToRunAttribute : Attribute
    {
    }
}
