// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included.

namespace System.Security.Claims
{
    internal static class SR
    {
        internal const string InvalidOperation_ClaimCannotBeRemoved =
            "The Claim '{0}' was not able to be removed.  It is either not part of this Identity or it is a Claim that is owned by the Principal that contains this Identity. For example, the Principal will own the Claim when creating a GenericPrincipal with roles. The roles will be exposed through the Identity that is passed in the constructor, but not actually owned by the Identity.  Similar logic exists for a RolePrincipal.";
        internal const string InvalidOperationException_ActorGraphCircular =
            "An Actor must not create a circular reference between itself (or one of its child Actors) and one of its parents.";
        internal const string PlatformNotSupported_Serialization =
            "This instance contains state that cannot be serialized and deserialized on this platform.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);
    }
}
