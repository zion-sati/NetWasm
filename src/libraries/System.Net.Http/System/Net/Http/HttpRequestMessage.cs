// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text;

namespace System.Net.Http
{
    public class HttpRequestMessage : IDisposable
    {
        internal static Version DefaultRequestVersion => HttpVersion.Version11;
        internal static HttpVersionPolicy DefaultVersionPolicy => HttpVersionPolicy.RequestVersionOrLower;

        [Flags]
        private enum MessageFlags
        {
            AlreadySent = 1,
            PropagatorStateInjectedByDiagnosticsHandler = 2,
            Disposed = 4,
            AuthDisabled = 8,
            DoNotPartitionConnectionPoolBySni = 32,
        }

        private MessageFlags _flags;

        private HttpMethod _method;
        private Uri? _requestUri;
        private HttpRequestHeaders? _headers;
        private Version _version;
        private HttpContent? _content;
        internal HttpRequestOptions? _options;

        public Version Version
        {
            get { return _version; }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                CheckDisposed();

                _version = value;
            }
        }

        public HttpContent? Content
        {
            get { return _content; }
            set
            {
                CheckDisposed();

                if (NetEventSource.Log.IsEnabled())
                {
                    if (value == null)
                    {
                        NetEventSource.ContentNull(this);
                    }
                    else
                    {
                        NetEventSource.Associate(this, value);
                    }
                }

                // It's OK to set a 'null' content, even if the method is POST/PUT.
                _content = value;
            }
        }

        public HttpMethod Method
        {
            get { return _method; }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                CheckDisposed();

                _method = value;
            }
        }

        public Uri? RequestUri
        {
            get { return _requestUri; }
            set
            {
                CheckDisposed();
                _requestUri = value;
            }
        }

        public HttpRequestHeaders Headers => _headers ??= new HttpRequestHeaders();

        internal bool HasHeaders => _headers != null;

        [Obsolete("HttpRequestMessage.Properties has been deprecated. Use Options instead.")]
        public IDictionary<string, object?> Properties => Options;

        /// <summary>
        /// Gets the collection of options to configure the HTTP request.
        /// </summary>
        public HttpRequestOptions Options => _options ??= new HttpRequestOptions();

        public HttpRequestMessage()
            : this(HttpMethod.Get, (Uri?)null)
        {
        }

        public HttpRequestMessage(HttpMethod method, Uri? requestUri)
        {
            ArgumentNullException.ThrowIfNull(method);

            // It's OK to have a 'null' request Uri. If HttpClient is used, the 'BaseAddress' will be added.
            // If there is no 'BaseAddress', sending this request message will throw.
            // Note that we also allow the string to be empty: null and empty are considered equivalent.
            _method = method;
            _requestUri = requestUri;
            _version = DefaultRequestVersion;
        }

        public HttpRequestMessage(HttpMethod method, [StringSyntax(StringSyntaxAttribute.Uri)] string? requestUri)
            : this(method, string.IsNullOrEmpty(requestUri) ? null : new Uri(requestUri, UriKind.RelativeOrAbsolute))
        {
        }

        public override string ToString()
        {
            ValueStringBuilder sb = new ValueStringBuilder(stackalloc char[512]);

            sb.Append("Method: ");
            sb.Append(_method.ToString());

            sb.Append(", RequestUri: '");
            if (_requestUri is null)
            {
                sb.Append("<null>");
            }
            else
            {
                sb.AppendSpanFormattable(_requestUri);
            }

            sb.Append("', Version: ");
            sb.AppendSpanFormattable(_version);

            sb.Append(", Content: ");
            sb.Append(_content == null ? "<null>" : _content.GetType().ToString());

            sb.Append(", Headers:");
            sb.Append(Environment.NewLine);
            HeaderUtilities.DumpHeaders(ref sb, _headers, _content?.Headers);

            return sb.ToString();
        }

        internal bool MarkAsSent()
        {
            MessageFlags previousFlags = _flags;
            _flags = previousFlags | MessageFlags.AlreadySent;
            return !previousFlags.HasFlag(MessageFlags.AlreadySent);
        }

        internal bool WasSentByHttpClient() => _flags.HasFlag(MessageFlags.AlreadySent);

        internal void MarkPropagatorStateInjectedByDiagnosticsHandler() => _flags |= MessageFlags.PropagatorStateInjectedByDiagnosticsHandler;

        internal bool WasPropagatorStateInjectedByDiagnosticsHandler() => _flags.HasFlag(MessageFlags.PropagatorStateInjectedByDiagnosticsHandler);

        internal void DisableAuth() => _flags |= MessageFlags.AuthDisabled;

        internal bool IsAuthDisabled() => _flags.HasFlag(MessageFlags.AuthDisabled);

        internal bool IsConnectionPoolPartitioningBySniDisabled() => _flags.HasFlag(MessageFlags.DoNotPartitionConnectionPoolBySni);

        // Experimental opt-in accessed via UnsafeAccessor from System.Net.Http tests. There is no product code path
        // that sets this flag today, so it is preserved from the trimmer via ILLink.Descriptors.LibraryBuild.xml.
        internal void ExperimentalDangerousDoNotPartitionConnectionPoolBySni() => _flags |= MessageFlags.DoNotPartitionConnectionPoolBySni;

        private bool Disposed
        {
            get => _flags.HasFlag(MessageFlags.Disposed);
            set
            {
                Debug.Assert(value);
                _flags |= MessageFlags.Disposed;
            }
        }

        #region IDisposable Members

        protected virtual void Dispose(bool disposing)
        {
            // The reason for this type to implement IDisposable is that it contains instances of types that implement
            // IDisposable (content).
            if (disposing && !Disposed)
            {
                Disposed = true;
                _content?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        private void CheckDisposed()
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
        }
    }
}
