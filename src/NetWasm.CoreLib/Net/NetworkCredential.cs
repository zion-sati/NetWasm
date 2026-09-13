// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace System.Net
{
    /// <summary>
    /// Carries username, password, and domain values for resolver contracts.
    /// </summary>
    /// <remarks>
    /// This profile intentionally retains only the string-based credential
    /// surface. It stores and returns data; it never opens a connection or
    /// selects an authentication transport.
    /// </remarks>
    public class NetworkCredential : ICredentials
    {
        private string _domain = string.Empty;
        private string _password = string.Empty;
        private string _userName = string.Empty;

        public NetworkCredential()
        {
        }

        public NetworkCredential(string? userName, string? password)
            : this(userName, password, string.Empty)
        {
        }

        public NetworkCredential(string? userName, string? password, string? domain)
        {
            UserName = userName;
            Password = password;
            Domain = domain;
        }

        [AllowNull]
        public string UserName
        {
            get => _userName;
            set => _userName = value ?? string.Empty;
        }

        [AllowNull]
        public string Password
        {
            get => _password;
            set => _password = value ?? string.Empty;
        }

        [AllowNull]
        public string Domain
        {
            get => _domain;
            set => _domain = value ?? string.Empty;
        }

        public NetworkCredential GetCredential(Uri? uri, string? authenticationType) => this;

        NetworkCredential? ICredentials.GetCredential(Uri uri, string authType) => this;
    }
}
