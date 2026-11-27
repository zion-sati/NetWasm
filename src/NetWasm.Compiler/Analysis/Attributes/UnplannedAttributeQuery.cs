// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Analysis.Attributes;

// A reached reference to an attribute query the compiler did not answer (NW1019): the method whose code holds it, the
// IL offset, what the reference does (the call with its target and filter, a delegate, a method handle), why the query
// stays at its run-time path, when that path throws PlatformNotSupportedException, and what answers it at compile time
// instead.
internal sealed record UnplannedAttributeQuery(string Method, int IlOffset, string Query, string Reason, string Outcome,
    string Remedy);
