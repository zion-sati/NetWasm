// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public interface IBrowserLinkPathValidator
{
    void Validate(string path);
}

/// <summary>Validates logical POSIX paths, without resolving or granting access to host paths.</summary>
public sealed class BrowserLinkPathValidator : IBrowserLinkPathValidator
{
    public void Validate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var relative = path[0] == '/' ? path[1..] : path;
        if (relative.Length == 0 || relative[0] == '-')
            throw new ArgumentException("A browser artifact requires a normalized virtual file path.", nameof(path));
        foreach (var segment in relative.Split('/'))
        {
            if (segment is "" or "." or "..")
                throw new ArgumentException("A browser artifact path contains an empty or relative segment.", nameof(path));
            foreach (var character in segment)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '.'))
                    throw new ArgumentException("A browser artifact path contains an unsupported character.", nameof(path));
            }
        }
    }
}
