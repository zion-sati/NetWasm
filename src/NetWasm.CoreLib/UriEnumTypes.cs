// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    // Adapted from dotnet/runtime System.Private.Uri at commit
    // 811225a482702af7ecc35d817966bc70b88a3a23.
    public enum UriKind
    {
        RelativeOrAbsolute = 0,
        Absolute = 1,
        Relative = 2
    }

    [Flags]
    public enum UriComponents
    {
        Scheme = 0x1,
        UserInfo = 0x2,
        Host = 0x4,
        Port = 0x8,
        StrongPort = 0x80,
        NormalizedHost = 0x100,
        Path = 0x10,
        Query = 0x20,
        Fragment = 0x40,
        KeepDelimiter = 0x40000000,
        SerializationInfoString = unchecked((int)0x80000000),
        SchemeAndServer = Scheme | Host | Port,
        HostAndPort = Host | StrongPort,
        StrongAuthority = UserInfo | Host | StrongPort,
        PathAndQuery = Path | Query,
        HttpRequestUrl = Scheme | Host | Port | Path | Query,
        AbsoluteUri = Scheme | UserInfo | Host | Port | Path | Query | Fragment
    }

    public enum UriFormat
    {
        UriEscaped = 1,
        Unescaped = 2,
        SafeUnescaped = 3
    }
}
