// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace NetWasm.Hosting.Execution;

/// <summary>A transport-safe execution failure without an exception object or stack.</summary>
/// <param name="Detail">
/// The single-line <c>name: message</c> of the error the host caught, when there was one.
/// It is diagnostic text for people; programs branch on <paramref name="Code"/>.
/// </param>
public sealed record NetWasmExecutionFailure(
    NetWasmFailurePhase Phase,
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null)
{
    /// <summary>The longest detail a failure carries, in UTF-16 code units.</summary>
    public const int MaxDetailLength = 1024;
}
