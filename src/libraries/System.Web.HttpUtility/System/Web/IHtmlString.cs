// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Web
{
    /// <summary>Represents an HTML-encoded string that should not be encoded again.</summary>
    public interface IHtmlString
    {
        /// <summary>Returns an HTML-encoded string.</summary>
        /// <returns>An HTML-encoded string.</returns>
        string? ToHtmlString();
    }
}
