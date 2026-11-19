// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Net.Http.HPack;

namespace System.Net.Http.Headers
{
    internal sealed partial class KnownHeader
    {
#if TARGET_WASI
        // NetWasm: no HTTP/3 (WASI has no QUIC), and the HTTP/2 name is encoded when SocketsHttpHandler's HTTP/2 first
        // writes the header rather than when the header is created, so a program that never speaks HTTP/2 links no HPACK
        // encoder and encodes nothing at startup. The static table index is found then too, by name, rather than kept in
        // every header: it is the index upstream passes each known header, the first static entry with the header's
        // name (pseudo-headers, which upstream passes none, excluded).
        private byte[]? _http2EncodedName;

        partial void Initialize(int? http2StaticTableIndex, int? http3StaticTableIndex)
        {
        }

        public byte[] Http2EncodedName => _http2EncodedName ??= StaticTableIndex(Name) is int index ?
            HPackEncoder.EncodeLiteralHeaderFieldWithoutIndexingToAllocatedArray(index) :
            HPackEncoder.EncodeLiteralHeaderFieldWithoutIndexingNewNameToAllocatedArray(Name);

        private static int? StaticTableIndex(string name)
        {
            if (name.StartsWith(':'))
            {
                return null;
            }
            for (int index = 0; index < H2StaticTable.Count; index++)
            {
                if (System.Text.Ascii.EqualsIgnoreCase(H2StaticTable.Get(index).Name, name))
                {
                    return index + 1;
                }
            }
            return null;
        }
#else
        [MemberNotNull(nameof(Http2EncodedName))]
        [MemberNotNull(nameof(Http3EncodedName))]
        partial void Initialize(int? http2StaticTableIndex, int? http3StaticTableIndex)
        {
            Http2EncodedName = http2StaticTableIndex.HasValue ?
                HPackEncoder.EncodeLiteralHeaderFieldWithoutIndexingToAllocatedArray(http2StaticTableIndex.GetValueOrDefault()) :
                HPackEncoder.EncodeLiteralHeaderFieldWithoutIndexingNewNameToAllocatedArray(Name);

            Http3EncodedName = http3StaticTableIndex.HasValue ?
                QPack.QPackEncoder.EncodeLiteralHeaderFieldWithStaticNameReferenceToArray(http3StaticTableIndex.GetValueOrDefault()) :
                QPack.QPackEncoder.EncodeLiteralHeaderFieldWithoutNameReferenceToArray(Name);
        }

        public byte[] Http2EncodedName { get; private set; }
        public byte[] Http3EncodedName { get; private set; }
#endif
    }
}
