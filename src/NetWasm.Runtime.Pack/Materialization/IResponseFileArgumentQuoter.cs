// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Runtime.Pack.Materialization;

/// <summary>Quotes one argument for a response file, so the tool reads it back exactly.</summary>
internal interface IResponseFileArgumentQuoter
{
    string Quote(string argument);
}
