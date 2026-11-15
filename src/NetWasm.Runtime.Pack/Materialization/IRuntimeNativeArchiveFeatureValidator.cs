// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeArchiveFeatureValidator
{
    void Validate(RuntimeNativeArchiveFeatureValidationRequest request);
}

// One static native library's members, linked on their own, validated against the profile.
internal sealed record RuntimeNativeArchiveFeatureValidationRequest(string LibraryName, RuntimeNativeModuleValidationRequest Module);
