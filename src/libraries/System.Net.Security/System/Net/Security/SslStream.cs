// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// EncryptionPolicy and the certificate callbacks are ported from dotnet/runtime commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce (System/Net/Security/SslStream.cs).
// NetWasm: the concrete SslStream is deferred (TLS needs a host security backend that NetWasm does not have).
// Its .NET Standard 2.1 surface comes from the pinned reference source (ref/System.Net.Security.cs):
// constructors validate the inner stream through AuthenticatedStream as upstream's do, then throw
// PlatformNotSupportedException before taking ownership or doing any IO; every other member throws.

using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Security
{
    public enum EncryptionPolicy
    {
        // Prohibit null ciphers (current system defaults)
        RequireEncryption = 0,

        // Add null ciphers to current system defaults
        [System.ObsoleteAttribute(Obsoletions.EncryptionPolicyMessage, DiagnosticId = Obsoletions.EncryptionPolicyDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        AllowNoEncryption,

        // Request null ciphers only
        [System.ObsoleteAttribute(Obsoletions.EncryptionPolicyMessage, DiagnosticId = Obsoletions.EncryptionPolicyDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        NoEncryption
    }

    // A user delegate used to verify remote SSL certificate.
    public delegate bool RemoteCertificateValidationCallback(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors);

    // A user delegate used to select local SSL certificate.
    public delegate X509Certificate? LocalCertificateSelectionCallback(object sender, string targetHost, X509CertificateCollection localCertificates, X509Certificate? remoteCertificate, string[] acceptableIssuers);

    public delegate X509Certificate ServerCertificateSelectionCallback(object sender, string? hostName);

    public partial class SslStream : AuthenticatedStream
    {
        public SslStream(System.IO.Stream innerStream) : base(innerStream, false)
        {
            throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        }
        public SslStream(System.IO.Stream innerStream, bool leaveInnerStreamOpen) : base(innerStream, leaveInnerStreamOpen)
        {
            throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        }
        public SslStream(System.IO.Stream innerStream, bool leaveInnerStreamOpen, System.Net.Security.RemoteCertificateValidationCallback? userCertificateValidationCallback) : base(innerStream, leaveInnerStreamOpen)
        {
            throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        }
        public SslStream(System.IO.Stream innerStream, bool leaveInnerStreamOpen, System.Net.Security.RemoteCertificateValidationCallback? userCertificateValidationCallback, System.Net.Security.LocalCertificateSelectionCallback? userCertificateSelectionCallback) : base(innerStream, leaveInnerStreamOpen)
        {
            throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        }
        public SslStream(System.IO.Stream innerStream, bool leaveInnerStreamOpen, System.Net.Security.RemoteCertificateValidationCallback? userCertificateValidationCallback, System.Net.Security.LocalCertificateSelectionCallback? userCertificateSelectionCallback, System.Net.Security.EncryptionPolicy encryptionPolicy) : base(innerStream, leaveInnerStreamOpen)
        {
            throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        }
        public override bool CanRead { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public override bool CanSeek { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public override bool CanTimeout { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public override bool CanWrite { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public virtual bool CheckCertRevocationStatus { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        [System.ObsoleteAttribute("KeyExchangeAlgorithm, KeyExchangeStrength, CipherAlgorithm, CipherStrength, HashAlgorithm and HashStrength properties of SslStream are obsolete. Use NegotiatedCipherSuite instead.", DiagnosticId = "SYSLIB0058", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        public virtual System.Security.Authentication.CipherAlgorithmType CipherAlgorithm { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        [System.ObsoleteAttribute("KeyExchangeAlgorithm, KeyExchangeStrength, CipherAlgorithm, CipherStrength, HashAlgorithm and HashStrength properties of SslStream are obsolete. Use NegotiatedCipherSuite instead.", DiagnosticId = "SYSLIB0058", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        public virtual int CipherStrength { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        [System.ObsoleteAttribute("KeyExchangeAlgorithm, KeyExchangeStrength, CipherAlgorithm, CipherStrength, HashAlgorithm and HashStrength properties of SslStream are obsolete. Use NegotiatedCipherSuite instead.", DiagnosticId = "SYSLIB0058", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        public virtual System.Security.Authentication.HashAlgorithmType HashAlgorithm { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        [System.ObsoleteAttribute("KeyExchangeAlgorithm, KeyExchangeStrength, CipherAlgorithm, CipherStrength, HashAlgorithm and HashStrength properties of SslStream are obsolete. Use NegotiatedCipherSuite instead.", DiagnosticId = "SYSLIB0058", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        public virtual int HashStrength { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public override bool IsAuthenticated { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public override bool IsEncrypted { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public override bool IsMutuallyAuthenticated { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public override bool IsServer { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public override bool IsSigned { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        [System.ObsoleteAttribute("KeyExchangeAlgorithm, KeyExchangeStrength, CipherAlgorithm, CipherStrength, HashAlgorithm and HashStrength properties of SslStream are obsolete. Use NegotiatedCipherSuite instead.", DiagnosticId = "SYSLIB0058", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        public virtual System.Security.Authentication.ExchangeAlgorithmType KeyExchangeAlgorithm { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        [System.ObsoleteAttribute("KeyExchangeAlgorithm, KeyExchangeStrength, CipherAlgorithm, CipherStrength, HashAlgorithm and HashStrength properties of SslStream are obsolete. Use NegotiatedCipherSuite instead.", DiagnosticId = "SYSLIB0058", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        public virtual int KeyExchangeStrength { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public override long Length { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public virtual System.Security.Cryptography.X509Certificates.X509Certificate? LocalCertificate { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public System.Net.Security.SslApplicationProtocol NegotiatedApplicationProtocol { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public override long Position { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); set => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public override int ReadTimeout { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); set => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public virtual System.Security.Cryptography.X509Certificates.X509Certificate? RemoteCertificate { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public virtual System.Security.Authentication.SslProtocols SslProtocol { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public System.Net.TransportContext TransportContext { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public override int WriteTimeout { get => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); set => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported); }
        public virtual void AuthenticateAsClient(string targetHost) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual void AuthenticateAsClient(string targetHost, System.Security.Cryptography.X509Certificates.X509CertificateCollection? clientCertificates, bool checkCertificateRevocation) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual void AuthenticateAsClient(string targetHost, System.Security.Cryptography.X509Certificates.X509CertificateCollection? clientCertificates, System.Security.Authentication.SslProtocols enabledSslProtocols, bool checkCertificateRevocation) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public System.Threading.Tasks.Task AuthenticateAsClientAsync(System.Net.Security.SslClientAuthenticationOptions sslClientAuthenticationOptions, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual System.Threading.Tasks.Task AuthenticateAsClientAsync(string targetHost) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual System.Threading.Tasks.Task AuthenticateAsClientAsync(string targetHost, System.Security.Cryptography.X509Certificates.X509CertificateCollection? clientCertificates, bool checkCertificateRevocation) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual System.Threading.Tasks.Task AuthenticateAsClientAsync(string targetHost, System.Security.Cryptography.X509Certificates.X509CertificateCollection? clientCertificates, System.Security.Authentication.SslProtocols enabledSslProtocols, bool checkCertificateRevocation) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual void AuthenticateAsServer(System.Security.Cryptography.X509Certificates.X509Certificate serverCertificate) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual void AuthenticateAsServer(System.Security.Cryptography.X509Certificates.X509Certificate serverCertificate, bool clientCertificateRequired, bool checkCertificateRevocation) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual void AuthenticateAsServer(System.Security.Cryptography.X509Certificates.X509Certificate serverCertificate, bool clientCertificateRequired, System.Security.Authentication.SslProtocols enabledSslProtocols, bool checkCertificateRevocation) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public System.Threading.Tasks.Task AuthenticateAsServerAsync(System.Net.Security.SslServerAuthenticationOptions sslServerAuthenticationOptions, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual System.Threading.Tasks.Task AuthenticateAsServerAsync(System.Security.Cryptography.X509Certificates.X509Certificate serverCertificate) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual System.Threading.Tasks.Task AuthenticateAsServerAsync(System.Security.Cryptography.X509Certificates.X509Certificate serverCertificate, bool clientCertificateRequired, bool checkCertificateRevocation) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual System.Threading.Tasks.Task AuthenticateAsServerAsync(System.Security.Cryptography.X509Certificates.X509Certificate serverCertificate, bool clientCertificateRequired, System.Security.Authentication.SslProtocols enabledSslProtocols, bool checkCertificateRevocation) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual System.IAsyncResult BeginAuthenticateAsClient(string targetHost, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual System.IAsyncResult BeginAuthenticateAsClient(string targetHost, System.Security.Cryptography.X509Certificates.X509CertificateCollection? clientCertificates, bool checkCertificateRevocation, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual System.IAsyncResult BeginAuthenticateAsClient(string targetHost, System.Security.Cryptography.X509Certificates.X509CertificateCollection? clientCertificates, System.Security.Authentication.SslProtocols enabledSslProtocols, bool checkCertificateRevocation, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual System.IAsyncResult BeginAuthenticateAsServer(System.Security.Cryptography.X509Certificates.X509Certificate serverCertificate, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual System.IAsyncResult BeginAuthenticateAsServer(System.Security.Cryptography.X509Certificates.X509Certificate serverCertificate, bool clientCertificateRequired, bool checkCertificateRevocation, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual System.IAsyncResult BeginAuthenticateAsServer(System.Security.Cryptography.X509Certificates.X509Certificate serverCertificate, bool clientCertificateRequired, System.Security.Authentication.SslProtocols enabledSslProtocols, bool checkCertificateRevocation, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public override System.IAsyncResult BeginRead(byte[] buffer, int offset, int count, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public override System.IAsyncResult BeginWrite(byte[] buffer, int offset, int count, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        protected override void Dispose(bool disposing) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public override System.Threading.Tasks.ValueTask DisposeAsync() => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual void EndAuthenticateAsClient(System.IAsyncResult asyncResult) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual void EndAuthenticateAsServer(System.IAsyncResult asyncResult) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public override int EndRead(System.IAsyncResult asyncResult) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public override void EndWrite(System.IAsyncResult asyncResult) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public override void Flush() => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public override System.Threading.Tasks.Task FlushAsync(System.Threading.CancellationToken cancellationToken) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public override int Read(byte[] buffer, int offset, int count) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public override System.Threading.Tasks.Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public override System.Threading.Tasks.ValueTask<int> ReadAsync(System.Memory<byte> buffer, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public override int ReadByte() => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public override void SetLength(long value) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public virtual System.Threading.Tasks.Task ShutdownAsync() => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public void Write(byte[] buffer) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public override void Write(byte[] buffer, int offset, int count) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public override System.Threading.Tasks.Task WriteAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
        public override System.Threading.Tasks.ValueTask WriteAsync(System.ReadOnlyMemory<byte> buffer, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) => throw new PlatformNotSupportedException(SR.net_ssl_NotSupported);
    }
}
