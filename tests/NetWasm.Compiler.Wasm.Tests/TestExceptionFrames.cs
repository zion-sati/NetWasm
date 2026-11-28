// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

// The exception frame strategies and step emitter as the service collection composes them.
internal static class TestExceptionFrames
{
    public static ExceptionFrameStrategySelector Strategies(ITargetLayout layouts, IRuntimeImportResolver imports)
    {
        var calls = new ExceptionFrameCallEmitter(layouts, imports, new StructuredExceptionGroupKeyFactory());
        var lazy = new LazyRootFrameEmitter(layouts, imports);
        var registered = new RegisteredExceptionFrame(calls, lazy);
        return new ExceptionFrameStrategySelector(
            new MarkedExceptionRegion(new LandingPadRestoreEmitter(layouts, imports), lazy),
            new PassThroughExceptionFrame(layouts, calls, new PassThroughCatchLocalFinder(), new LandingPadRestoreEmitter(layouts, imports), lazy),
            new PassThroughExceptionFrame(layouts, calls, new PassThroughCatchLocalFinder(), new LandingPadRestoreEmitter(layouts, imports), lazy,
                registersUnderFilters: false),
            new FilterGuardedCatchFrame(layouts, calls, new LandingPadRestoreEmitter(layouts, imports), lazy, registered),
            new UnregisteredCatchFrame(calls, new LandingPadRestoreEmitter(layouts, imports), lazy),
            registered);
    }

    public static ExceptionFrameStepEmitter Steps(ITargetLayout layouts, IRuntimeImportResolver imports) =>
        new(Strategies(layouts, imports), new LazyRootFrameEmitter(layouts, imports));
}
