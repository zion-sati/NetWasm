using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal sealed record ManagedByRefAggregatePromotionRequest(
    StructuredMethod Method,
    ManagedMethodIdentity Caller,
    IReadOnlyDictionary<ManagedCallSiteKey, ManagedCallSite> CallSites,
    bool Enabled);
