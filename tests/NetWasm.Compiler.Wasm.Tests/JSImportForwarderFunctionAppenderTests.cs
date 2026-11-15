// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class JSImportForwarderFunctionAppenderTests
{
    private static readonly WasmFunctionType ForwarderType = new([CliValueKind.ManagedReference], CliValueKind.I4);

    [Fact]
    public void AppendsEachForwarderAtItsPlannedIndexWithTheManagedFunctionType()
    {
        var program = new FakeProgram();
        var import = Import(program);
        var emitter = new RecordingForwarderEmitter();
        var functions = new List<WasmFunctionDefinition> { Function("first"), Function("second") };
        var moduleTarget = CreateInstructionModuleTarget(program);

        Appender(emitter).Append(functions, 5, [import], moduleTarget, Resolver(program, 7));

        Assert.Equal(3, functions.Count);
        var forwarder = functions[2];
        Assert.Equal($"jsimport.forwarder<{import.CanonicalName}>", forwarder.Name);
        Assert.Same(ForwarderType, forwarder.Type);
        Assert.Same(emitter.Body, forwarder.Body);
        Assert.Same(moduleTarget, emitter.Target);
    }

    [Fact]
    public void RejectsAForwarderAwayFromItsPlannedIndexBeforeEmitting()
    {
        var program = new FakeProgram();
        var emitter = new RecordingForwarderEmitter();
        var functions = new List<WasmFunctionDefinition> { Function("first") };

        var exception = Assert.Throws<CompilerException>(() => Appender(emitter).Append(
            functions,
            5,
            [Import(program)],
            CreateInstructionModuleTarget(program),
            Resolver(program, 9)));

        Assert.Equal(DiagnosticCode.CompilerInvariant, exception.Diagnostic.Code);
        Assert.StartsWith("JSIMPORT_FORWARDER_INDEX", exception.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Single(functions);
        Assert.Null(emitter.Target);
    }

    private static IJSImportForwarderFunctionAppender Appender(IJSImportForwarderFunctionEmitter emitter) =>
        Assert.IsAssignableFrom<IJSImportForwarderFunctionAppender>(
            new JSImportForwarderFunctionAppender(emitter, new FixedFunctionTypes()));

    private static IFunctionIndexResolver Resolver(FakeProgram program, int forwarderIndex) =>
        CreateFunctionIndexResolver(program, new FunctionIndexMap([], [], [], [])
        {
            JSImportForwarders = ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty
                .Add(EntryKey, new(forwarderIndex)),
        });

    private static MethodInstanceModel Import(FakeProgram program)
    {
        var definition = program.GetMethod(EntryKey) with { JSImport = new("resolve", "tests") };
        return new(
            definition,
            CliTypeIdentity.Named(Assembly, "Test", "Host", isValueType: false),
            [],
            definition.Signature);
    }

    private static WasmFunctionDefinition Function(string name) =>
        new(name, ForwarderType, [WasmOpcodes.End]);

    private sealed class RecordingForwarderEmitter : IJSImportForwarderFunctionEmitter
    {
        public byte[] Body { get; } = [0, WasmOpcodes.End];
        public InstructionModuleTarget? Target { get; private set; }

        public byte[] Emit(
            MethodInstanceModel import,
            InstructionModuleTarget target,
            IFunctionIndexResolver functionIndices)
        {
            Target = target;
            return Body;
        }
    }

    private sealed class FixedFunctionTypes : IManagedMethodFunctionTypeResolver
    {
        public WasmFunctionType Resolve(MethodDefinitionModel method) => ForwarderType;

        public WasmFunctionType Resolve(MethodInstanceModel method) => ForwarderType;
    }
}
