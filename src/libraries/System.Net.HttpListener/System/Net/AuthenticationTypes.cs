// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Net
{
    internal static class AuthenticationTypes
    {
        internal const string NTLM = "NTLM";
        internal const string Negotiate = "Negotiate";
        internal const string Basic = "Basic";
    }
}
