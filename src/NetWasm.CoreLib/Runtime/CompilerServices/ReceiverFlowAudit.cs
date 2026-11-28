// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace System.Runtime.CompilerServices
{
    // Qualification builds only (NetWasmReceiverFlowAudit): the compiler calls Fail where a dispatched call reaches a
    // receiver outside the set its receiver flow analysis found, with the receiver's type id. It writes nothing itself
    // (a plain core module has no standard error to write to); the exception carries the call's message, and a run
    // reports it unless the program catches every exception there.
    internal static class ReceiverFlowAudit
    {
        [DoesNotReturn]
        internal static void Fail(string message, int typeId) =>
            throw new InvalidProgramException(message + " (receiver type id " + typeId.ToString() + ")");
    }
}
