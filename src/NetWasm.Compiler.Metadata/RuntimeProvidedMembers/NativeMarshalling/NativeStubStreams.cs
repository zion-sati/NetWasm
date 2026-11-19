// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// A marshalling stub's streams: each parameter's, in order, and its result's.
internal sealed record NativeStubStreams(ImmutableArray<NativeArgumentStreams> Arguments, NativeResultStreams Result);
