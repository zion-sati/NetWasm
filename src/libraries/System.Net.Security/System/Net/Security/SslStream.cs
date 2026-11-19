// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce (System/Net/Security/SslStream.cs,
// SslStream.IO.cs): the public surface, argument and state validation, and messages.
// NetWasm: a client-side SslStream over the platform's TLS provider (ITlsClientProvider; wasi:tls on WASI). The
// provider runs the handshake and validates the server's certificate against the host's roots. What wasi:tls does not
// offer throws PlatformNotSupportedException (server mode, client certificates, null encryption) or reads as unknown:
// the negotiated protocol (SslProtocols.None), the remote certificate (null), no application protocol, no cipher
// details. A RemoteCertificateValidationCallback runs after the host has accepted the certificate, without the
// certificate or chain (null) and with no policy errors; returning false rejects the connection, but no callback
// can accept a certificate the host rejected. Synchronous members are upstream's sync-over-async.

using System.IO;
using System.Runtime.ExceptionServices;
using System.Security.Authentication;
using System.Security.Authentication.ExtendedProtection;
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
        private enum NestedState
        {
            StreamNotInUse = 0,
            StreamInUse = 1,
            StreamDisposed = 2,
        }

        private static readonly ExceptionDispatchInfo s_disposedSentinel =
            ExceptionDispatchInfo.Capture(new ObjectDisposedException(nameof(SslStream), (string?)null));

        private readonly EncryptionPolicy _encryptionPolicy;
        private RemoteCertificateValidationCallback? _certValidationDelegate;
        private LocalCertificateSelectionCallback? _certSelectionDelegate;
        private X509RevocationMode _revocationMode = X509RevocationMode.NoCheck;
        private string _targetHost = string.Empty;
        private bool _authenticationAttempted;
        private bool _handshakeCompleted;
        private bool _shutdown;
        private ExceptionDispatchInfo? _exception;
        private ITlsClientSession? _session;
        private NestedState _nestedAuth;
        private NestedState _nestedRead;
        private NestedState _nestedWrite;

        public SslStream(Stream innerStream)
            : this(innerStream, false, null, null)
        {
        }

        public SslStream(Stream innerStream, bool leaveInnerStreamOpen)
            : this(innerStream, leaveInnerStreamOpen, null, null, EncryptionPolicy.RequireEncryption)
        {
        }

        public SslStream(Stream innerStream, bool leaveInnerStreamOpen, RemoteCertificateValidationCallback? userCertificateValidationCallback)
            : this(innerStream, leaveInnerStreamOpen, userCertificateValidationCallback, null, EncryptionPolicy.RequireEncryption)
        {
        }

        public SslStream(Stream innerStream, bool leaveInnerStreamOpen, RemoteCertificateValidationCallback? userCertificateValidationCallback,
            LocalCertificateSelectionCallback? userCertificateSelectionCallback)
            : this(innerStream, leaveInnerStreamOpen, userCertificateValidationCallback, userCertificateSelectionCallback, EncryptionPolicy.RequireEncryption)
        {
        }

        public SslStream(Stream innerStream, bool leaveInnerStreamOpen, RemoteCertificateValidationCallback? userCertificateValidationCallback,
            LocalCertificateSelectionCallback? userCertificateSelectionCallback, EncryptionPolicy encryptionPolicy)
            : base(innerStream, leaveInnerStreamOpen)
        {
#pragma warning disable SYSLIB0040 // NoEncryption and AllowNoEncryption are obsolete
            if (encryptionPolicy != EncryptionPolicy.RequireEncryption && encryptionPolicy != EncryptionPolicy.AllowNoEncryption && encryptionPolicy != EncryptionPolicy.NoEncryption)
            {
                throw new ArgumentException(SR.Format(SR.net_invalid_enum, "EncryptionPolicy"), nameof(encryptionPolicy));
            }
#pragma warning restore SYSLIB0040

            _encryptionPolicy = encryptionPolicy;
            _certValidationDelegate = userCertificateValidationCallback;
            _certSelectionDelegate = userCertificateSelectionCallback;
        }

        // Client side auth.
        public virtual IAsyncResult BeginAuthenticateAsClient(string targetHost, AsyncCallback? asyncCallback, object? asyncState) =>
            BeginAuthenticateAsClient(targetHost, null, SslProtocols.None, false, asyncCallback, asyncState);

        public virtual IAsyncResult BeginAuthenticateAsClient(string targetHost, X509CertificateCollection? clientCertificates,
            bool checkCertificateRevocation, AsyncCallback? asyncCallback, object? asyncState) =>
            BeginAuthenticateAsClient(targetHost, clientCertificates, SslProtocols.None, checkCertificateRevocation, asyncCallback, asyncState);

        public virtual IAsyncResult BeginAuthenticateAsClient(string targetHost, X509CertificateCollection? clientCertificates,
            SslProtocols enabledSslProtocols, bool checkCertificateRevocation, AsyncCallback? asyncCallback, object? asyncState) =>
            TaskToAsyncResult.Begin(AuthenticateAsClientAsync(ClientOptions(targetHost, clientCertificates, enabledSslProtocols, checkCertificateRevocation)), asyncCallback, asyncState);

        public virtual void EndAuthenticateAsClient(IAsyncResult asyncResult) => TaskToAsyncResult.End(asyncResult);

        // Server side auth: wasi:tls has no server mode.
        public virtual IAsyncResult BeginAuthenticateAsServer(X509Certificate serverCertificate, AsyncCallback? asyncCallback, object? asyncState) =>
            throw ServerNotSupported();

        public virtual IAsyncResult BeginAuthenticateAsServer(X509Certificate serverCertificate, bool clientCertificateRequired,
            bool checkCertificateRevocation, AsyncCallback? asyncCallback, object? asyncState) => throw ServerNotSupported();

        public virtual IAsyncResult BeginAuthenticateAsServer(X509Certificate serverCertificate, bool clientCertificateRequired,
            SslProtocols enabledSslProtocols, bool checkCertificateRevocation, AsyncCallback? asyncCallback, object? asyncState) =>
            throw ServerNotSupported();

        public virtual void EndAuthenticateAsServer(IAsyncResult asyncResult) => throw ServerNotSupported();

        public TransportContext TransportContext => new SslStreamContext();

        public virtual void AuthenticateAsClient(string targetHost) =>
            AuthenticateAsClient(targetHost, null, SslProtocols.None, false);

        public virtual void AuthenticateAsClient(string targetHost, X509CertificateCollection? clientCertificates, bool checkCertificateRevocation) =>
            AuthenticateAsClient(targetHost, clientCertificates, SslProtocols.None, checkCertificateRevocation);

        public virtual void AuthenticateAsClient(string targetHost, X509CertificateCollection? clientCertificates, SslProtocols enabledSslProtocols, bool checkCertificateRevocation) =>
            AuthenticateAsClient(ClientOptions(targetHost, clientCertificates, enabledSslProtocols, checkCertificateRevocation));

        public void AuthenticateAsClient(SslClientAuthenticationOptions sslClientAuthenticationOptions) =>
            AuthenticateAsClientAsync(sslClientAuthenticationOptions).GetAwaiter().GetResult();

        public virtual void AuthenticateAsServer(X509Certificate serverCertificate) => throw ServerNotSupported();

        public virtual void AuthenticateAsServer(X509Certificate serverCertificate, bool clientCertificateRequired, bool checkCertificateRevocation) =>
            throw ServerNotSupported();

        public virtual void AuthenticateAsServer(X509Certificate serverCertificate, bool clientCertificateRequired, SslProtocols enabledSslProtocols, bool checkCertificateRevocation) =>
            throw ServerNotSupported();

        public virtual Task AuthenticateAsClientAsync(string targetHost) => AuthenticateAsClientAsync(targetHost, null, false);

        public virtual Task AuthenticateAsClientAsync(string targetHost, X509CertificateCollection? clientCertificates, bool checkCertificateRevocation) =>
            AuthenticateAsClientAsync(targetHost, clientCertificates, SslProtocols.None, checkCertificateRevocation);

        public virtual Task AuthenticateAsClientAsync(string targetHost, X509CertificateCollection? clientCertificates, SslProtocols enabledSslProtocols, bool checkCertificateRevocation) =>
            AuthenticateAsClientAsync(ClientOptions(targetHost, clientCertificates, enabledSslProtocols, checkCertificateRevocation));

        public Task AuthenticateAsClientAsync(SslClientAuthenticationOptions sslClientAuthenticationOptions, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(sslClientAuthenticationOptions);

            ThrowIfExceptional();
            UpdateOptions(sslClientAuthenticationOptions);
            return ProcessAuthenticationAsync(cancellationToken);
        }

        public virtual Task AuthenticateAsServerAsync(X509Certificate serverCertificate) => throw ServerNotSupported();

        public virtual Task AuthenticateAsServerAsync(X509Certificate serverCertificate, bool clientCertificateRequired, bool checkCertificateRevocation) =>
            throw ServerNotSupported();

        public virtual Task AuthenticateAsServerAsync(X509Certificate serverCertificate, bool clientCertificateRequired, SslProtocols enabledSslProtocols, bool checkCertificateRevocation) =>
            throw ServerNotSupported();

        public Task AuthenticateAsServerAsync(SslServerAuthenticationOptions sslServerAuthenticationOptions, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(sslServerAuthenticationOptions);
            throw ServerNotSupported();
        }

        public virtual Task ShutdownAsync()
        {
            ThrowIfExceptionalOrNotAuthenticatedOrShutdown();
            _shutdown = true;
            return _session!.ShutdownAsync();
        }

        public override bool IsAuthenticated => _exception == null && _handshakeCompleted;

        // wasi:tls sends no client certificate.
        public override bool IsMutuallyAuthenticated => false;

        public override bool IsEncrypted => IsAuthenticated;

        public override bool IsSigned => IsAuthenticated;

        public override bool IsServer => false;

        // wasi:tls does not report the negotiated protocol.
        public virtual SslProtocols SslProtocol
        {
            get
            {
                ThrowIfExceptionalOrNotAuthenticated();
                return SslProtocols.None;
            }
        }

        public virtual bool CheckCertRevocationStatus => _revocationMode != X509RevocationMode.NoCheck;

        public virtual X509Certificate? LocalCertificate
        {
            get
            {
                ThrowIfExceptionalOrNotAuthenticated();
                return null;
            }
        }

        // wasi:tls validates the server's certificate in the host and does not hand it over.
        public virtual X509Certificate? RemoteCertificate
        {
            get
            {
                ThrowIfExceptional();
                return null;
            }
        }

        // wasi:tls negotiates no application protocol (ALPN), as with a server that offers none.
        public SslApplicationProtocol NegotiatedApplicationProtocol
        {
            get
            {
                ThrowIfExceptionalOrNotAuthenticated();
                return default;
            }
        }

        [Obsolete(Obsoletions.TlsCipherAlgorithmEnumsMessage, DiagnosticId = Obsoletions.TlsCipherAlgorithmEnumsDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public virtual CipherAlgorithmType CipherAlgorithm
        {
            get
            {
                ThrowIfExceptionalOrNotAuthenticated();
                return CipherAlgorithmType.None;
            }
        }

        [Obsolete(Obsoletions.TlsCipherAlgorithmEnumsMessage, DiagnosticId = Obsoletions.TlsCipherAlgorithmEnumsDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public virtual int CipherStrength
        {
            get
            {
                ThrowIfExceptionalOrNotAuthenticated();
                return 0;
            }
        }

        [Obsolete(Obsoletions.TlsCipherAlgorithmEnumsMessage, DiagnosticId = Obsoletions.TlsCipherAlgorithmEnumsDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public virtual HashAlgorithmType HashAlgorithm
        {
            get
            {
                ThrowIfExceptionalOrNotAuthenticated();
                return HashAlgorithmType.None;
            }
        }

        [Obsolete(Obsoletions.TlsCipherAlgorithmEnumsMessage, DiagnosticId = Obsoletions.TlsCipherAlgorithmEnumsDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public virtual int HashStrength
        {
            get
            {
                ThrowIfExceptionalOrNotAuthenticated();
                return 0;
            }
        }

        [Obsolete(Obsoletions.TlsCipherAlgorithmEnumsMessage, DiagnosticId = Obsoletions.TlsCipherAlgorithmEnumsDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public virtual ExchangeAlgorithmType KeyExchangeAlgorithm
        {
            get
            {
                ThrowIfExceptionalOrNotAuthenticated();
                return ExchangeAlgorithmType.None;
            }
        }

        [Obsolete(Obsoletions.TlsCipherAlgorithmEnumsMessage, DiagnosticId = Obsoletions.TlsCipherAlgorithmEnumsDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public virtual int KeyExchangeStrength
        {
            get
            {
                ThrowIfExceptionalOrNotAuthenticated();
                return 0;
            }
        }

        // Stream contract implementation.
        public override bool CanSeek => false;

        public override bool CanRead => IsAuthenticated && InnerStream.CanRead;

        public override bool CanTimeout => InnerStream.CanTimeout;

        public override bool CanWrite => IsAuthenticated && InnerStream.CanWrite && !_shutdown;

        public override int ReadTimeout
        {
            get => InnerStream.ReadTimeout;
            set => InnerStream.ReadTimeout = value;
        }

        public override int WriteTimeout
        {
            get => InnerStream.WriteTimeout;
            set => InnerStream.WriteTimeout = value;
        }

        public override long Length => InnerStream.Length;

        public override long Position
        {
            get => InnerStream.Position;
            set => throw new NotSupportedException(SR.net_noseek);
        }

        public override void SetLength(long value) => InnerStream.SetLength(value);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(SR.net_noseek);

        // Each chunk of ciphertext is flushed to the inner stream as the provider produces it.
        public override void Flush() => InnerStream.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => InnerStream.FlushAsync(cancellationToken);

        protected override void Dispose(bool disposing)
        {
            try
            {
                CloseInternal();
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                CloseInternal();
            }
            finally
            {
                await base.DisposeAsync().ConfigureAwait(false);
            }
        }

        public override int ReadByte()
        {
            var oneByte = new byte[1];
            return Read(oneByte, 0, 1) == 0 ? -1 : oneByte[0];
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        }

        public void Write(byte[] buffer) => Write(buffer, 0, buffer.Length);

        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? asyncCallback, object? asyncState) =>
            TaskToAsyncResult.Begin(ReadAsync(buffer, offset, count, CancellationToken.None), asyncCallback, asyncState);

        public override int EndRead(IAsyncResult asyncResult) => TaskToAsyncResult.End<int>(asyncResult);

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? asyncCallback, object? asyncState) =>
            TaskToAsyncResult.Begin(WriteAsync(buffer, offset, count, CancellationToken.None), asyncCallback, asyncState);

        public override void EndWrite(IAsyncResult asyncResult) => TaskToAsyncResult.End(asyncResult);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            return ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfExceptionalOrNotAuthenticated();
            return ReadAsyncInternal(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            return WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken).AsTask();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfExceptionalOrNotAuthenticatedOrShutdown();
            return WriteAsyncInternal(buffer, cancellationToken);
        }

        private async ValueTask<int> ReadAsyncInternal(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _nestedRead, NestedState.StreamInUse) == NestedState.StreamInUse)
            {
                throw new NotSupportedException(SR.Format(SR.net_io_invalidnestedcall, "read"));
            }

            try
            {
                return await _session!.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException && !ReferenceEquals(_exception, s_disposedSentinel))
            {
                var failure = e is IOException ? new IOException(SR.net_io_decrypt, e) : new IOException(SR.net_io_read, e);
                SetException(failure);
                throw failure;
            }
            finally
            {
                Interlocked.CompareExchange(ref _nestedRead, NestedState.StreamNotInUse, NestedState.StreamInUse);
            }
        }

        private async ValueTask WriteAsyncInternal(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _nestedWrite, NestedState.StreamInUse) == NestedState.StreamInUse)
            {
                throw new NotSupportedException(SR.Format(SR.net_io_invalidnestedcall, "write"));
            }

            try
            {
                await _session!.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException && !ReferenceEquals(_exception, s_disposedSentinel))
            {
                var failure = new IOException(SR.net_io_write, e);
                SetException(failure);
                throw failure;
            }
            finally
            {
                Interlocked.CompareExchange(ref _nestedWrite, NestedState.StreamNotInUse, NestedState.StreamInUse);
            }
        }

        private SslClientAuthenticationOptions ClientOptions(string targetHost, X509CertificateCollection? clientCertificates,
            SslProtocols enabledSslProtocols, bool checkCertificateRevocation) =>
            new SslClientAuthenticationOptions
            {
                TargetHost = targetHost,
                ClientCertificates = clientCertificates,
                EnabledSslProtocols = enabledSslProtocols,
                CertificateRevocationCheckMode = checkCertificateRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
                EncryptionPolicy = _encryptionPolicy,
            };

        // Upstream's SslAuthenticationOptions.UpdateOptions for a client: a callback set in the constructor must not be
        // replaced by a different one.
        private void UpdateOptions(SslClientAuthenticationOptions options)
        {
            if (_certValidationDelegate == null)
            {
                _certValidationDelegate = options.RemoteCertificateValidationCallback;
            }
            else if (options.RemoteCertificateValidationCallback != null && _certValidationDelegate != options.RemoteCertificateValidationCallback)
            {
                throw new InvalidOperationException(SR.Format(SR.net_conflicting_options, nameof(RemoteCertificateValidationCallback)));
            }

            if (_certSelectionDelegate == null)
            {
                _certSelectionDelegate = options.LocalCertificateSelectionCallback;
            }
            else if (options.LocalCertificateSelectionCallback != null && _certSelectionDelegate != options.LocalCertificateSelectionCallback)
            {
                throw new InvalidOperationException(SR.Format(SR.net_conflicting_options, nameof(LocalCertificateSelectionCallback)));
            }

            _targetHost = options.TargetHost ?? string.Empty;
            _revocationMode = options.CertificateRevocationCheckMode;

#pragma warning disable SYSLIB0040 // NoEncryption is obsolete
            if (options.EncryptionPolicy == EncryptionPolicy.NoEncryption)
            {
                throw new PlatformNotSupportedException(SR.net_ssl_null_encryption_NotSupported);
            }
#pragma warning restore SYSLIB0040

            if (options.ClientCertificates is { Count: > 0 })
            {
                throw new PlatformNotSupportedException(SR.net_ssl_client_certificates_NotSupported);
            }

            // The host negotiates TLS 1.2 or 1.3; a set that allows neither cannot be met.
            var protocols = options.EnabledSslProtocols;
            if (protocols != SslProtocols.None && (protocols & (SslProtocols.Tls12 | SslProtocols.Tls13)) == 0)
            {
                throw new AuthenticationException(SR.net_auth_SSPI, new NotSupportedException(SR.net_securityprotocolnotsupported));
            }
        }

        private async Task ProcessAuthenticationAsync(CancellationToken cancellationToken)
        {
            if (_handshakeCompleted)
            {
                throw new InvalidOperationException(SR.net_auth_reauth);
            }

            if (Interlocked.CompareExchange(ref _nestedAuth, NestedState.StreamInUse, NestedState.StreamNotInUse) != NestedState.StreamNotInUse)
            {
                ObjectDisposedException.ThrowIf(_nestedAuth == NestedState.StreamDisposed, this);
                throw new InvalidOperationException(SR.Format(SR.net_io_invalidnestedcall, "authenticate"));
            }

            try
            {
                if (_authenticationAttempted)
                {
                    // Upstream's context after a failed handshake cannot be reused either.
                    throw new InvalidOperationException(SR.net_auth_reauth);
                }

                _authenticationAttempted = true;
                var provider = TlsClientProviders.Default ?? throw new PlatformNotSupportedException(
                    TlsClientProviders.IsSupported ? SR.net_ssl_NotSupported : SR.net_ssl_SupportSwitchedOff);
                _session = provider.Open(InnerStream);
                try
                {
                    await _session.HandshakeAsync(_targetHost, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException e)
                {
                    throw new AuthenticationException(SR.net_auth_SSPI, e);
                }

                if (_certValidationDelegate is { } validate && !validate(this, null, null, SslPolicyErrors.None))
                {
                    throw new AuthenticationException(SR.net_ssl_io_cert_custom_validation);
                }

                _handshakeCompleted = true;
            }
            catch (Exception e)
            {
                if (!ReferenceEquals(_exception, s_disposedSentinel))
                {
                    SetException(e);
                }

                throw;
            }
            finally
            {
                Interlocked.CompareExchange(ref _nestedAuth, NestedState.StreamNotInUse, NestedState.StreamInUse);
            }
        }

        private void SetException(Exception e)
        {
            _exception ??= ExceptionDispatchInfo.Capture(e);
            CloseSession();
        }

        private void CloseInternal()
        {
            _exception = s_disposedSentinel;
            _nestedAuth = NestedState.StreamDisposed;
            CloseSession();
        }

        private void CloseSession()
        {
            var session = _session;
            _session = null;
            if (session is not null)
            {
                _ = session.DisposeAsync();
            }
        }

        private static PlatformNotSupportedException ServerNotSupported() => new(SR.net_ssl_server_NotSupported);

        private void ThrowIfExceptional()
        {
            var e = _exception;
            if (e != null)
            {
                ThrowExceptional(e);
            }

            void ThrowExceptional(ExceptionDispatchInfo e)
            {
                // If the stored exception just indicates disposal, throw a new ODE rather than the stored one, so as to not
                // continually build onto the shared exception's stack.
                ObjectDisposedException.ThrowIf(ReferenceEquals(e, s_disposedSentinel), this);
                e.Throw();
            }
        }

        private void ThrowIfExceptionalOrNotAuthenticated()
        {
            ThrowIfExceptional();
            if (!IsAuthenticated)
            {
                ThrowNotAuthenticated();
            }
        }

        private void ThrowIfExceptionalOrNotAuthenticatedOrShutdown()
        {
            ThrowIfExceptionalOrNotAuthenticated();
            if (_shutdown)
            {
                throw new InvalidOperationException(SR.net_ssl_io_already_shutdown);
            }
        }

        private static void ThrowNotAuthenticated() => throw new InvalidOperationException(SR.net_auth_noauth);

        // wasi:tls exposes no channel binding.
        private sealed class SslStreamContext : TransportContext
        {
            public override ChannelBinding? GetChannelBinding(ChannelBindingKind kind) => null;
        }
    }
}
