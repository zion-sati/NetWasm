// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace System.Runtime.InteropServices.Streams;

// The streams NetWasm reaches through WASI imports, as the phantom type argument of the stream
// invoker contracts. Each stream's invokers implement only their own stream's contracts, so a call
// on one stream's contract reaches no other stream's imports at any optimization level: at O0 an
// interface call dispatches to every allocated implementation of the method it names, and the
// levels lane checks that O0 reaches the same host imports as O1. Never instantiated.
internal abstract class StandardInputStreamKind
{
    private StandardInputStreamKind()
    {
    }
}

internal abstract class StandardOutputStreamKind
{
    private StandardOutputStreamKind()
    {
    }
}

internal abstract class StandardErrorStreamKind
{
    private StandardErrorStreamKind()
    {
    }
}

internal abstract class ReadOnlyFileStreamKind
{
    private ReadOnlyFileStreamKind()
    {
    }
}
