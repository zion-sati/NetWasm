// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.IO;
using System.Runtime.InteropServices.WebAssembly;
using System.Threading;
using System.Threading.Tasks;
using NetWasm.Wit.Netwasm.Tls._1._0._0;
using WasiTlsError = NetWasm.Wit.Netwasm.Tls._1._0._0.Error;

namespace System.Net.Security
{
    // TLS through wasi:tls's client connector (the Preview 3 draft Wasmtime 50 implements behind -S tls): two
    // stream transforms the host runs, set up before the handshake. What SslStream writes reaches send through a
    // pipe and send's ciphertext is copied onto the transport; the transport itself is receive's ciphertext, read
    // until it ends (the copy never disposes it), and receive's cleartext is what SslStream reads.
    internal sealed class WasiTlsClientProvider : ITlsClientProvider
    {
        private const int CiphertextChunk = 16 * 1024;

        public static WasiTlsClientProvider Instance { get; } = new();

        private WasiTlsClientProvider() { }

        public ITlsClientSession Open(Stream transport)
        {
            ArgumentNullException.ThrowIfNull(transport);
            return new Session(transport);
        }

        private sealed class Session : ITlsClientSession
        {
            private readonly TlsOutgoingPipe _outgoing = new();
            private readonly Stream _cleartext;
            private readonly Task<WitResult<WitUnit, WasiTlsError>> _sent;
            private readonly Task<WitResult<WitUnit, WasiTlsError>> _received;
            private readonly Task _ciphertextCopy;
            private Connector? _connector;

            public Session(Stream transport)
            {
                _connector = ClientImports.CreateConnector();
                (var ciphertext, _sent) = ClientImports.Send(_connector, _outgoing);
                (_cleartext, _received) = ClientImports.Receive(_connector, transport);
                _ciphertextCopy = CopyCiphertextAsync(ciphertext, transport);
            }

            public async Task HandshakeAsync(string serverName, CancellationToken cancellationToken)
            {
                var connector = _connector ?? throw new InvalidOperationException();
                _connector = null;
                var result = await ClientImports.Connect(connector, serverName).WaitAsync(cancellationToken).ConfigureAwait(false);
                if (!result.IsOk) throw Failure(result.Error);
            }

            public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
            {
                var read = await _cleartext.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                // The cleartext ends at the server's close_notify or with a failure. The failure is reported when
                // the host settles receive's future; it settles only after the transport ends, so it is checked,
                // never awaited.
                if (read == 0 && !buffer.IsEmpty && _received.IsCompletedSuccessfully && !_received.Result.IsOk)
                {
                    throw Failure(_received.Result.Error);
                }
                return read;
            }

            public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
            {
                ThrowIfSendingFailed();
                await _outgoing.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                ThrowIfSendingFailed();
            }

            public async Task ShutdownAsync()
            {
                _outgoing.Complete();
                // The ciphertext ends once close_notify has been produced; the copy has then put it on the transport.
                await _ciphertextCopy.ConfigureAwait(false);
                ThrowIfSendingFailed();
            }

            public ValueTask DisposeAsync()
            {
                _outgoing.Complete();
                _cleartext.Dispose();
                _connector?.Dispose();
                _connector = null;
                return default;
            }

            private void ThrowIfSendingFailed()
            {
                if (_ciphertextCopy.IsFaulted) throw new IOException(_ciphertextCopy.Exception!.InnerException!.Message, _ciphertextCopy.Exception.InnerException);
                if (_sent.IsCompletedSuccessfully && !_sent.Result.IsOk) throw Failure(_sent.Result.Error);
            }

            private static IOException Failure(WasiTlsError error)
            {
                using (error)
                {
                    return new IOException(TypesImports.ToDebugString(error));
                }
            }

            private static async Task CopyCiphertextAsync(Stream ciphertext, Stream transport)
            {
                var buffer = new byte[CiphertextChunk];
                int read;
                while ((read = await ciphertext.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
                {
                    await transport.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                    await transport.FlushAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
