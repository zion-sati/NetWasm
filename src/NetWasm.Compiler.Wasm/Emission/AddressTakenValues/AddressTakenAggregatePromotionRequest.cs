using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal sealed record AddressTakenAggregatePromotionRequest(
    StructuredMethod Method,
    ManagedMethodIdentity Caller,
    IEntryLookup<ManagedCallSiteKey, ManagedCallSite> CallSites,
    IEntryLookup<string, ManagedByRefMethodEffects> ByRefEffects,
    bool Enabled);
