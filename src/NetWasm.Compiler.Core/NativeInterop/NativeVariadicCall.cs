// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Core.NativeInterop;

// A P/Invoke that calls a C variadic function through one of several fixed-signature declarations, as SQLitePCLRaw
// declares sqlite3_config: its first FixedParameters parameters are the function's own, and the rest are its variadic
// arguments, which the stub stores in a buffer it passes by address, as C's caller does in Wasm's C ABI.
public sealed record NativeVariadicCall(int FixedParameters);
