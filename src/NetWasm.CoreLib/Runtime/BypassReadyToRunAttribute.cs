// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Copied from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce src/libraries/System.Private.CoreLib/src/System/Runtime/BypassReadyToRunAttribute.cs

namespace System.Runtime
{
    // Use to disable ReadyToRun compilation for a method.
    internal sealed class BypassReadyToRunAttribute : Attribute
    {
    }
}
