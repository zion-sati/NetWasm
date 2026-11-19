// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Net.Http.HPack;
using System.Net.Http.QPack;
using System.Text;

// NetWasm: no HTTP/3 (WASI has no QUIC), so no QPACK encoding of the method; H3StaticTable's indexes still classify it.
// The classification (MustHaveRequestBody, IsConnect, IsHead) is computed from the index when SocketsHttpHandler asks,
// and a method made by name resolves its index then, rather than every HttpMethod storing them: an HttpClient program
// that never reaches SocketsHttpHandler carries neither.

namespace System.Net.Http
{
    public partial class HttpMethod
    {
        private byte[]? _http1EncodedBytes;
        private byte[]? _http2EncodedBytes;
        // A method made by name resolves its index on first use.
        private const int UnresolvedHttp3Index = int.MinValue;

        private int _http3Index;

        private int Http3Index => _http3Index != UnresolvedHttp3Index
            ? _http3Index
            : _http3Index = GetKnownMethod(Method)?._http3Index ?? 0;

        internal bool MustHaveRequestBody => Http3Index is not (H3StaticTable.MethodConnect or H3StaticTable.MethodHead or
            H3StaticTable.MethodGet or H3StaticTable.MethodOptions or H3StaticTable.MethodDelete);
        internal bool IsConnect => Http3Index == H3StaticTable.MethodConnect;
        internal bool IsHead => Http3Index == H3StaticTable.MethodHead;

        partial void Initialize(string method) => _http3Index = UnresolvedHttp3Index;

        partial void Initialize(int http3Index) => _http3Index = http3Index;

        internal byte[] Http1EncodedBytes => _http1EncodedBytes ?? CreateHttp1EncodedBytes();
        internal byte[] Http2EncodedBytes => _http2EncodedBytes ?? CreateHttp2EncodedBytes();

        private byte[] CreateHttp1EncodedBytes()
        {
            HttpMethod? knownMethod = GetKnownMethod(Method);
            byte[]? bytes = knownMethod?._http1EncodedBytes;

            if (bytes is null)
            {
                Debug.Assert(Ascii.IsValid(Method));

                string method = knownMethod?.Method ?? Method;
                bytes = new byte[method.Length + 1];
                Ascii.FromUtf16(method, bytes, out _);
                bytes[^1] = (byte)' ';

                knownMethod?._http1EncodedBytes = bytes;
            }

            _http1EncodedBytes = bytes;
            return bytes;
        }

        private byte[] CreateHttp2EncodedBytes()
        {
            HttpMethod? knownMethod = GetKnownMethod(Method);
            byte[]? bytes = knownMethod?._http2EncodedBytes;

            if (bytes is null)
            {
                bytes = Http3Index switch
                {
                    H3StaticTable.MethodGet => [0x80 | H2StaticTable.MethodGet],
                    H3StaticTable.MethodPost => [0x80 | H2StaticTable.MethodPost],
                    _ => HPackEncoder.EncodeLiteralHeaderFieldWithoutIndexingToAllocatedArray(H2StaticTable.MethodGet, knownMethod?.Method ?? Method)
                };

                knownMethod?._http2EncodedBytes = bytes;
            }

            _http2EncodedBytes = bytes;
            return bytes;
        }
    }
}
