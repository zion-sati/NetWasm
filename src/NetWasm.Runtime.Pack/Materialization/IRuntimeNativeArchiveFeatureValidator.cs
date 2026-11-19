// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeArchiveFeatureValidator
{
    RuntimeNativeArchiveFeatures Validate(RuntimeNativeArchiveFeatureValidationRequest request);
}

// One static native library's members, linked on their own, validated against the profile.
internal sealed record RuntimeNativeArchiveFeatureValidationRequest(string LibraryName, RuntimeNativeModuleValidationRequest Module);

// What an archive needs beyond the profile: Wasm exception handling in its legacy encoding (try, catch), which the
// final link translates to the profile's exnref encoding, and the C++ exception runtime, for code that throws or
// catches C++ exceptions.
internal sealed record RuntimeNativeArchiveFeatures(bool UsesLegacyExceptions, bool UsesCxxExceptions = false);
