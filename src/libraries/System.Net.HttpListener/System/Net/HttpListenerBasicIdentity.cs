// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Security.Principal;

namespace System.Net
{
    public class HttpListenerBasicIdentity : GenericIdentity
    {
        public HttpListenerBasicIdentity(string username, string password) :
            base(username, "Basic")
        {
            Password = password;
        }

        public virtual string Password { get; }
    }
}
