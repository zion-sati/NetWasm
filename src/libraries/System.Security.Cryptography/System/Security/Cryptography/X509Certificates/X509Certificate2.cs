// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit 811225a482702af7ecc35d817966bc70b88a3a23.
// NetWasm adaptation: The ML-KEM, ML-DSA, SLH-DSA and Composite ML-DSA members, and
// MatchesHostname, are not ported. CreateFromPem/CreateFromPemFile/CreateFromEncryptedPem/
// CreateFromEncryptedPemFile and their key-extraction helpers are upstream's (dotnet/runtime
// deae2e32), loading the certificate through the constructor since X509CertificateLoader is not
// ported, and without the post-quantum key types; a certificate with one fails as an unknown key
// algorithm. CreateCustomExtensionIfAny maps only the extension types NetWasm declares: Basic
// Constraints, Key Usage, Enhanced Key Usage and Subject Key Identifier.

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Formats.Asn1;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Versioning;
using System.Security.Cryptography.Asn1;
using System.Security.Cryptography.X509Certificates.Asn1;
using System.Text;
using Internal.Cryptography;

namespace System.Security.Cryptography.X509Certificates
{
    public class X509Certificate2 : X509Certificate
    {
        private static readonly string[] s_RsaPublicKeyPrivateKeyLabels = [PemLabels.RsaPrivateKey, PemLabels.Pkcs8PrivateKey];
        private static readonly string[] s_DsaPublicKeyPrivateKeyLabels = [PemLabels.Pkcs8PrivateKey];

        private Oid? _lazySignatureAlgorithm;
        private int _lazyVersion;
        private X500DistinguishedName? _lazySubjectName;
        private X500DistinguishedName? _lazyIssuerName;
        private PublicKey? _lazyPublicKey;
        private AsymmetricAlgorithm? _lazyPrivateKey;
        private X509ExtensionCollection? _lazyExtensions;

        public override void Reset()
        {
            _lazySignatureAlgorithm = null;
            _lazyVersion = 0;
            _lazySubjectName = null;
            _lazyIssuerName = null;
            _lazyPublicKey = null;
            _lazyPrivateKey = null;
            _lazyExtensions = null;

            base.Reset();
        }

        [Obsolete(Obsoletions.X509CertificateImmutableMessage, DiagnosticId = Obsoletions.X509CertificateImmutableDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        [UnsupportedOSPlatform("browser")]
        public X509Certificate2()
            : base()
        {
        }

        [UnsupportedOSPlatform("browser")]
        [Obsolete(Obsoletions.X509CtorCertDataObsoleteMessage, DiagnosticId = Obsoletions.X509CtorCertDataObsoleteDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public X509Certificate2(byte[] rawData)
            : base(rawData)
        {
        }

        [UnsupportedOSPlatform("browser")]
        [Obsolete(Obsoletions.X509CtorCertDataObsoleteMessage, DiagnosticId = Obsoletions.X509CtorCertDataObsoleteDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public X509Certificate2(byte[] rawData, string? password)
            : base(rawData, password)
        {
        }

        [UnsupportedOSPlatform("browser")]
        [CLSCompliantAttribute(false)]
        [Obsolete(Obsoletions.X509CtorCertDataObsoleteMessage, DiagnosticId = Obsoletions.X509CtorCertDataObsoleteDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public X509Certificate2(byte[] rawData, SecureString? password)
            : base(rawData, password)
        {
        }

        [UnsupportedOSPlatform("browser")]
        [Obsolete(Obsoletions.X509CtorCertDataObsoleteMessage, DiagnosticId = Obsoletions.X509CtorCertDataObsoleteDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public X509Certificate2(byte[] rawData, string? password, X509KeyStorageFlags keyStorageFlags)
            : base(rawData, password, keyStorageFlags)
        {
        }

        [UnsupportedOSPlatform("browser")]
        [CLSCompliantAttribute(false)]
        [Obsolete(Obsoletions.X509CtorCertDataObsoleteMessage, DiagnosticId = Obsoletions.X509CtorCertDataObsoleteDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public X509Certificate2(byte[] rawData, SecureString? password, X509KeyStorageFlags keyStorageFlags)
            : base(rawData, password, keyStorageFlags)
        {
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref="X509Certificate2"/> class from certificate data.
        /// </summary>
        /// <param name="rawData">
        ///   The certificate data to process.
        /// </param>
        /// <exception cref="CryptographicException">An error with the certificate occurs.</exception>
        [UnsupportedOSPlatform("browser")]
        [Obsolete(Obsoletions.X509CtorCertDataObsoleteMessage, DiagnosticId = Obsoletions.X509CtorCertDataObsoleteDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public X509Certificate2(ReadOnlySpan<byte> rawData)
            : base(rawData)
        {
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref="X509Certificate2"/> class from certificate data,
        ///   a password, and key storage flags.
        /// </summary>
        /// <param name="rawData">
        ///   The certificate data to process.
        /// </param>
        /// <param name="password">
        ///   The password required to access the certificate data.
        /// </param>
        /// <param name="keyStorageFlags">
        ///   A bitwise combination of the enumeration values that control where and how to import the certificate.
        /// </param>
        /// <exception cref="CryptographicException">An error with the certificate occurs.</exception>
        [UnsupportedOSPlatform("browser")]
        [Obsolete(Obsoletions.X509CtorCertDataObsoleteMessage, DiagnosticId = Obsoletions.X509CtorCertDataObsoleteDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public X509Certificate2(ReadOnlySpan<byte> rawData, ReadOnlySpan<char> password, X509KeyStorageFlags keyStorageFlags = 0)
            : base(rawData, password, keyStorageFlags)
        {
        }

        [UnsupportedOSPlatform("browser")]
        public X509Certificate2(IntPtr handle)
            : base(handle)
        {
        }

        internal X509Certificate2(ICertificatePal pal)
            : base(pal)
        {
        }

        [UnsupportedOSPlatform("browser")]
        [Obsolete(Obsoletions.X509CtorCertDataObsoleteMessage, DiagnosticId = Obsoletions.X509CtorCertDataObsoleteDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public X509Certificate2(string fileName)
            : base(fileName)
        {
        }

        [UnsupportedOSPlatform("browser")]
        [Obsolete(Obsoletions.X509CtorCertDataObsoleteMessage, DiagnosticId = Obsoletions.X509CtorCertDataObsoleteDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public X509Certificate2(string fileName, string? password)
            : base(fileName, password)
        {
        }

        [UnsupportedOSPlatform("browser")]
        [CLSCompliantAttribute(false)]
        [Obsolete(Obsoletions.X509CtorCertDataObsoleteMessage, DiagnosticId = Obsoletions.X509CtorCertDataObsoleteDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public X509Certificate2(string fileName, SecureString? password)
            : base(fileName, password)
        {
        }

        [UnsupportedOSPlatform("browser")]
        [Obsolete(Obsoletions.X509CtorCertDataObsoleteMessage, DiagnosticId = Obsoletions.X509CtorCertDataObsoleteDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public X509Certificate2(string fileName, string? password, X509KeyStorageFlags keyStorageFlags)
            : base(fileName, password, keyStorageFlags)
        {
        }

        [UnsupportedOSPlatform("browser")]
        [CLSCompliantAttribute(false)]
        [Obsolete(Obsoletions.X509CtorCertDataObsoleteMessage, DiagnosticId = Obsoletions.X509CtorCertDataObsoleteDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public X509Certificate2(string fileName, SecureString? password, X509KeyStorageFlags keyStorageFlags)
            : base(fileName, password, keyStorageFlags)
        {
        }

        [UnsupportedOSPlatform("browser")]
        [Obsolete(Obsoletions.X509CtorCertDataObsoleteMessage, DiagnosticId = Obsoletions.X509CtorCertDataObsoleteDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public X509Certificate2(string fileName, ReadOnlySpan<char> password, X509KeyStorageFlags keyStorageFlags = 0)
            : base(fileName, password, keyStorageFlags)
        {
        }

        [UnsupportedOSPlatform("browser")]
        public X509Certificate2(X509Certificate certificate)
            : base(certificate)
        {
        }

        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected X509Certificate2(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            throw new PlatformNotSupportedException();
        }

        internal new ICertificatePal Pal => (ICertificatePal)base.Pal!; // called base ctors guaranteed to initialize

        public bool Archived
        {
            get
            {
                ThrowIfInvalid();

                return Pal.Archived;
            }

            [SupportedOSPlatform("windows")]
            set
            {
                ThrowIfInvalid();

                Pal.Archived = value;
            }
        }

        public X509ExtensionCollection Extensions
        {
            get
            {
                ThrowIfInvalid();

                X509ExtensionCollection? extensions = _lazyExtensions;
                if (extensions == null)
                {
                    extensions = new X509ExtensionCollection();
                    foreach (X509Extension extension in Pal.Extensions)
                    {
                        X509Extension? customExtension = CreateCustomExtensionIfAny(extension.Oid!);
                        if (customExtension == null)
                        {
                            extensions.Add(extension);
                        }
                        else
                        {
                            customExtension.CopyFrom(extension);
                            extensions.Add(customExtension);
                        }
                    }
                    _lazyExtensions = extensions;
                }
                return extensions;
            }
        }

        public string FriendlyName
        {
            get
            {
                ThrowIfInvalid();

                return Pal.FriendlyName;
            }

            [SupportedOSPlatform("windows")]
            set
            {
                ThrowIfInvalid();

                Pal.FriendlyName = value;
            }
        }

        public bool HasPrivateKey
        {
            get
            {
                ThrowIfInvalid();

                return Pal.HasPrivateKey;
            }
        }

        [Obsolete(Obsoletions.X509CertificatePrivateKeyMessage, DiagnosticId = Obsoletions.X509CertificatePrivateKeyDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public AsymmetricAlgorithm? PrivateKey
        {
            get
            {
                ThrowIfInvalid();

                if (!HasPrivateKey)
                    return null;

                _lazyPrivateKey ??= GetKeyAlgorithm() switch
                {
                    Oids.Rsa => Pal.GetRSAPrivateKey(),
                    Oids.Dsa => Pal.GetDSAPrivateKey(),

                    // This includes ECDSA, because an Oids.EcPublicKey key can be
                    // many different algorithm kinds, not necessarily with mutual exclusion.
                    // Plus, .NET Framework only supports RSA and DSA in this property.
                    _ => throw new NotSupportedException(SR.NotSupported_KeyAlgorithm),
                };

                return _lazyPrivateKey;
            }
            set
            {
                throw new PlatformNotSupportedException();
            }
        }

        public X500DistinguishedName IssuerName
        {
            get
            {
                ThrowIfInvalid();

                return _lazyIssuerName ??= Pal.IssuerName;
            }
        }

        public DateTime NotAfter => GetNotAfter();
        public DateTime NotBefore => GetNotBefore();

        public PublicKey PublicKey
        {
            get
            {
                ThrowIfInvalid();

                PublicKey? publicKey = _lazyPublicKey;

                if (publicKey == null)
                {
                    string keyAlgorithmOid = GetKeyAlgorithm();
                    byte[]? parameters = Pal.KeyAlgorithmParameters;
                    byte[] keyValue = Pal.PublicKeyValue;
                    Oid oid = new Oid(keyAlgorithmOid);
                    // PublicKey can use skipCopy because AsnEncodedData creates a defensive copy of the values.
                    publicKey = _lazyPublicKey = new PublicKey(
                        oid,
                        parameters is null ? null : new AsnEncodedData(oid, parameters),
                        new AsnEncodedData(oid, keyValue),
                        skipCopy: true);
                }

                return publicKey;
            }
        }

        public byte[] RawData => RawDataMemory.ToArray();

        /// <summary>
        /// Gets the raw data of a certificate.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="RawData" />, this does not create a fresh copy of the data
        /// every time.
        /// </remarks>
        public ReadOnlyMemory<byte> RawDataMemory => PalRawDataMemory;

        public string SerialNumber => GetSerialNumberString();

        public Oid SignatureAlgorithm
        {
            get
            {
                ThrowIfInvalid();

                return _lazySignatureAlgorithm ??= new Oid(Pal.SignatureAlgorithm, null);
            }
        }

        public X500DistinguishedName SubjectName
        {
            get
            {
                ThrowIfInvalid();

                return _lazySubjectName ??= Pal.SubjectName;
            }
        }

        public string Thumbprint
        {
            get
            {
                return GetCertHashString();
            }
        }

        public int Version
        {
            get
            {
                ThrowIfInvalid();

                int version = _lazyVersion;
                if (version == 0)
                    version = _lazyVersion = Pal.Version;
                return version;
            }
        }

        [UnsupportedOSPlatform("browser")]
        public static X509ContentType GetCertContentType(byte[] rawData)
        {
            if (rawData == null || rawData.Length == 0)
                throw new ArgumentException(SR.Arg_EmptyOrNullArray, nameof(rawData));

            return X509Pal.Instance.GetCertContentType(rawData);
        }

        /// <summary>
        ///   Indicates the type of certificate contained in the provided data.
        /// </summary>
        /// <param name="rawData">
        ///   The data to identify.
        /// </param>
        /// <returns>
        ///   One of the enumeration values that indicate the content type of the provided data.
        /// </returns>
        [UnsupportedOSPlatform("browser")]
        public static X509ContentType GetCertContentType(ReadOnlySpan<byte> rawData)
        {
            if (rawData.Length == 0)
                throw new ArgumentException(SR.Arg_EmptyOrNullArray, nameof(rawData));

            return X509Pal.Instance.GetCertContentType(rawData);
        }

        [UnsupportedOSPlatform("browser")]
        public static X509ContentType GetCertContentType(string fileName)
        {
            ArgumentNullException.ThrowIfNull(fileName);

            // .NET Framework compat: The .NET Framework expands the filename to a full path for the purpose of performing a CAS permission check. While CAS is not present here,
            // we still need to call GetFullPath() so we get the same exception behavior if the fileName is bad.
            _ = Path.GetFullPath(fileName);

            return X509Pal.Instance.GetCertContentType(fileName);
        }

        public string GetNameInfo(X509NameType nameType, bool forIssuer)
        {
            return Pal.GetNameInfo(nameType, forIssuer);
        }

        public override string ToString() => base.ToString(fVerbose: true);

        public override string ToString(bool verbose)
        {
            if (!verbose || Pal == null)
                return ToString();

            StringBuilder sb = new StringBuilder();

            // Version
            sb.AppendLine("[Version]");
            sb.Append("  V");
            sb.Append(Version);

            // Subject
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("[Subject]");
            sb.Append("  ");
            sb.Append(SubjectName.Name);
            string simpleName = GetNameInfo(X509NameType.SimpleName, false);
            if (simpleName.Length > 0)
            {
                sb.AppendLine();
                sb.Append("  ");
                sb.Append("Simple Name: ");
                sb.Append(simpleName);
            }
            string emailName = GetNameInfo(X509NameType.EmailName, false);
            if (emailName.Length > 0)
            {
                sb.AppendLine();
                sb.Append("  ");
                sb.Append("Email Name: ");
                sb.Append(emailName);
            }
            string upnName = GetNameInfo(X509NameType.UpnName, false);
            if (upnName.Length > 0)
            {
                sb.AppendLine();
                sb.Append("  ");
                sb.Append("UPN Name: ");
                sb.Append(upnName);
            }
            string dnsName = GetNameInfo(X509NameType.DnsName, false);
            if (dnsName.Length > 0)
            {
                sb.AppendLine();
                sb.Append("  ");
                sb.Append("DNS Name: ");
                sb.Append(dnsName);
            }

            // Issuer
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("[Issuer]");
            sb.Append("  ");
            sb.Append(IssuerName.Name);
            simpleName = GetNameInfo(X509NameType.SimpleName, true);
            if (simpleName.Length > 0)
            {
                sb.AppendLine();
                sb.Append("  ");
                sb.Append("Simple Name: ");
                sb.Append(simpleName);
            }
            emailName = GetNameInfo(X509NameType.EmailName, true);
            if (emailName.Length > 0)
            {
                sb.AppendLine();
                sb.Append("  ");
                sb.Append("Email Name: ");
                sb.Append(emailName);
            }
            upnName = GetNameInfo(X509NameType.UpnName, true);
            if (upnName.Length > 0)
            {
                sb.AppendLine();
                sb.Append("  ");
                sb.Append("UPN Name: ");
                sb.Append(upnName);
            }
            dnsName = GetNameInfo(X509NameType.DnsName, true);
            if (dnsName.Length > 0)
            {
                sb.AppendLine();
                sb.Append("  ");
                sb.Append("DNS Name: ");
                sb.Append(dnsName);
            }

            // Serial Number
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("[Serial Number]");
            sb.Append("  ");
            sb.AppendLine(SerialNumber);

            // NotBefore
            sb.AppendLine();
            sb.AppendLine("[Not Before]");
            sb.Append("  ");
            sb.AppendLine(FormatDate(NotBefore));

            // NotAfter
            sb.AppendLine();
            sb.AppendLine("[Not After]");
            sb.Append("  ");
            sb.AppendLine(FormatDate(NotAfter));

            // Thumbprint
            sb.AppendLine();
            sb.AppendLine("[Thumbprint]");
            sb.Append("  ");
            sb.AppendLine(Thumbprint);

            // Signature Algorithm
            sb.AppendLine();
            sb.AppendLine("[Signature Algorithm]");
            sb.Append("  ");
            sb.Append(SignatureAlgorithm.FriendlyName);
            sb.Append('(');
            sb.Append(SignatureAlgorithm.Value);
            sb.AppendLine(")");

            // Public Key
            sb.AppendLine();
            sb.Append("[Public Key]");
            // It could throw if it's some user-defined CryptoServiceProvider
            try
            {
                PublicKey pubKey = PublicKey;

                sb.AppendLine();
                sb.Append("  ");
                sb.Append("Algorithm: ");
                sb.Append(pubKey.Oid.FriendlyName);
                // So far, we only support RSACryptoServiceProvider & DSACryptoServiceProvider Keys
                try
                {
                    sb.AppendLine();
                    sb.Append("  ");
                    sb.Append("Length: ");

                    using (RSA? pubRsa = this.GetRSAPublicKey())
                    {
                        if (pubRsa != null)
                        {
                            sb.Append(pubRsa.KeySize);
                        }
                    }
                }
                catch (NotSupportedException)
                {
                }

                sb.AppendLine();
                sb.Append("  ");
                sb.Append("Key Blob: ");
                sb.AppendLine(pubKey.EncodedKeyValue.Format(true));

                sb.Append("  ");
                sb.Append("Parameters: ");

                if (pubKey.EncodedParameters is AsnEncodedData parameters)
                {
                    sb.Append(parameters.Format(true));
                }
            }
            catch (CryptographicException)
            {
            }

            // Private key
            Pal.AppendPrivateKeyInfo(sb);

            // Extensions
            X509ExtensionCollection extensions = Extensions;
            if (extensions.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.Append("[Extensions]");
                foreach (X509Extension extension in extensions)
                {
                    try
                    {
                        sb.AppendLine();
                        sb.Append("* ");
                        sb.Append(extension.Oid!.FriendlyName);
                        sb.Append('(');
                        sb.Append(extension.Oid.Value);
                        sb.Append("):");

                        sb.AppendLine();
                        sb.Append("  ");
                        sb.Append(extension.Format(true));
                    }
                    catch (CryptographicException)
                    {
                    }
                }
            }

            sb.AppendLine();
            return sb.ToString();
        }

        [Obsolete(Obsoletions.X509CertificateImmutableMessage, DiagnosticId = Obsoletions.X509CertificateImmutableDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public override void Import(byte[] rawData)
        {
            base.Import(rawData);
        }

        [Obsolete(Obsoletions.X509CertificateImmutableMessage, DiagnosticId = Obsoletions.X509CertificateImmutableDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public override void Import(byte[] rawData, string? password, X509KeyStorageFlags keyStorageFlags)
        {
            base.Import(rawData, password, keyStorageFlags);
        }

        [System.CLSCompliantAttribute(false)]
        [Obsolete(Obsoletions.X509CertificateImmutableMessage, DiagnosticId = Obsoletions.X509CertificateImmutableDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public override void Import(byte[] rawData, SecureString? password, X509KeyStorageFlags keyStorageFlags)
        {
            base.Import(rawData, password, keyStorageFlags);
        }

        [Obsolete(Obsoletions.X509CertificateImmutableMessage, DiagnosticId = Obsoletions.X509CertificateImmutableDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public override void Import(string fileName)
        {
            base.Import(fileName);
        }

        [Obsolete(Obsoletions.X509CertificateImmutableMessage, DiagnosticId = Obsoletions.X509CertificateImmutableDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public override void Import(string fileName, string? password, X509KeyStorageFlags keyStorageFlags)
        {
            base.Import(fileName, password, keyStorageFlags);
        }

        [System.CLSCompliantAttribute(false)]
        [Obsolete(Obsoletions.X509CertificateImmutableMessage, DiagnosticId = Obsoletions.X509CertificateImmutableDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public override void Import(string fileName, SecureString? password, X509KeyStorageFlags keyStorageFlags)
        {
            base.Import(fileName, password, keyStorageFlags);
        }

        public bool Verify()
        {
            ThrowIfInvalid();

            using (var chain = new X509Chain())
            {
                // Use the default vales of chain.ChainPolicy including:
                //  RevocationMode = X509RevocationMode.Online
                //  RevocationFlag = X509RevocationFlag.ExcludeRoot
                //  VerificationFlags = X509VerificationFlags.NoFlag
                //  VerificationTime = DateTime.Now
                //  UrlRetrievalTimeout = new TimeSpan(0, 0, 0)

                bool verified = chain.Build(this, throwOnException: false);

                for (int i = 0; i < chain.ChainElements.Count; i++)
                {
                    chain.ChainElements[i].Certificate.Dispose();
                }

                return verified;
            }
        }

        /// <summary>
        /// Gets the <see cref="ECDiffieHellman" /> public key from this certificate.
        /// </summary>
        /// <returns>
        /// The public key, or <see langword="null" /> if this certificate does not have
        /// an ECDiffieHellman public key.
        /// </returns>
        /// <exception cref="CryptographicException">
        /// The handle is invalid.
        /// </exception>
        public ECDiffieHellman? GetECDiffieHellmanPublicKey()
        {
            return this.GetPublicKey<ECDiffieHellman>(HasECDiffieHellmanKeyUsage);
        }

        /// <summary>
        /// Gets the <see cref="ECDiffieHellman" /> private key from this certificate.
        /// </summary>
        /// <returns>
        /// The private key, or <see langword="null" /> if this certificate does not have
        /// an ECDiffieHellman private key.
        /// </returns>
        /// <exception cref="CryptographicException">
        /// The handle is invalid.
        /// </exception>
        public ECDiffieHellman? GetECDiffieHellmanPrivateKey()
        {
            return this.GetPrivateKey<ECDiffieHellman>(HasECDiffieHellmanKeyUsage);
        }

        /// <summary>
        /// Combines a private key with the public key of an <see cref="ECDiffieHellman" />
        /// certificate to generate a new ECDiffieHellman certificate.
        /// </summary>
        /// <param name="privateKey">The private ECDiffieHellman key.</param>
        /// <returns>
        /// A new ECDiffieHellman certificate with the <see cref="HasPrivateKey" /> property set to <see langword="true"/>.
        /// The current certificate isn't modified.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="privateKey" /> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The certificate already has an associated private key.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <para>
        ///   The certificate doesn't have a public key.
        /// </para>
        /// <para> -or- </para>
        /// <para>
        ///   The specified private key doesn't match the public key for this certificate.
        /// </para>
        /// </exception>
        public X509Certificate2 CopyWithPrivateKey(ECDiffieHellman privateKey)
        {
            ArgumentNullException.ThrowIfNull(privateKey);

            if (HasPrivateKey)
                throw new InvalidOperationException(SR.Cryptography_Cert_AlreadyHasPrivateKey);

            using (ECDiffieHellman? publicKey = GetECDiffieHellmanPublicKey())
            {
                if (publicKey is null)
                {
                    throw new ArgumentException(SR.Cryptography_PrivateKey_WrongAlgorithm);
                }

                if (!Helpers.AreSamePublicECParameters(publicKey.ExportParameters(false), privateKey.ExportParameters(false)))
                {
                    throw new ArgumentException(SR.Cryptography_PrivateKey_DoesNotMatch, nameof(privateKey));
                }
            }

            ICertificatePal pal = Pal.CopyWithPrivateKey(privateKey);
            return new X509Certificate2(pal);
        }

        /// <summary>
        /// Exports the public X.509 certificate, encoded as PEM.
        /// </summary>
        /// <returns>
        /// The PEM encoding of the certificate.
        /// </returns>
        /// <exception cref="CryptographicException">
        /// The certificate is corrupt, in an invalid state, or could not be exported
        /// to PEM.
        /// </exception>
        /// <remarks>
        /// <p>
        ///   A PEM-encoded X.509 certificate will begin with <c>-----BEGIN CERTIFICATE-----</c>
        ///   and end with <c>-----END CERTIFICATE-----</c>, with the base64 encoded DER
        ///   contents of the certificate between the PEM boundaries.
        /// </p>
        /// <p>
        ///   The certificate is encoded according to the IETF RFC 7468 &quot;strict&quot;
        ///   encoding rules.
        /// </p>
        /// </remarks>
        public string ExportCertificatePem()
        {
            return PemEncoding.WriteString(PemLabels.X509Certificate, RawDataMemory.Span);
        }

        /// <summary>
        /// Attempts to export the public X.509 certificate, encoded as PEM.
        /// </summary>
        /// <param name="destination">The buffer to receive the PEM encoded certificate.</param>
        /// <param name="charsWritten">When this method returns, the total number of characters written to <paramref name="destination" />.</param>
        /// <returns>
        ///   <see langword="true"/> if <paramref name="destination"/> was large enough to receive the encoded PEM;
        ///   otherwise, <see langword="false" />.
        /// </returns>
        /// <exception cref="CryptographicException">
        /// The certificate is corrupt, in an invalid state, or could not be exported
        /// to PEM.
        /// </exception>
        /// <remarks>
        /// <p>
        ///   A PEM-encoded X.509 certificate will begin with <c>-----BEGIN CERTIFICATE-----</c>
        ///   and end with <c>-----END CERTIFICATE-----</c>, with the base64 encoded DER
        ///   contents of the certificate between the PEM boundaries.
        /// </p>
        /// <p>
        ///   The certificate is encoded according to the IETF RFC 7468 &quot;strict&quot;
        ///   encoding rules.
        /// </p>
        /// </remarks>
        public bool TryExportCertificatePem(Span<char> destination, out int charsWritten)
        {
            return PemEncoding.TryWrite(PemLabels.X509Certificate, RawDataMemory.Span, destination, out charsWritten);
        }

        private static X509Extension? CreateCustomExtensionIfAny(Oid oid) =>
            CreateCustomExtensionIfAny(oid.Value);

        internal static X509Extension? CreateCustomExtensionIfAny(string? oidValue) =>
            oidValue switch
            {
                Oids.BasicConstraints => LegacyBasicConstraintsDecoder.IsSupported ? new X509BasicConstraintsExtension() : null,
                Oids.BasicConstraints2 => new X509BasicConstraintsExtension(),
                Oids.KeyUsage => new X509KeyUsageExtension(),
                Oids.EnhancedKeyUsage => new X509EnhancedKeyUsageExtension(),
                Oids.SubjectKeyIdentifier => new X509SubjectKeyIdentifierExtension(),
                _ => null,
            };

        private static bool HasECDiffieHellmanKeyUsage(X509Certificate2 certificate)
        {
            foreach (X509Extension extension in certificate.Extensions)
            {
                if (extension.Oid?.Value == Oids.KeyUsage && extension is X509KeyUsageExtension ext)
                {
                    // keyAgreement is mandatory for id-ecPublicKey certificates
                    // when used with ECDH.
                    return ((ext.KeyUsages & X509KeyUsageFlags.KeyAgreement) != 0);
                }
            }

            // If the key usage extension is not present in the certificate it is
            // considered valid for all usages, so we can use it for ECDH.
            return true;
        }


        /// <summary>
        /// Creates a new X509 certificate from the file contents of an RFC 7468 PEM-encoded
        /// certificate and private key.
        /// </summary>
        /// <param name="certPemFilePath">The path for the PEM-encoded X509 certificate.</param>
        /// <param name="keyPemFilePath">
        /// If specified, the path for the PEM-encoded private key.
        /// If unspecified, the <paramref name="certPemFilePath" /> file will be used to load
        /// the private key.
        /// </param>
        /// <returns>A new certificate with the private key.</returns>
        /// <exception cref="CryptographicException">
        /// <para>
        ///   The contents of the file path in <paramref name="certPemFilePath" /> do not contain
        ///   a PEM-encoded certificate, or it is malformed.
        /// </para>
        /// <para>-or-</para>
        /// <para>
        ///   The contents of the file path in <paramref name="keyPemFilePath" /> do not contain a
        ///   PEM-encoded private key, or it is malformed.
        /// </para>
        /// <para>-or-</para>
        /// <para>
        ///   The contents of the file path in <paramref name="keyPemFilePath" /> contains
        ///   a key that does not match the public key in the certificate.
        /// </para>
        /// <para>-or-</para>
        /// <para>The certificate uses an unknown public key algorithm.</para>
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="certPemFilePath" /> is <see langword="null" />.
        /// </exception>
        /// <remarks>
        /// <para>
        /// See <see cref="System.IO.File.ReadAllText(string)" /> for additional documentation about
        /// exceptions that can be thrown.
        /// </para>
        /// <para>
        ///   The SubjectPublicKeyInfo from the certificate determines what PEM labels are accepted for the private key.
        ///   For RSA certificates, accepted private key PEM labels are "RSA PRIVATE KEY" and "PRIVATE KEY".
        ///   For ECDSA certificates, accepted private key PEM labels are "EC PRIVATE KEY" and "PRIVATE KEY".
        ///   For DSA certificates, the accepted private key PEM label is "PRIVATE KEY".
        /// </para>
        /// <para>PEM-encoded items that have a different label are ignored.</para>
        /// <para>
        ///   Combined PEM-encoded certificates and keys do not require a specific order. For the certificate, the
        ///   the first certificate with a CERTIFICATE label is loaded. For the private key, the first private
        ///   key with an acceptable label is loaded. More advanced scenarios for loading certificates and
        ///   private keys can leverage <see cref="System.Security.Cryptography.PemEncoding" /> to enumerate
        ///   PEM-encoded values and apply any custom loading behavior.
        /// </para>
        /// <para>
        /// For password protected PEM-encoded keys, use <see cref="CreateFromEncryptedPemFile" /> to specify a password.
        /// </para>
        /// </remarks>
        [UnsupportedOSPlatform("browser")]
        public static X509Certificate2 CreateFromPemFile(string certPemFilePath, string? keyPemFilePath = default)
        {
            ArgumentNullException.ThrowIfNull(certPemFilePath);

            ReadOnlySpan<char> certContents = File.ReadAllText(certPemFilePath);
            ReadOnlySpan<char> keyContents = keyPemFilePath is null ? certContents : File.ReadAllText(keyPemFilePath);

            return CreateFromPem(certContents, keyContents);
        }

        /// <summary>
        /// Creates a new X509 certificate from the file contents of an RFC 7468 PEM-encoded
        /// certificate and password protected private key.
        /// </summary>
        /// <param name="certPemFilePath">The path for the PEM-encoded X509 certificate.</param>
        /// <param name="keyPemFilePath">
        /// If specified, the path for the password protected PEM-encoded private key.
        /// If unspecified, the <paramref name="certPemFilePath" /> file will be used to load
        /// the private key.
        /// </param>
        /// <param name="password">The password for the encrypted PEM.</param>
        /// <returns>A new certificate with the private key.</returns>
        /// <exception cref="CryptographicException">
        /// <para>
        ///   The contents of the file path in <paramref name="certPemFilePath" /> do not contain
        ///   a PEM-encoded certificate, or it is malformed.
        /// </para>
        /// <para>-or-</para>
        /// <para>
        ///   The contents of the file path in <paramref name="keyPemFilePath" /> do not contain a
        ///   password protected PEM-encoded private key, or it is malformed.
        /// </para>
        /// <para>-or-</para>
        /// <para>
        ///   The contents of the file path in <paramref name="keyPemFilePath" /> contains
        ///   a key that does not match the public key in the certificate.
        /// </para>
        /// <para>-or-</para>
        /// <para>The certificate uses an unknown public key algorithm.</para>
        /// <para>-or-</para>
        /// <para>The password specified for the private key is incorrect.</para>
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="certPemFilePath" /> is <see langword="null" />.
        /// </exception>
        /// <remarks>
        /// <para>
        /// See <see cref="System.IO.File.ReadAllText(string)" /> for additional documentation about
        /// exceptions that can be thrown.
        /// </para>
        /// <para>
        /// Password protected PEM-encoded keys are always expected to have the PEM label "ENCRYPTED PRIVATE KEY".
        /// </para>
        /// <para>PEM-encoded items that have a different label are ignored.</para>
        /// <para>
        ///   Combined PEM-encoded certificates and keys do not require a specific order. For the certificate, the
        ///   the first certificate with a CERTIFICATE label is loaded. For the private key, the first private
        ///   key with the label "ENCRYPTED PRIVATE KEY" is loaded. More advanced scenarios for loading certificates and
        ///   private keys can leverage <see cref="System.Security.Cryptography.PemEncoding" /> to enumerate
        ///   PEM-encoded values and apply any custom loading behavior.
        /// </para>
        /// <para>
        /// For PEM-encoded keys without a password, use <see cref="CreateFromPemFile" />.
        /// </para>
        /// </remarks>
        [UnsupportedOSPlatform("browser")]
        public static X509Certificate2 CreateFromEncryptedPemFile(string certPemFilePath, ReadOnlySpan<char> password, string? keyPemFilePath = default)
        {
            ArgumentNullException.ThrowIfNull(certPemFilePath);

            ReadOnlySpan<char> certContents = File.ReadAllText(certPemFilePath);
            ReadOnlySpan<char> keyContents = keyPemFilePath is null ? certContents : File.ReadAllText(keyPemFilePath);

            return CreateFromEncryptedPem(certContents, keyContents, password);
        }

        /// <summary>
        /// Creates a new X509 certificate from the contents of an RFC 7468 PEM-encoded certificate and private key.
        /// </summary>
        /// <param name="certPem">The text of the PEM-encoded X509 certificate.</param>
        /// <param name="keyPem">The text of the PEM-encoded private key.</param>
        /// <returns>A new certificate with the private key.</returns>
        /// <exception cref="CryptographicException">
        /// <para>The contents of <paramref name="certPem" /> do not contain a PEM-encoded certificate, or it is malformed.</para>
        /// <para>-or-</para>
        /// <para>The contents of <paramref name="keyPem" /> do not contain a PEM-encoded private key, or it is malformed.</para>
        /// <para>-or-</para>
        /// <para>The contents of <paramref name="keyPem" /> contains a key that does not match the public key in the certificate.</para>
        /// <para>-or-</para>
        /// <para>The certificate uses an unknown public key algorithm.</para>
        /// </exception>
        /// <remarks>
        /// <para>
        ///   The SubjectPublicKeyInfo from the certificate determines what PEM labels are accepted for the private key.
        ///   For RSA certificates, accepted private key PEM labels are "RSA PRIVATE KEY" and "PRIVATE KEY".
        ///   For ECDSA and ECDH certificates, accepted private key PEM labels are "EC PRIVATE KEY" and "PRIVATE KEY".
        ///   For DSA certificates, the accepted private key PEM label is "PRIVATE KEY".
        /// </para>
        /// <para>PEM-encoded items that have a different label are ignored.</para>
        /// <para>
        ///   If the PEM-encoded certificate and private key are in the same text, use the same
        ///   string for both <paramref name="certPem" /> and <paramref name="keyPem" />, such as:
        ///   <code>
        ///     CreateFromPem(combinedCertAndKey, combinedCertAndKey);
        ///   </code>
        ///   Combined PEM-encoded certificates and keys do not require a specific order. For the certificate, the
        ///   the first certificate with a CERTIFICATE label is loaded. For the private key, the first private
        ///   key with an acceptable label is loaded. More advanced scenarios for loading certificates and
        ///   private keys can leverage <see cref="System.Security.Cryptography.PemEncoding" /> to enumerate
        ///   PEM-encoded values and apply any custom loading behavior.
        /// </para>
        /// <para>
        /// For password protected PEM-encoded keys, use <see cref="CreateFromEncryptedPem" /> to specify a password.
        /// </para>
        /// </remarks>
        [UnsupportedOSPlatform("browser")]
        public static X509Certificate2 CreateFromPem(ReadOnlySpan<char> certPem, ReadOnlySpan<char> keyPem)
        {
            using (X509Certificate2 certificate = CreateFromPem(certPem))
            {
                string keyAlgorithm = certificate.GetKeyAlgorithm();

                return keyAlgorithm switch
                {
                    Oids.Rsa => ExtractKeyFromPem<RSA>(
                        keyPem,
                        s_RsaPublicKeyPrivateKeyLabels,
                        static keyPem => CreateAndImport(keyPem, RSA.Create),
                        certificate.CopyWithPrivateKey),
                    Oids.Dsa when Helpers.IsDSASupported => ExtractKeyFromPem<DSA>(
                        keyPem,
                        s_DsaPublicKeyPrivateKeyLabels,
                        static keyPem => CreateAndImport(keyPem, DSA.Create),
                        certificate.CopyWithPrivateKey),
                    Oids.EcPublicKey => ExtractKeyFromECPem(certificate, keyPem),
                    _ => throw new CryptographicException(SR.Format(SR.Cryptography_UnknownKeyAlgorithm, keyAlgorithm)),
                };
            }
        }

        /// <summary>
        /// Creates a new X509 certificate from the contents of an RFC 7468 PEM-encoded
        /// certificate and password protected private key.
        /// </summary>
        /// <param name="certPem">The text of the PEM-encoded X509 certificate.</param>
        /// <param name="keyPem">The text of the password protected PEM-encoded private key.</param>
        /// <param name="password">The password for the encrypted PEM.</param>
        /// <returns>A new certificate with the private key.</returns>
        /// <exception cref="CryptographicException">
        /// <para>The contents of <paramref name="certPem" /> do not contain a PEM-encoded certificate, or it is malformed.</para>
        /// <para>-or-</para>
        /// <para>
        ///   The contents of <paramref name="keyPem" /> do not contain a password protected PEM-encoded private key,
        ///   or it is malformed.
        /// </para>
        /// <para>-or-</para>
        /// <para>The contents of <paramref name="keyPem" /> contains a key that does not match the public key in the certificate.</para>
        /// <para>-or-</para>
        /// <para>The certificate uses an unknown public key algorithm.</para>
        /// <para>-or-</para>
        /// <para>The password specified for the private key is incorrect.</para>
        /// </exception>
        /// <remarks>
        /// <para>
        /// Password protected PEM-encoded keys are always expected to have the PEM label "ENCRYPTED PRIVATE KEY".
        /// </para>
        /// <para>PEM-encoded items that have a different label are ignored.</para>
        /// <para>
        ///   If the PEM-encoded certificate and private key are in the same text, use the same
        ///   string for both <paramref name="certPem" /> and <paramref name="keyPem" />, such as:
        ///   <code>
        ///     CreateFromEncryptedPem(combinedCertAndKey, combinedCertAndKey, theKeyPassword);
        ///   </code>
        ///   Combined PEM-encoded certificates and keys do not require a specific order. For the certificate, the
        ///   the first certificate with a CERTIFICATE label is loaded. For the private key, the first private
        ///   key with the label "ENCRYPTED PRIVATE KEY" is loaded. More advanced scenarios for loading certificates and
        ///   private keys can leverage <see cref="System.Security.Cryptography.PemEncoding" /> to enumerate
        ///   PEM-encoded values and apply any custom loading behavior.
        /// </para>
        /// <para>
        /// For PEM-encoded keys without a password, use <see cref="CreateFromPem(ReadOnlySpan{char}, ReadOnlySpan{char})" />.
        /// </para>
        /// </remarks>
        [UnsupportedOSPlatform("browser")]
        public static X509Certificate2 CreateFromEncryptedPem(ReadOnlySpan<char> certPem, ReadOnlySpan<char> keyPem, ReadOnlySpan<char> password)
        {
            using (X509Certificate2 certificate = CreateFromPem(certPem))
            {
                string keyAlgorithm = certificate.GetKeyAlgorithm();

                return keyAlgorithm switch
                {
                    Oids.Rsa =>
                        ExtractKeyFromEncryptedPem<RSA>(
                            keyPem,
                            password,
                            static (keyPem, password) => CreateAndImportEncrypted(keyPem, password, RSA.Create),
                            certificate.CopyWithPrivateKey),
                    Oids.Dsa when Helpers.IsDSASupported =>
                        ExtractKeyFromEncryptedPem<DSA>(
                            keyPem,
                            password,
                            static (keyPem, password) => CreateAndImportEncrypted(keyPem, password, DSA.Create),
                            certificate.CopyWithPrivateKey),
                    Oids.EcPublicKey => ExtractKeyFromEncryptedECPem(certificate, keyPem, password),
                    _ => throw new CryptographicException(SR.Format(SR.Cryptography_UnknownKeyAlgorithm, keyAlgorithm)),
                };
            }
        }

        private static bool IsECDsa(X509Certificate2 certificate)
        {
            using (ECDsa? ecdsa = certificate.GetECDsaPublicKey())
            {
                return ecdsa is not null;
            }
        }

        private static bool IsECDiffieHellman(X509Certificate2 certificate)
        {
            using (ECDiffieHellman? ecdh = certificate.GetECDiffieHellmanPublicKey())
            {
                return ecdh is not null;
            }
        }

        /// <summary>
        /// Creates a new X509 certificate from the contents of an RFC 7468 PEM-encoded
        /// certificate.
        /// </summary>
        /// <param name="certPem">The text of the PEM-encoded X509 certificate.</param>
        /// <returns>A new X509 certificate.</returns>
        /// <exception cref="CryptographicException">
        /// The contents of <paramref name="certPem" /> do not contain a PEM-encoded certificate, or it is malformed.
        /// </exception>
        /// <remarks>
        /// <para>
        /// This loads the first well-formed PEM found with a CERTIFICATE label.
        /// </para>
        /// <para>
        /// For PEM-encoded certificates with a private key, use
        /// <see cref="CreateFromPem(ReadOnlySpan{char}, ReadOnlySpan{char})" />.
        /// </para>
        /// <para>
        /// For PEM-encoded certificates in a file, use <see cref="X509Certificate2(string)" />.
        /// </para>
        /// </remarks>
        [UnsupportedOSPlatform("browser")]
        public static X509Certificate2 CreateFromPem(ReadOnlySpan<char> certPem)
        {
            foreach ((ReadOnlySpan<char> contents, PemFields fields) in PemEnumerator.Utf16(certPem))
            {
                ReadOnlySpan<char> label = contents[fields.Label];

                if (label.SequenceEqual(PemLabels.X509Certificate))
                {
                    byte[] certBytes = CryptoPool.Rent(fields.DecodedDataLength);

                    if (!Convert.TryFromBase64Chars(contents[fields.Base64Data], certBytes, out int bytesWritten)
                        || bytesWritten != fields.DecodedDataLength)
                    {
                        Debug.Fail("The contents should have already been validated by the PEM reader.");
                        throw new CryptographicException(SR.Cryptography_X509_NoPemCertificate);
                    }

                    ReadOnlyMemory<byte> certData = new ReadOnlyMemory<byte>(certBytes, 0, bytesWritten);

                    try
                    {
                        // Check that the contents are actually an X509 DER encoded
                        // certificate, not something else that the constructor will
                        // will otherwise be able to figure out.
                        CertificateAsn.Decode(certData, AsnEncodingRules.DER);
                    }
                    catch (CryptographicException)
                    {
                        throw new CryptographicException(SR.Cryptography_X509_NoPemCertificate);
                    }

                    // NetWasm: X509CertificateLoader is not ported; the constructor loads the one DER certificate.
#pragma warning disable SYSLIB0057
                    X509Certificate2 ret = new X509Certificate2(certData.Span);
#pragma warning restore SYSLIB0057
                    // Certs are public data, no need to clear.
                    CryptoPool.Return(certBytes, clearSize: 0);
                    return ret;
                }
            }

            throw new CryptographicException(SR.Cryptography_X509_NoPemCertificate);
        }

        private static TAlg CreateAndImport<TAlg>(ReadOnlySpan<char> keyPem, Func<TAlg> factory) where TAlg : AsymmetricAlgorithm
        {
            TAlg alg = factory();
            alg.ImportFromPem(keyPem);
            return alg;
        }

        private static TAlg CreateAndImportEncrypted<TAlg>(
            ReadOnlySpan<char> keyPem,
            ReadOnlySpan<char> password,
            Func<TAlg> factory) where TAlg : AsymmetricAlgorithm
        {
            TAlg alg = factory();
            alg.ImportFromEncryptedPem(keyPem, password);
            return alg;
        }

        private static X509Certificate2 ExtractKeyFromPem<TAlg>(
            ReadOnlySpan<char> keyPem,
            ReadOnlySpan<string> labels,
            Func<ReadOnlySpan<char>, TAlg> factory,
            Func<TAlg, X509Certificate2> import) where TAlg : IDisposable
        {
            foreach ((ReadOnlySpan<char> contents, PemFields fields) in PemEnumerator.Utf16(keyPem))
            {
                ReadOnlySpan<char> label = contents[fields.Label];

                foreach (string eligibleLabel in labels)
                {
                    if (label.SequenceEqual(eligibleLabel))
                    {
                        return ExtractKeyFromPem(contents[fields.Location], factory, import);
                    }
                }
            }

            throw new CryptographicException(SR.Cryptography_X509_NoOrMismatchedPemKey);
        }

        private static X509Certificate2 ExtractKeyFromPem<TAlg>(
            ReadOnlySpan<char> keyPem,
            Func<ReadOnlySpan<char>, TAlg> factory,
            Func<TAlg, X509Certificate2> import) where TAlg : IDisposable
        {
            using (TAlg key = factory(keyPem))
            {
                try
                {
                    return import(key);
                }
                catch (ArgumentException ae)
                {
                    throw new CryptographicException(SR.Cryptography_X509_NoOrMismatchedPemKey, ae);
                }
            }
        }

        private static X509Certificate2 ExtractKeyFromEncryptedPem<TAlg>(
            ReadOnlySpan<char> keyPem,
            ReadOnlySpan<char> password,
            Func<ReadOnlySpan<char>, ReadOnlySpan<char>, TAlg> factory,
            Func<TAlg, X509Certificate2> import) where TAlg : IDisposable
        {
            foreach ((ReadOnlySpan<char> contents, PemFields fields) in PemEnumerator.Utf16(keyPem))
            {
                ReadOnlySpan<char> label = contents[fields.Label];

                if (label.SequenceEqual(PemLabels.EncryptedPkcs8PrivateKey))
                {
                    using (TAlg key = factory(contents[fields.Location], password))
                    {
                        try
                        {
                            return import(key);
                        }
                        catch (ArgumentException ae)
                        {
                            throw new CryptographicException(SR.Cryptography_X509_NoOrMismatchedPemKey, ae);
                        }
                    }
                }
            }

            throw new CryptographicException(SR.Cryptography_X509_NoOrMismatchedPemKey);
        }

        [UnsupportedOSPlatform("browser")]
        private static X509Certificate2 ExtractKeyFromEncryptedECPem(
            X509Certificate2 certificate,
            ReadOnlySpan<char> keyPem,
            ReadOnlySpan<char> password)
        {
            Debug.Assert(certificate.GetKeyAlgorithm() == Oids.EcPublicKey);

            foreach ((ReadOnlySpan<char> contents, PemFields fields) in PemEnumerator.Utf16(keyPem))
            {
                ReadOnlySpan<char> label = contents[fields.Label];

                if (!label.SequenceEqual(PemLabels.EncryptedPkcs8PrivateKey))
                {
                    continue;
                }

                byte[] base64Buffer = CryptoPool.Rent(fields.DecodedDataLength);
                int base64ClearSize = CryptoPool.ClearAll;
                ArraySegment<byte>? decryptedPkcs8 = null;

                try
                {
                    bool result = Convert.TryFromBase64Chars(contents[fields.Base64Data], base64Buffer, out int base64Written);

                    if (!result || base64Written != fields.DecodedDataLength)
                    {
                        Debug.Fail("Preallocated buffer and validated data decoding failed.");
                        break;
                    }

                    base64ClearSize = base64Written;
                    Debug.Assert(!decryptedPkcs8.HasValue);
                    decryptedPkcs8 = KeyFormatHelper.DecryptPkcs8(password, base64Buffer.AsSpan(0, base64Written), out int bytesRead);

                    if (bytesRead != base64Written)
                    {
                        break;
                    }

                    X509Certificate2? loaded = ExtractKeyFromECPrivateKeyInfo(certificate, decryptedPkcs8.Value);

                    if (loaded is null)
                    {
                        break;
                    }

                    return loaded;
                }
                catch (CryptographicException ce)
                {
                    throw new CryptographicException(SR.Cryptography_X509_NoOrMismatchedPemKey, ce);
                }
                finally
                {
                    CryptoPool.Return(base64Buffer, base64ClearSize);

                    if (decryptedPkcs8.HasValue)
                    {
                        CryptoPool.Return(decryptedPkcs8.Value);
                    }
                }
            }

            throw new CryptographicException(SR.Cryptography_X509_NoOrMismatchedPemKey);
        }

        [UnsupportedOSPlatform("browser")]
        private static X509Certificate2 ExtractKeyFromECPem(X509Certificate2 certificate, ReadOnlySpan<char> keyPem)
        {
            Debug.Assert(certificate.GetKeyAlgorithm() == Oids.EcPublicKey);

            foreach ((ReadOnlySpan<char> contents, PemFields fields) in PemEnumerator.Utf16(keyPem))
            {
                ReadOnlySpan<char> label = contents[fields.Label];

                if (label.SequenceEqual(PemLabels.EcPrivateKey))
                {
                    // EC PRIVATE KEYs do not have a key usage, so usage is determined by the certificate.

                    // If we can load it is EC-DH, we should prefer that over EC-DSA. ECC keys that are "both" prefer
                    // to be imported as EC-DH. Importing it as EC-DSA would restrict it to EC-DSA, even if the key
                    // and certificate are valid for EC-DH. Other platforms don't have such restrictions.
                    if (IsECDiffieHellman(certificate))
                    {
                        return ExtractKeyFromPem(
                            keyPem,
                            static keyPem => CreateAndImport(keyPem, ECDiffieHellman.Create),
                            certificate.CopyWithPrivateKey);
                    }

                    if (IsECDsa(certificate))
                    {
                        return ExtractKeyFromPem(
                            keyPem,
                            static keyPem => CreateAndImport(keyPem, ECDsa.Create),
                            certificate.CopyWithPrivateKey);
                    }

                    // If we got here, then the key is neither EC-DH or EC-DSA eligible, but we had a matching PEM
                    // label. Break out and throw.
                    break;
                }

                if (!label.SequenceEqual(PemLabels.Pkcs8PrivateKey))
                {
                    continue;
                }

                byte[] base64Buffer = CryptoPool.Rent(fields.DecodedDataLength);
                int clearSize = CryptoPool.ClearAll;

                try
                {
                    bool result = Convert.TryFromBase64Chars(contents[fields.Base64Data], base64Buffer, out int base64Written);

                    if (!result || base64Written != fields.DecodedDataLength)
                    {
                        Debug.Fail("Preallocated buffer and validated data decoding failed.");
                        break;
                    }

                    clearSize = base64Written;
                    X509Certificate2? loaded = ExtractKeyFromECPrivateKeyInfo(certificate, base64Buffer.AsMemory(0, base64Written));

                    if (loaded is null)
                    {
                        break;
                    }

                    return loaded;
                }
                catch (CryptographicException ce)
                {
                    throw new CryptographicException(SR.Cryptography_X509_NoOrMismatchedPemKey, ce);
                }
                finally
                {
                    CryptoPool.Return(base64Buffer, clearSize);
                }
            }

            throw new CryptographicException(SR.Cryptography_X509_NoOrMismatchedPemKey);
        }

        [UnsupportedOSPlatform("browser")]
        private static X509Certificate2? ExtractKeyFromECPrivateKeyInfo(
            X509Certificate2 certificate,
            ReadOnlyMemory<byte> privateKeyInfo)
        {
            // We are not going to perform any validation on the PrivateKeyInfo here. We just need a hint
            // what algorithm to use. The actual algorithm will do whatever validation on the key as needed.
            PrivateKeyInfoAsn privateKeyInfoAsn = PrivateKeyInfoAsn.Decode(privateKeyInfo, AsnEncodingRules.BER);

            const X509KeyUsageFlags EcdsaKeyUsageFlags =
                X509KeyUsageFlags.DigitalSignature |
                X509KeyUsageFlags.KeyCertSign |
                X509KeyUsageFlags.CrlSign;

            X509KeyUsageFlags? usages = GetKeyUsageFlags(in privateKeyInfoAsn);

            // If any of the following is true we should load it as EC-DH
            // * There is no keyUsage extension. Loading it as EC-DH will allow loading it as EC-DSA, too.
            // * It has any keyUsage that is not a "signing" usage. That at minimum means loading it as EC-DH.
            //   it may still yet have a keyUsage that allows signing as well, in which case it will work for EC-DSA
            //   too.
            // The certificate must also have a key usage that permits EC-DH, either with "no" usage (in which case
            // it will work for EC-DSA, too) or as EC-DH explicitly.
            if ((usages is null || (usages & ~EcdsaKeyUsageFlags) != 0) && IsECDiffieHellman(certificate))
            {
                using (ECDiffieHellman ecdh = ECDiffieHellman.Create())
                {
                    ecdh.ImportPkcs8PrivateKey(privateKeyInfo.Span, out int pkcs8Read);

                    if (pkcs8Read != privateKeyInfo.Length)
                    {
                        Debug.Fail("Unexpected trailing data in PKCS#8 buffer.");
                        throw new CryptographicException();
                    }

                    return certificate.CopyWithPrivateKey(ecdh);
                }
            }

            // If we are here, then either the key or certificate has a key usage that requires loading it as EC-DSA.
            if (IsECDsa(certificate))
            {
                using (ECDsa ecdsa = ECDsa.Create())
                {
                    ecdsa.ImportPkcs8PrivateKey(privateKeyInfo.Span, out int pkcs8Read);

                    if (pkcs8Read != privateKeyInfo.Length)
                    {
                        Debug.Fail("Unexpected trailing data in PKCS#8 buffer.");
                        throw new CryptographicException();
                    }

                    return certificate.CopyWithPrivateKey(ecdsa);
                }
            }

            // If we get here, the key and certificate do not agree on algorithm use (the key has digitalSignature but
            // the certificate has keyAgreement, for example). It cannot be loaded.
            return null;
        }

        private static X509KeyUsageFlags? GetKeyUsageFlags(ref readonly PrivateKeyInfoAsn keyInfo)
        {
            if (keyInfo.Attributes is null)
            {
                return null;
            }

            foreach (AttributeAsn attr in keyInfo.Attributes)
            {
                if (attr.AttrType != Oids.KeyUsage)
                {
                    continue;
                }

                if (attr.AttrValues is [ReadOnlyMemory<byte> attrValue])
                {
                    X509KeyUsageExtension.DecodeX509KeyUsageExtension(attrValue.Span, out X509KeyUsageFlags usages);
                    return usages;
                }

                // If the attribute has no value or too many values, consider it malformed.
                throw new CryptographicException(SR.Cryptography_X509_NoOrMismatchedPemKey);
            }

            return null;
        }
    }
}
