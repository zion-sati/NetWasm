// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    // Adapted from the pinned System.Private.Uri parser registry. Registration
    // only changes managed scheme metadata; it never creates a host capability.
    public abstract class UriParser
    {
        private static readonly string[] BuiltInSchemes =
        {
            Uri.UriSchemeFile,
            Uri.UriSchemeFtp,
            Uri.UriSchemeSftp,
            Uri.UriSchemeFtps,
            Uri.UriSchemeGopher,
            Uri.UriSchemeHttp,
            Uri.UriSchemeHttps,
            Uri.UriSchemeWs,
            Uri.UriSchemeWss,
            Uri.UriSchemeMailto,
            Uri.UriSchemeNews,
            Uri.UriSchemeNntp,
            Uri.UriSchemeSsh,
            Uri.UriSchemeTelnet,
            Uri.UriSchemeNetTcp,
            Uri.UriSchemeNetPipe
        };

        private static UriParser?[] RegisteredParsers = new UriParser?[8];
        private static string[] RegisteredSchemes = new string[8];
        private static int[] RegisteredPorts = new int[8];
        private static int RegisteredCount;

        protected UriParser()
        {
        }

        public static bool IsKnownScheme(string schemeName)
        {
            if (schemeName is null)
            {
                throw new ArgumentNullException(nameof(schemeName));
            }

            for (var index = 0; index < BuiltInSchemes.Length; index++)
            {
                if (string.Equals(BuiltInSchemes[index], schemeName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            for (var index = 0; index < RegisteredCount; index++)
            {
                if (string.Equals(RegisteredSchemes[index], schemeName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static void Register(UriParser uriParser, string schemeName, int defaultPort)
        {
            if (uriParser is null)
            {
                throw new ArgumentNullException(nameof(uriParser));
            }
            if (!Uri.CheckSchemeName(schemeName))
            {
                throw new ArgumentException(nameof(schemeName));
            }
            if (defaultPort is < -1 or > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(defaultPort));
            }
            if (IsKnownScheme(schemeName))
            {
                throw new InvalidOperationException();
            }

            if (RegisteredCount == RegisteredSchemes.Length)
            {
                var newLength = RegisteredCount * 2;
                var parsers = new UriParser?[newLength];
                var schemes = new string[newLength];
                var ports = new int[newLength];
                for (var index = 0; index < RegisteredCount; index++)
                {
                    parsers[index] = RegisteredParsers[index];
                    schemes[index] = RegisteredSchemes[index];
                    ports[index] = RegisteredPorts[index];
                }
                RegisteredParsers = parsers;
                RegisteredSchemes = schemes;
                RegisteredPorts = ports;
            }

            RegisteredParsers[RegisteredCount] = uriParser;
            RegisteredSchemes[RegisteredCount] = schemeName;
            RegisteredPorts[RegisteredCount] = defaultPort;
            RegisteredCount++;
        }

    }
}
