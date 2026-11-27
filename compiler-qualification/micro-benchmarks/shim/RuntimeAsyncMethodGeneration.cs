// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace System.Runtime.CompilerServices
{
    // Roslyn's per-method choice, by this name, when runtime async is on: false compiles the method as a state machine.
    // dotnet/runtime's async tests declare it the same way (src/tests/async/RuntimeAsyncMethodGenerationAttribute.cs).
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class RuntimeAsyncMethodGenerationAttribute(bool runtimeAsync) : Attribute
    {
        public bool RuntimeAsync { get; } = runtimeAsync;
    }
}
