// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Adapted from the pinned System.Private.Uri behavior for the portable
// NetWasm profile. Parsing remains pure and has no host, filesystem, DNS, or
// transport capability.

using System.UriParsing;

namespace System
{
    public partial class Uri : IEquatable<Uri>, IFormattable
    {
        private static readonly IUriInputValidator InputValidator =
            UriCompositionRoot.CreateInputValidator();
        private static readonly IUriTextParser TextParser =
            UriCompositionRoot.CreateTextParser();
        private static readonly IUriRelativeResolver RelativeResolver =
            UriCompositionRoot.CreateRelativeResolver();
        private static readonly IUriAuthorityFormatter AuthorityFormatter =
            UriCompositionRoot.CreateAuthorityFormatter();
        private static readonly IUriHostClassifier HostClassifier =
            UriCompositionRoot.CreateHostClassifier();
        private static readonly IUriSegmentReader SegmentReader =
            UriCompositionRoot.CreateSegmentReader();
        private static readonly IUriEqualityComparer EqualityComparer =
            UriCompositionRoot.CreateEqualityComparer();
        private static readonly IUriSchemeEndReader SchemeEndReader =
            UriCompositionRoot.CreateSchemeEndReader();
        private static readonly IDefaultUriPortResolver DefaultUriPortResolver =
            UriCompositionRoot.CreateDefaultUriPortResolver();
        private static readonly IUriEscaper Escaper = UriCompositionRoot.CreateEscaper();
        private static readonly IUriUnescaper Unescaper = UriCompositionRoot.CreateUnescaper();
        private static readonly IUriIdnNormalizer IdnNormalizer = UriCompositionRoot.CreateIdnNormalizer();

        public const string SchemeDelimiter = "://";
        public const string UriSchemeFile = "file";
        public const string UriSchemeFtp = "ftp";
        public const string UriSchemeSftp = "sftp";
        public const string UriSchemeFtps = "ftps";
        public const string UriSchemeGopher = "gopher";
        public const string UriSchemeHttp = "http";
        public const string UriSchemeHttps = "https";
        public const string UriSchemeWs = "ws";
        public const string UriSchemeWss = "wss";
        public const string UriSchemeMailto = "mailto";
        public const string UriSchemeNews = "news";
        public const string UriSchemeNntp = "nntp";
        public const string UriSchemeSsh = "ssh";
        public const string UriSchemeTelnet = "telnet";
        public const string UriSchemeNetTcp = "net.tcp";
        public const string UriSchemeNetPipe = "net.pipe";
        public const string UriSchemeData = "data";

        private readonly string _originalString;
        private readonly string _scheme;
        private readonly string _userInfo;
        private readonly string _host;
        private readonly string _path;
        private readonly string _query;
        private readonly string _fragment;
        private readonly int _port;
        private readonly bool _isAbsolute;
        private readonly bool _userEscaped;
        private readonly string _canonicalString;

        public Uri(string uriString)
            : this(uriString, UriKind.RelativeOrAbsolute)
        {
        }

        public Uri(string uriString, UriKind uriKind)
        {
            InputValidator.Validate(uriString, uriKind);
            var parsed = TextParser.Parse(uriString, uriKind);
            _originalString = parsed.OriginalString;
            _scheme = parsed.Scheme;
            _userInfo = parsed.UserInfo;
            _host = parsed.Host;
            _path = parsed.Path;
            _query = parsed.Query;
            _fragment = parsed.Fragment;
            _port = parsed.Port;
            _isAbsolute = parsed.IsAbsolute;
            _userEscaped = parsed.UserEscaped;
            _canonicalString = parsed.CanonicalString;
        }

        public Uri(string uriString, bool dontEscape)
            : this(uriString, UriKind.RelativeOrAbsolute)
        {
            _userEscaped = dontEscape || _userEscaped;
        }

        public Uri(string uriString, in UriCreationOptions creationOptions)
            : this(uriString, UriKind.Absolute)
        {
        }

        public Uri(Uri baseUri, string? relativeUri)
        {
            if (baseUri is null)
            {
                throw new ArgumentNullException(nameof(baseUri));
            }
            if (relativeUri is null)
            {
                throw new ArgumentNullException(nameof(relativeUri));
            }

            var combined = RelativeResolver.Resolve(
                baseUri,
                relativeUri);
            var parsed = new Uri(combined, UriKind.RelativeOrAbsolute);
            _originalString = parsed._originalString;
            _scheme = parsed._scheme;
            _userInfo = parsed._userInfo;
            _host = parsed._host;
            _path = parsed._path;
            _query = parsed._query;
            _fragment = parsed._fragment;
            _port = parsed._port;
            _isAbsolute = parsed._isAbsolute;
            _userEscaped = parsed._userEscaped;
            _canonicalString = parsed._canonicalString;
        }

        public Uri(Uri baseUri, Uri relativeUri)
            : this(baseUri, relativeUri?.OriginalString)
        {
        }

        public Uri(Uri baseUri, string relativeUri, bool dontEscape)
            : this(baseUri, relativeUri)
        {
            _userEscaped = dontEscape || _userEscaped;
        }

        public string AbsolutePath => _isAbsolute
            ? EscapeExistingEscapes(_path)
            : throw new InvalidOperationException();
        public string AbsoluteUri => _isAbsolute
            ? GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped)
            : throw new InvalidOperationException();
        public string Authority => _isAbsolute
            ? AuthorityFormatter.Format(
                string.Empty,
                _host,
                _port >= 0 && _port != DefaultUriPortResolver.Resolve(_scheme) ? _port : -1,
                includePort: true)
            : throw new InvalidOperationException();
        public string DnsSafeHost => HostNameType == UriHostNameType.IPv6 &&
            _host.Length > 1 && _host[0] == '[' && _host[_host.Length - 1] == ']'
                ? _host.Substring(1, _host.Length - 2)
                : Host;
        public string Fragment => _isAbsolute
            ? (_fragment.Length == 0 ? string.Empty : "#" + EscapeExistingEscapes(_fragment))
            : throw new InvalidOperationException();
        public string Host => _isAbsolute ? _host : throw new InvalidOperationException();
        public UriHostNameType HostNameType =>
            HostClassifier.Classify(_host);
        public string IdnHost => IdnNormalizer.Normalize(Host);
        public bool IsAbsoluteUri => _isAbsolute;
        public bool IsDefaultPort =>
            _port < 0 || _port == DefaultUriPortResolver.Resolve(_scheme);
        public bool IsFile => string.Equals(_scheme, UriSchemeFile, StringComparison.Ordinal);
        public bool IsLoopback =>
            string.Equals(_host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            _host == "127.0.0.1" || _host == "[::1]";
        public bool IsUnc => IsFile && _host.Length > 0;
        public string LocalPath => AbsolutePath;
        public string OriginalString => _originalString;
        public string PathAndQuery => _isAbsolute
            ? EscapeExistingEscapes(_path) +
                (_query.Length == 0 ? string.Empty : "?" + EscapeExistingEscapes(_query))
            : throw new InvalidOperationException();
        public int Port => _isAbsolute
            ? (_port < 0 ? DefaultUriPortResolver.Resolve(_scheme) : _port)
            : throw new InvalidOperationException();
        public string Query => _isAbsolute
            ? (_query.Length == 0 ? string.Empty : "?" + EscapeExistingEscapes(_query))
            : throw new InvalidOperationException();
        public string Scheme => _isAbsolute ? _scheme : throw new InvalidOperationException();
        public string[] Segments => SegmentReader.Read(_path, _isAbsolute);
        public bool UserEscaped => _userEscaped;
        public string UserInfo => _isAbsolute ? _userInfo : throw new InvalidOperationException();

        public override string ToString() => _canonicalString;

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        public bool Equals(Uri? other) => other is not null &&
            EqualityComparer.AreEqual(this, other);

        public override bool Equals(object? comparand) => comparand is Uri other && Equals(other);

        public override int GetHashCode() => _canonicalString.GetHashCode();

        public static bool operator ==(Uri? left, Uri? right) => left?.Equals(right) ?? right is null;

        public static bool operator !=(Uri? left, Uri? right) => !(left == right);

        public static bool TryCreate(string? uriString, UriKind uriKind, out Uri? result)
        {
            try
            {
                result = uriString is null ? null : new Uri(uriString, uriKind);
                return result is not null;
            }
            catch (UriFormatException)
            {
                result = null;
                return false;
            }
        }

        public static bool TryCreate(
            string? uriString,
            in UriCreationOptions creationOptions,
            out Uri? result) => TryCreate(uriString, UriKind.Absolute, out result);

        public static bool TryCreate(Uri? baseUri, string? relativeUri, out Uri? result)
        {
            try
            {
                result = baseUri is null || relativeUri is null
                    ? null
                    : new Uri(baseUri, relativeUri);
                return result is not null;
            }
            catch (UriFormatException)
            {
                result = null;
                return false;
            }
        }

        public static bool TryCreate(Uri? baseUri, Uri? relativeUri, out Uri? result) =>
            TryCreate(baseUri, relativeUri?.OriginalString, out result);

        public bool IsBaseOf(Uri uri)
        {
            if (uri is null || !_isAbsolute || !uri._isAbsolute)
            {
                return false;
            }
            return string.Equals(_scheme, uri._scheme, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_host, uri._host, StringComparison.OrdinalIgnoreCase) &&
                uri._path.StartsWith(_path, StringComparison.Ordinal);
        }

        public Uri MakeRelativeUri(Uri uri)
        {
            if (uri is null)
            {
                throw new ArgumentNullException(nameof(uri));
            }
            if (!_isAbsolute || !uri._isAbsolute ||
                !string.Equals(_scheme, uri._scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_host, uri._host, StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }

            var common = 0;
            var length = _path.Length < uri._path.Length ? _path.Length : uri._path.Length;
            while (common < length && _path[common] == uri._path[common])
            {
                common++;
            }
            return new Uri(uri._path.Substring(common).TrimStart('/'), UriKind.Relative);
        }

        public static UriHostNameType CheckHostName(string? name) =>
            name is null
                ? UriHostNameType.Unknown
                : HostClassifier.Classify(name);

        public static bool CheckSchemeName(string? schemeName)
        {
            if (schemeName is null || schemeName.Length == 0)
            {
                return false;
            }
            return SchemeEndReader.Read(schemeName + ":") == schemeName.Length;
        }

        public bool IsWellFormedOriginalString() =>
            _originalString.Length > 0 && IsWellFormedText(_originalString);

        public static bool IsWellFormedUriString(string? uriString, UriKind uriKind)
        {
            return TryCreate(uriString, uriKind, out var result) &&
                result is not null && result.IsWellFormedOriginalString();
        }

        public string GetLeftPart(UriPartial part) => part switch
        {
            UriPartial.Scheme => _scheme + "://",
            UriPartial.Authority => _scheme + "://" + AuthorityWithUserInfo,
            UriPartial.Path => _scheme + "://" + AuthorityWithUserInfo + AbsolutePath,
            UriPartial.Query => _scheme + "://" + AuthorityWithUserInfo + PathAndQuery,
            _ => throw new ArgumentOutOfRangeException(nameof(part))
        };

        public string GetComponents(UriComponents components, UriFormat format)
        {
            if (!_isAbsolute)
            {
                throw new InvalidOperationException();
            }
            if (format is < UriFormat.UriEscaped or > UriFormat.SafeUnescaped)
            {
                throw new ArgumentOutOfRangeException(nameof(format));
            }

            var result = string.Empty;
            var keepDelimiter = (components & UriComponents.KeepDelimiter) != 0;
            var includeHost = (components & (UriComponents.Host | UriComponents.NormalizedHost)) != 0;
            var hasAuthority = (components & (UriComponents.UserInfo | UriComponents.Host |
                UriComponents.NormalizedHost | UriComponents.Port | UriComponents.StrongPort)) != 0;
            var hasPath = (components & UriComponents.Path) != 0;
            var hasQuery = (components & UriComponents.Query) != 0;
            var hasFragment = (components & UriComponents.Fragment) != 0;
            var hasScheme = (components & UriComponents.Scheme) != 0;
            if (hasScheme)
            {
                result += _scheme;
                if (hasAuthority)
                {
                    result += "://";
                }
                else if (hasPath || hasQuery || hasFragment)
                {
                    result += ":";
                }
            }
            if ((components & UriComponents.UserInfo) != 0)
            {
                result += FormatComponent(_userInfo, format);
                if (_userInfo.Length > 0 && hasAuthority)
                {
                    result += "@";
                }
            }
            if (includeHost)
            {
                result += (components & UriComponents.Host) != 0 ? _host : DnsSafeHost;
            }
            if ((components & (UriComponents.Port | UriComponents.StrongPort)) != 0 &&
                (_port >= 0 || (components & UriComponents.StrongPort) != 0))
            {
                var port = _port >= 0 ? _port : Port;
                result += ":" + port;
            }
            if (hasPath)
            {
                var path = FormatComponent(_path, format);
                if (!hasAuthority && !hasScheme && !hasQuery && !hasFragment &&
                    path.StartsWith("/", StringComparison.Ordinal))
                {
                    path = path.Substring(1);
                }
                result += path;
            }
            if (hasQuery)
            {
                if (_query.Length > 0 && (keepDelimiter || hasPath))
                {
                    result += "?";
                }
                result += FormatComponent(_query, format);
            }
            if (hasFragment)
            {
                if (_fragment.Length > 0 && (keepDelimiter || hasPath || hasQuery))
                {
                    result += "#";
                }
                result += FormatComponent(_fragment, format);
            }
            return result;
        }

        public static string EscapeDataString(string stringToEscape) =>
            Escaper.Escape(stringToEscape);

        public static string EscapeDataString(ReadOnlySpan<char> charsToEscape) =>
            Escaper.Escape(charsToEscape.ToString());

        public static string UnescapeDataString(string stringToUnescape) =>
            Unescaper.Unescape(stringToUnescape);

        public static string UnescapeDataString(ReadOnlySpan<char> charsToUnescape) =>
            Unescaper.Unescape(charsToUnescape.ToString());

        public static bool IsHexDigit(char character) =>
            IsAsciiDigit(character) || character is >= 'A' and <= 'F' or >= 'a' and <= 'f';

        public static int FromHex(char digit)
        {
            if (digit is >= '0' and <= '9') return digit - '0';
            if (digit is >= 'A' and <= 'F') return digit - 'A' + 10;
            if (digit is >= 'a' and <= 'f') return digit - 'a' + 10;
            throw new ArgumentException(nameof(digit));
        }

        private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';

        private string AuthorityWithUserInfo
        {
            get
            {
                var authority = _userInfo.Length == 0 ? string.Empty : _userInfo + "@";
                return authority + Authority;
            }
        }

        private string FormatComponent(string value, UriFormat format) => format switch
        {
            UriFormat.UriEscaped => EscapeExistingEscapes(value),
            UriFormat.Unescaped or UriFormat.SafeUnescaped => Unescaper.Unescape(value),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        private string EscapeExistingEscapes(string value)
        {
            var result = string.Empty;
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] == '%' && index + 2 < value.Length &&
                    IsHexDigit(value[index + 1]) && IsHexDigit(value[index + 2]))
                {
                    result += value.Substring(index, 3);
                    index += 2;
                    continue;
                }
                var character = value[index];
                if (character == ' ')
                {
                    result += "%20";
                }
                else if (character > 0x7f)
                {
                    var length = character is >= '\uD800' and <= '\uDBFF' &&
                        index + 1 < value.Length &&
                        value[index + 1] is >= '\uDC00' and <= '\uDFFF' ? 2 : 1;
                    result += Escaper.Escape(value.Substring(index, length));
                    index += length - 1;
                }
                else
                {
                    result += character.ToString();
                }
            }
            return result;
        }

        private static bool IsWellFormedText(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsWhiteSpace(character) || char.IsControl(character))
                {
                    return false;
                }
                if (character == '%' &&
                    (index + 2 >= value.Length ||
                     !IsHexDigit(value[index + 1]) ||
                     !IsHexDigit(value[index + 2])))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
