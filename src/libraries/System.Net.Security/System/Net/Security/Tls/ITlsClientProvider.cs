// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Security
{
    // NetWasm: TLS is a WASI platform feature behind one seam, as wasi:sockets and wasi:http are. SslStream speaks
    // to a provider; a change in WASI's TLS interface replaces only the provider.
    internal interface ITlsClientProvider
    {
        // A client session over the transport, ready for its handshake: it encrypts what it is given onto the
        // transport, and decrypts what the transport delivers.
        ITlsClientSession Open(Stream transport);
    }

    // One client connection. A failure throws IOException with the provider's description.
    internal interface ITlsClientSession : IAsyncDisposable
    {
        // The handshake with the server named; the provider validates the server's certificate for that name
        // against the host's trusted roots.
        Task HandshakeAsync(string serverName, CancellationToken cancellationToken);

        // Decrypted data, 0 once the server has closed its side.
        ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

        // Completes when the session has taken all of the data; the buffer is not used afterwards.
        ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);

        // Closes the sending side (close_notify) once everything written before it has reached the transport.
        Task ShutdownAsync();
    }

    // The platform's TLS client: wasi:tls on WASI; none in a browser, which has no TLS a program can drive, nor in a
    // program built with NetWasmTlsSupport false for a host without wasi:tls. Both checks fold at compile time, so
    // such a program imports no wasi:tls.
    internal static class TlsClientProviders
    {
        internal const string SupportSwitch = "NetWasm.Net.Security.TlsSupport";

        [FeatureSwitchDefinition(SupportSwitch)]
        internal static bool IsSupported { get; } = !AppContext.TryGetSwitch(SupportSwitch, out bool supported) || supported;

        internal static ITlsClientProvider? Default => !IsSupported || OperatingSystem.IsBrowser() ? null : WasiTlsClientProvider.Instance;
    }
}
