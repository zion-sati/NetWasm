// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Net.Http
{
    // NetWasm: in place of upstream's HttpConnectionPool.Http3.cs. WASI has no QUIC, so HTTP/3 is never enabled, and
    // Alt-Svc, which only advertises HTTP/3 endpoints, is never followed; the pool's assignments that disable them have
    // nothing to do.
    internal sealed partial class HttpConnectionPool
    {
        private bool _http3Enabled
        {
            get => false;
            set { }
        }

        private bool _altSvcEnabled
        {
            get => false;
            set { }
        }

        private void ProcessAltSvc(HttpResponseMessage response)
        {
        }

        internal void HandleAltSvc(System.Collections.Generic.IEnumerable<string> altSvcHeaderValues, TimeSpan? responseAge)
        {
        }
    }
}
