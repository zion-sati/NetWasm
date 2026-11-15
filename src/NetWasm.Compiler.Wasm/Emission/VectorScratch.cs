// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Wasm.Emission;

// The two locals a 12-byte vector access uses. Wasm has no dup, and a Vector3
// moves as its low 8 bytes and its third lane, each at the same address: a load
// reads the address twice, a store the address and the value twice.
internal readonly record struct VectorScratch(int Address, int Value);
