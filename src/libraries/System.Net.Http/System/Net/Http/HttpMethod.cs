// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace System.Net.Http
{
    public partial class HttpMethod : IEquatable<HttpMethod>
    {
        private readonly string _method;
        private int _hashcode;

        public static HttpMethod Get { get; } = new("GET", validated: true);
        public static HttpMethod Put { get; } = new("PUT", validated: true);
        public static HttpMethod Post { get; } = new("POST", validated: true);
        public static HttpMethod Delete { get; } = new("DELETE", validated: true);
        public static HttpMethod Head { get; } = new("HEAD", validated: true);
        public static HttpMethod Options { get; } = new("OPTIONS", validated: true);
        public static HttpMethod Trace { get; } = new("TRACE", validated: true);
        public static HttpMethod Patch { get; } = new("PATCH", validated: true);

        // NetWasm: QUERY and CONNECT are newer than the netstandard contract; they stay internal for the known-method
        // lookup that names methods in telemetry.
        internal static HttpMethod Query { get; } = new("QUERY", validated: true);

        internal static HttpMethod Connect { get; } = new("CONNECT", validated: true);

        public string Method => _method;

        public HttpMethod(string method)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(method);
            if (!HttpRuleParser.IsToken(method))
            {
                throw new FormatException(SR.net_http_httpmethod_format_error);
            }

            _method = method;
        }

        // NetWasm: upstream passes each known method's HTTP/3 static-table index to SocketsHttpHandler-specific
        // initialization, which WASI does not have.
        private HttpMethod(string method, bool validated)
        {
            _method = method;
        }

        public bool Equals([NotNullWhen(true)] HttpMethod? other) =>
            other is not null &&
            string.Equals(_method, other._method, StringComparison.OrdinalIgnoreCase);

        public override bool Equals([NotNullWhen(true)] object? obj) =>
            obj is HttpMethod method &&
            Equals(method);

        public override int GetHashCode()
        {
            if (_hashcode == 0)
            {
                _hashcode = StringComparer.OrdinalIgnoreCase.GetHashCode(_method);
            }

            return _hashcode;
        }

        public override string ToString() => _method;

        public static bool operator ==(HttpMethod? left, HttpMethod? right) =>
            left is null || right is null
                ? ReferenceEquals(left, right)
                : left.Equals(right);

        public static bool operator !=(HttpMethod? left, HttpMethod? right) =>
            !(left == right);

        internal static HttpMethod? GetKnownMethod(ReadOnlySpan<char> method)
        {
            if (method.Length >= 3) // 3 == smallest known method
            {
                HttpMethod? match = (method[0] | 0x20) switch
                {
                    'c' => Connect,
                    'd' => Delete,
                    'g' => Get,
                    'h' => Head,
                    'o' => Options,
                    'p' => method.Length switch
                    {
                        3 => Put,
                        4 => Post,
                        _ => Patch,
                    },
                    'q' => Query,
                    't' => Trace,
                    _ => null,
                };

                if (match is not null &&
                    method.Equals(match._method, StringComparison.OrdinalIgnoreCase))
                {
                    return match;
                }
            }

            return null;
        }
    }
}
