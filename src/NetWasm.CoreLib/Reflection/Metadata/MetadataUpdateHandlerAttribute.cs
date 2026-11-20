// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Portions adapted from dotnet/runtime System.Private.CoreLib at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Diagnostics.CodeAnalysis;

namespace System.Reflection.Metadata
{
    // Names a type whose ClearCache and UpdateApplication methods hot reload calls. NetWasm applies no updates,
    // so nothing calls them; libraries carry the attribute unchanged.
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class MetadataUpdateHandlerAttribute : Attribute
    {
        public MetadataUpdateHandlerAttribute([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type handlerType) =>
            HandlerType = handlerType;

        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
        public Type HandlerType { get; }
    }
}
