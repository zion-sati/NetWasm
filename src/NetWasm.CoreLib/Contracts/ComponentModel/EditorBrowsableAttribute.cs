// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// Contract adapted from dotnet/runtime System.Private.CoreLib.
// Licensed under the MIT license; see the upstream repository for the
// complete copyright notice.
using System.Diagnostics.CodeAnalysis;

namespace System.ComponentModel
{
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum |
        AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property |
        AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Delegate |
        AttributeTargets.Interface)]
    public sealed class EditorBrowsableAttribute : Attribute
    {
        public EditorBrowsableAttribute() : this(EditorBrowsableState.Always)
        {
        }

        public EditorBrowsableAttribute(EditorBrowsableState state) => State = state;

        public EditorBrowsableState State { get; }

        public override bool Equals([NotNullWhen(true)] object? obj) =>
            obj == this || obj is EditorBrowsableAttribute other && other.State == State;

        public override int GetHashCode() => State.GetHashCode();
    }
}
