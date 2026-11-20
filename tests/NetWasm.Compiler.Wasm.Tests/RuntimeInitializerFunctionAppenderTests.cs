// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class RuntimeInitializerFunctionAppenderTests
{
    [Fact]
    public void WithoutModuleInitializersOneNeverInlinedFunctionServesEntriesAndInstantiation()
    {
        var plan = TestRuntimeInitialization.Create(512) with
        {
            InitializerFunctionIndex = null,
            StateInitializerFunctionIndex = null,
        };
        var state = new RecordingStateEmitter();
        var entries = new RecordingEntryEmitter();
        List<WasmFunctionDefinition> functions = [Existing()];

        var appended = new RuntimeInitializerFunctionAppender(state, entries).Append(functions, 7, plan);

        Assert.Equal(8, appended.InitializerFunctionIndex);
        Assert.Equal(8, appended.StateInitializerFunctionIndex);
        Assert.Same(plan, Assert.Single(state.Plans));
        Assert.Empty(entries.Calls);
        Assert.Equal(2, functions.Count);
        AssertInitializer(functions[^1], "runtime.initialize", RecordingStateEmitter.Body);
    }

    // A module initializer may wait, which a component's instantiation never may, so instantiation
    // calls the state initializer and every entry calls the function that also runs the module
    // initializers.
    [Fact]
    public void ModuleInitializersRunFromASecondFunctionThatEntriesCall()
    {
        ImmutableArray<ModuleInitializerCall> initializers = [new(256, 37), new(260, 41)];
        var plan = TestRuntimeInitialization.Create(512) with { ModuleInitializers = initializers };
        var state = new RecordingStateEmitter();
        var entries = new RecordingEntryEmitter();
        List<WasmFunctionDefinition> functions = [Existing()];

        var appended = new RuntimeInitializerFunctionAppender(state, entries).Append(functions, 7, plan);

        Assert.Equal(8, appended.StateInitializerFunctionIndex);
        Assert.Equal(9, appended.InitializerFunctionIndex);
        Assert.Equal(initializers, appended.ModuleInitializers);
        var call = Assert.Single(entries.Calls);
        Assert.Equal(8, call.State);
        Assert.Equal(initializers, call.Initializers);
        AssertInitializer(functions[1], "runtime.initialize", RecordingStateEmitter.Body);
        AssertInitializer(functions[2], "runtime.initialize-entry", RecordingEntryEmitter.Body);
    }

    [Fact]
    public void RejectsMissingArguments()
    {
        var appender = new RuntimeInitializerFunctionAppender(new RecordingStateEmitter(), new RecordingEntryEmitter());
        var plan = TestRuntimeInitialization.Create(512);

        Assert.Throws<ArgumentNullException>(() => appender.Append(null!, 0, plan));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append([], -1, plan));
        Assert.Throws<ArgumentNullException>(() => appender.Append([], 0, null!));
    }

    private static WasmFunctionDefinition Existing() =>
        new("existing", WasmFunctionType.Create(CliValueKind.Void), [0, 0x0b]);

    // Initialization runs once per entry and is cold; inlining would copy it into every entry.
    private static void AssertInitializer(WasmFunctionDefinition function, string name, byte[] body)
    {
        Assert.Equal(name, function.Name);
        Assert.Equal(WasmFunctionType.Create(CliValueKind.Void), function.Type);
        Assert.Equal(body, function.Body);
        Assert.True(function.IsNoInlining);
        Assert.False(function.IsAggressiveInlining);
    }

    private sealed class RecordingStateEmitter : IRuntimeInitializerFunctionEmitter
    {
        public static readonly byte[] Body = [0, 0x0b];

        public List<RuntimeInitializationPlan> Plans { get; } = [];

        public byte[] Emit(RuntimeInitializationPlan plan)
        {
            Plans.Add(plan);
            return Body;
        }
    }

    private sealed class RecordingEntryEmitter : IEntryInitializerFunctionEmitter
    {
        public static readonly byte[] Body = [0, 0x01, 0x0b];

        public List<(int State, ImmutableArray<ModuleInitializerCall> Initializers)> Calls { get; } = [];

        public byte[] Emit(int stateInitializerIndex, ImmutableArray<ModuleInitializerCall> moduleInitializers)
        {
            Calls.Add((stateInitializerIndex, moduleInitializers));
            return Body;
        }
    }
}
