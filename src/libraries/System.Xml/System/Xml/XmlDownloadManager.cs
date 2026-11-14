// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// From dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce (src/libraries/System.Private.Xml/src/System/Xml/XmlDownloadManager.cs).
// NetWasm adaptation: file URIs open as upstream's do; other URIs throw PlatformNotSupportedException rather than
// going through HttpClient, since XmlDocument.Load and XmlReader.Create reach this method for local files and the HTTP
// stack would then be part of every program that reads XML.

using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace System.Xml
{
    internal static class XmlDownloadManager
    {
        internal static Stream GetStream(Uri uri, ICredentials? credentials, IWebProxy? proxy)
        {
            if (uri.Scheme == "file")
            {
                return new FileStream(uri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
            }
            else
            {
                throw NonFileUnsupported(uri);
            }
        }

        internal static Task<Stream> GetStreamAsync(Uri uri, ICredentials? credentials, IWebProxy? proxy)
        {
            if (uri.Scheme == "file")
            {
                Uri fileUri = uri;
                return Task.FromResult<Stream>(new FileStream(fileUri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1, useAsync: true));
            }
            else
            {
                return Task.FromException<Stream>(NonFileUnsupported(uri));
            }
        }

        private static PlatformNotSupportedException NonFileUnsupported(Uri uri) =>
            new(SR.Format(SR.NetWasm_XmlUrlResolverUnsupported, uri));
    }
}
