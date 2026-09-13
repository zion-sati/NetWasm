// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    // URI builder state is pure managed data. Materializing Uri never opens
    // or resolves the endpoint represented by the components.
    public class UriBuilder
    {
        private string _scheme = Uri.UriSchemeHttp;
        private string _userName = string.Empty;
        private string _password = string.Empty;
        private string _host = "localhost";
        private int _port = -1;
        private string _path = "/";
        private string _query = string.Empty;
        private string _fragment = string.Empty;

        public UriBuilder()
        {
        }

        public UriBuilder(string uri) : this(new Uri(uri, UriKind.Absolute))
        {
        }

        public UriBuilder(Uri uri)
        {
            if (uri is null)
            {
                throw new ArgumentNullException(nameof(uri));
            }
            if (!uri.IsAbsoluteUri)
            {
                throw new ArgumentException(nameof(uri));
            }

            _scheme = uri.Scheme;
            _host = uri.Host;
            _port = uri.Port;
            _path = uri.AbsolutePath.Length == 0 ? "/" : uri.AbsolutePath;
            _query = RemovePrefix(uri.Query, '?');
            _fragment = RemovePrefix(uri.Fragment, '#');

            var userInfo = uri.UserInfo;
            if (userInfo.Length != 0)
            {
                var separator = userInfo.IndexOf(':');
                if (separator < 0)
                {
                    _userName = userInfo;
                }
                else
                {
                    _userName = userInfo.Substring(0, separator);
                    _password = userInfo.Substring(separator + 1);
                }
            }
        }

        public UriBuilder(string schemeName, string hostName)
            : this(schemeName, hostName, -1, "/", string.Empty)
        {
        }

        public UriBuilder(string schemeName, string hostName, int portNumber)
            : this(schemeName, hostName, portNumber, "/", string.Empty)
        {
        }

        public UriBuilder(string schemeName, string hostName, int portNumber, string pathValue)
            : this(schemeName, hostName, portNumber, pathValue, string.Empty)
        {
        }

        public UriBuilder(
            string schemeName,
            string hostName,
            int portNumber,
            string pathValue,
            string extraValue)
        {
            Scheme = schemeName;
            Host = hostName;
            Port = portNumber;
            Path = pathValue;
            ApplyExtra(extraValue);
        }

        public string Scheme
        {
            get => _scheme;
            set
            {
                if (value is null)
                {
                    throw new ArgumentNullException(nameof(value));
                }
                if (!Uri.CheckSchemeName(value))
                {
                    throw new ArgumentException(nameof(value));
                }
                _scheme = value.ToLowerInvariant();
            }
        }

        public string UserName
        {
            get => _userName;
            set => _userName = value ?? string.Empty;
        }

        public string Password
        {
            get => _password;
            set => _password = value ?? string.Empty;
        }

        public string Host
        {
            get => _host;
            set => _host = value ?? throw new ArgumentNullException(nameof(value));
        }

        public int Port
        {
            get => _port;
            set
            {
                if (value < -1 || value > 65535)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                _port = value;
            }
        }

        public string Path
        {
            get => EscapePath(_path);
            set => _path = string.IsNullOrEmpty(value) ? "/" : value;
        }

        public string Query
        {
            get => _query.Length == 0 ? string.Empty : "?" + _query;
            set => _query = RemovePrefix(value ?? string.Empty, '?');
        }

        public string Fragment
        {
            get => _fragment.Length == 0 ? string.Empty : "#" + _fragment;
            set => _fragment = RemovePrefix(value ?? string.Empty, '#');
        }

        public Uri Uri => new Uri(BuildRawUri(), UriKind.Absolute);

        public override string ToString()
        {
            var result = Uri.AbsoluteUri;
            if (_port >= 0)
            {
                result = AddExplicitPort(result, _port);
            }
            return result;
        }

        public override bool Equals(object? obj) =>
            obj is UriBuilder other && string.Equals(ToString(), other.ToString(), StringComparison.Ordinal);

        public override int GetHashCode() => ToString().GetHashCode();

        private string BuildRawUri()
        {
            var result = _scheme + "://";
            if (_userName.Length != 0 || _password.Length != 0)
            {
                result += _userName;
                if (_password.Length != 0)
                {
                    result += ":" + _password;
                }
                result += "@";
            }
            result += _host;
            if (_port >= 0)
            {
                result += ":" + _port;
            }
            result += _path.Length == 0 ? "/" : _path;
            if (_query.Length != 0)
            {
                result += "?" + _query;
            }
            if (_fragment.Length != 0)
            {
                result += "#" + _fragment;
            }
            return result;
        }

        private void ApplyExtra(string extraValue)
        {
            var extra = extraValue ?? string.Empty;
            var fragment = extra.IndexOf('#');
            if (fragment >= 0)
            {
                Fragment = extra.Substring(fragment + 1);
                extra = extra.Substring(0, fragment);
            }
            Query = extra;
        }

        private static string RemovePrefix(string value, char prefix) =>
            value.Length > 0 && value[0] == prefix ? value.Substring(1) : value;

        private static string EscapePath(string value)
        {
            var result = string.Empty;
            for (var index = 0; index < value.Length; index++)
            {
                result += value[index] == ' ' ? "%20" : value[index].ToString();
            }
            return result;
        }

        private static string AddExplicitPort(string value, int port)
        {
            var authorityStart = value.IndexOf("://", StringComparison.Ordinal);
            if (authorityStart < 0)
            {
                return value;
            }
            authorityStart += 3;
            var authorityEnd = value.Length;
            for (var index = authorityStart; index < value.Length; index++)
            {
                if (value[index] is '/' or '?' or '#')
                {
                    authorityEnd = index;
                    break;
                }
            }
            var authority = value.Substring(authorityStart, authorityEnd - authorityStart);
            var hostStart = authority.LastIndexOf('@') + 1;
            var host = authority.Substring(hostStart);
            if (host.EndsWith(":" + port, StringComparison.Ordinal))
            {
                return value;
            }
            var insert = authorityStart + hostStart + host.Length;
            return value.Substring(0, insert) + ":" + port + value.Substring(insert);
        }
    }
}
