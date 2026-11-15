// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;

namespace NetWasm.Hosting.Deployment;

public interface IContentHasher
{
    string Hash(ReadOnlyMemory<byte> content);
}
