// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeLinkedImportValidator
{
    void Validate(
        RuntimeNativeValidationProfile profile,
        RuntimeLinkedModule module,
        RuntimeNativeCallbackSupport? callbackSupport = null,
        IReadOnlySet<string>? importModules = null);
}
