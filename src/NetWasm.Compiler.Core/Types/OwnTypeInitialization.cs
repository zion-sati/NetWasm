// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Core.Types;

// Which methods of a precise type (not BeforeFieldInit) run only once that type's initialization was triggered, so they
// find it initialized, or initializing on this thread: invoking a static method or a constructor triggers the
// initializer (ECMA-335 II.10.5.3.1), and an instance of a reference type exists only after a constructor ran. A value
// type's instance methods also run on default instances no constructor made, and the initializer itself runs before
// its type is initialized.
public static class OwnTypeInitialization
{
    public static bool TriggeredByInvocation(MethodDefinitionModel method) =>
        (method ?? throw new ArgumentNullException(nameof(method))).Name != ".cctor" &&
        (method.IsStatic || method.Name == ".ctor");

    public static bool InitializedOnEntry(MethodDefinitionModel method, bool declaringTypeIsValueType) =>
        TriggeredByInvocation(method) || method.Name != ".cctor" && !declaringTypeIsValueType;
}
