// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Runtime.Pack.Materialization;

/// <summary>Formats the excerpt of a failed tool's output that its failure message ends with.</summary>
internal interface IToolOutputExcerptFormatter
{
    string Format(string output);
}
