// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Reflection
{
    [AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
    public sealed class AssemblySignatureKeyAttribute : Attribute
    {
        public AssemblySignatureKeyAttribute(string publicKey, string countersignature)
        {
            PublicKey = publicKey;
            Countersignature = countersignature;
        }

        public string PublicKey { get; }

        public string Countersignature { get; }
    }
}
