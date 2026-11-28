// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// The locals that hold where the frame arenas stood, for a landing pad that returns them there (LandingPadRestore): the
// root and value arenas' cursors (addresses; the runtime finds the value list's head from the value cursor, on the
// exceptional path) and the stack-trace depth (an i32). A protected block whose restore reads marks as it begins has its
// own; a method has one set read after its prologue.
internal sealed record RegionMarkLocals(int Root, int ValueCursor, int StackTrace);
