// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.
// NetWasm adaptation: the ML-DSA, ML-KEM and SLH-DSA members are not ported.

using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace System.Security.Cryptography.X509Certificates
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PolicyData
    {
        internal byte[] ApplicationCertPolicies;
        internal byte[] CertPolicies;
        internal byte[] CertPolicyMappings;
        internal byte[] CertPolicyConstraints;
        internal byte[] EnhancedKeyUsage;
        internal byte[] InhibitAnyPolicyExtension;
    }

    /// <summary>Provides specific implementation for X509Certificate2.</summary>
    internal interface ICertificatePal : ICertificatePalCore
    {
        int Version { get; }
        bool Archived { get; set; }
        string FriendlyName { get; set; }
        X500DistinguishedName SubjectName { get; }
        X500DistinguishedName IssuerName { get; }
        IEnumerable<X509Extension> Extensions { get; }
        RSA? GetRSAPrivateKey();
        DSA? GetDSAPrivateKey();
        ECDsa? GetECDsaPrivateKey();
        ECDiffieHellman? GetECDiffieHellmanPrivateKey();
        string GetNameInfo(X509NameType nameType, bool forIssuer);
        void AppendPrivateKeyInfo(StringBuilder sb);
        ICertificatePal CopyWithPrivateKey(DSA privateKey);
        ICertificatePal CopyWithPrivateKey(ECDsa privateKey);
        ICertificatePal CopyWithPrivateKey(RSA privateKey);
        ICertificatePal CopyWithPrivateKey(ECDiffieHellman privateKey);
        PolicyData GetPolicyData();
    }
}
