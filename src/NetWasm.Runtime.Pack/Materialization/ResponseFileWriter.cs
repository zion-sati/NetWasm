// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class ResponseFileWriter(IResponseFileArgumentQuoter quoter) : IResponseFileWriter
{
    private readonly IResponseFileArgumentQuoter _quoter = quoter ?? throw new ArgumentNullException(nameof(quoter));

    public void Write(string path, ImmutableArray<string> arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllLines(path, arguments.Select(_quoter.Quote));
    }
}
