// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Core.ManagedExecutables;

public enum ManagedExecutableParameterShape
{
    None,
    StringArray,
}

public enum ManagedExecutableReturnShape
{
    Void,
    ExitCode,
}

public enum ManagedExecutableCompletionShape
{
    Synchronous,
    Asynchronous,
}

public sealed record ManagedExecutableEntryPointAbi(
    ManagedExecutableParameterShape ParameterShape,
    ManagedExecutableReturnShape ReturnShape,
    ManagedExecutableCompletionShape CompletionShape =
        ManagedExecutableCompletionShape.Synchronous);
