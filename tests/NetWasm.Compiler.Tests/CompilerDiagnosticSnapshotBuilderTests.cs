// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticSnapshotBuilderTests
{
    [Fact]
    public void BuildsEachPassSnapshotWithControlFlowAndRootDecisions()
    {
        using var context = CompilerInvariantTestContext.Create();
        var operandFormatter = new CompilerCilOperandFormatter();
        var metadataBuilder = new CompilerMetadataSnapshotBuilder(operandFormatter);
        var programBuilder = new CompilerProgramSnapshotBuilder(operandFormatter);
        var layoutBuilder = new CompilerLayoutSnapshotBuilder();
        var rootBuilder = new CompilerRootMapSnapshotBuilder();
        var snapshot = context.Metadata.Snapshot;
        var symbols = MetadataCompilationTestActors.Symbols(snapshot);
        var methodBodies = MetadataCompilationTestActors.MethodBodies(snapshot);
        var metadata = Serialize(metadataBuilder.BuildMetadata(snapshot, methodBodies, symbols));
        var program = Serialize(programBuilder.BuildProgram(symbols, context.Result.Program));
        var layouts = Serialize(layoutBuilder.BuildLayouts(context.Result.Layouts));
        var roots = Serialize(rootBuilder.BuildRootMaps(context.Result.Program));

        Assert.Contains("instructions", metadata, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("successors", program, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exceptionRegions", program, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("instructionStacks", program, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("typeDescriptors", layouts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("safepoints", roots, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentNullException>(() => metadataBuilder.BuildMetadata(
            null!, methodBodies, symbols));
        Assert.Throws<ArgumentNullException>(() => metadataBuilder.BuildMetadata(
            snapshot, null!, symbols));
        Assert.Throws<ArgumentNullException>(() => metadataBuilder.BuildMetadata(
            snapshot, methodBodies, null!));
        Assert.Throws<ArgumentNullException>(() => programBuilder.BuildProgram(
            null!, context.Result.Program));
        Assert.Throws<ArgumentNullException>(() => programBuilder.BuildProgram(
            symbols, null!));
        Assert.Throws<ArgumentNullException>(() => layoutBuilder.BuildLayouts(null!));
        Assert.Throws<ArgumentNullException>(() => rootBuilder.BuildRootMaps(null!));
    }

    [Fact]
    public void MetadataSnapshotRecordsMethodsWithoutBodiesAndDecodeFailures()
    {
        using var context = CompilerInvariantTestContext.Create();
        var snapshot = context.Metadata.Snapshot;
        var bodyMethod = snapshot.EntryAssemblyMethods.First(method => method.HasBody);
        var noBody = bodyMethod with
        {
            Name = "NoBody",
            RelativeVirtualAddress = 0,
        };
        var failing = bodyMethod with { Name = "FailsToDecode" };
        var body = MetadataCompilationTestActors.MethodBodies(snapshot)
            .ReadMethodBody(bodyMethod);
        var methods = new StubMethodBodies(body, failing);
        var symbols = MetadataCompilationTestActors.Symbols(snapshot);
        var metadata = new CompilerMetadataSnapshotBuilder(
            new CompilerCilOperandFormatter()).BuildMetadata(
                snapshot with { EntryAssemblyMethods = [noBody, bodyMethod, failing] },
                methods,
                symbols);
        var text = Serialize(metadata);

        Assert.Contains("\"hasBody\":false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FailsToDecode", text, StringComparison.Ordinal);
        Assert.Contains("DecodeError", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RootMapSnapshotIncludesConstructedRoots()
    {
        using var context = CompilerInvariantTestContext.Create();
        var direct = context.Result.Program.RootMaps.First();
        var source = new RootSource(RootSourceKind.Local, 1);
        var map = new MethodRootMap(
            direct.Value.Method,
            ImmutableDictionary<RootSource, int>.Empty.Add(source, 2),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                0,
                new SafepointRootMap(0, [source], [source])));
        var constructed = context.Result.Program with
        {
            ConstructedRootMaps = ImmutableDictionary<string, MethodRootMap>.Empty
                .Add("constructed", map),
        };

        var text = Serialize(new CompilerRootMapSnapshotBuilder().BuildRootMaps(constructed));

        Assert.Contains("constructed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramSnapshotIncludesConstructedMethodsAndDispatchSites()
    {
        var definition = ValidationTestData.Method() with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4),
        };
        var instance = ValidationTestData.Instance() with
        {
            Definition = definition,
            Signature = definition.Signature,
        };
        var method = ValidationTestData.ManagedMethodBody(
            [new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None())],
            definition, instance);
        var receiver = CliTypeIdentity.Named(
            ValidationTestData.Assembly,
            "",
            "Unit",
            isValueType: false);
        var dispatch = new DispatchCallSiteModel(
            "caller",
            2,
            instance,
            [new DispatchTargetModel(receiver, instance)]);
        var typeTestTarget = CliTypeIdentity.Named(
            ValidationTestData.Assembly,
            "Tests",
            "ITypeTestTarget",
            isValueType: false);
        var typeTestSite = new TypeTestSiteModel(
            instance.CanonicalName,
            4,
            typeTestTarget,
            [receiver]);
        var delegateBinding = new ManagedDelegateBinding(
            new ManagedMethodIdentity(instance.CanonicalName),
            new ManagedMethodIdentity(instance.CanonicalName),
            instance,
            instance,
            [.. instance.Signature.ParameterSignatureTypes.Select(parameter =>
                new ManagedDelegateValueBinding(
                    parameter,
                    parameter,
                    ManagedDelegateAdaptation.Identity))],
            new ManagedDelegateValueBinding(
                instance.Signature.ReturnSignatureType,
                instance.Signature.ReturnSignatureType,
                ManagedDelegateAdaptation.Identity));
        var program = ValidationTestData.Program() with
        {
            Methods = ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty
                .Add(method.Method.Definition.Key, method),
            Fields = ImmutableHashSet<EntityKey>.Empty.Add(
                new EntityKey(ValidationTestData.Assembly, 0x04000001)),
            Exports = [new ProgramExport(
                "export",
                method.Method.Definition.Key)],
            ConstructedMethods = ImmutableDictionary<string, ManagedMethodBody>.Empty
                .Add("constructed", method),
            ConstructedAllocatingMethods = ["constructed"],
            ConstructedTypes = [receiver],
            TypeTestSites = ImmutableDictionary<string, TypeTestSiteModel>.Empty
                .Add(typeTestSite.Key, typeTestSite),
            DispatchCallSites = ImmutableDictionary<string, DispatchCallSiteModel>.Empty
                .Add(dispatch.Key, dispatch),
            DelegateBindings = [delegateBinding],
        };

        var snapshot = new CompilerProgramSnapshotBuilder(
            new CompilerCilOperandFormatter()).BuildProgram(
                ValidationTestData.Symbols(),
                program);
        var text = Serialize(snapshot);

        Assert.Contains("typeTestSites", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(typeTestTarget.CanonicalName, text, StringComparison.Ordinal);

        Assert.Contains("constructed", text, StringComparison.Ordinal);
        Assert.Contains("dispatch", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delegateBindings", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adaptation", text, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(text);
        var parameter = Assert.Single(document.RootElement.GetProperty("DelegateBindings")
            [0].GetProperty("Parameters").EnumerateArray());
        Assert.Equal(instance.Signature.ParameterSignatureTypes[0].CanonicalName,
            parameter.GetProperty("Source").GetString());
        Assert.Equal(instance.Signature.ParameterSignatureTypes[0].CanonicalName,
            parameter.GetProperty("Target").GetString());
        Assert.Equal((int)ManagedDelegateAdaptation.Identity,
            parameter.GetProperty("Adaptation").GetInt32());
        Assert.Contains("\"entryStack\":[", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProgramSnapshotPreservesReachableStacksAndRepresentsUnreachableBlocks()
    {
        var method = ValidationTestData.ManagedMethodBody(
            [
                new CilInstruction(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(42)),
                new CilInstruction(1, 2, CilOperation.Branch, new CilOperand.BranchTarget(2)),
                new CilInstruction(2, 3, CilOperation.Pop, new CilOperand.None()),
                new CilInstruction(3, 4, CilOperation.Return, new CilOperand.None()),
                new CilInstruction(4, 5, CilOperation.Nop, new CilOperand.None()),
                new CilInstruction(5, 6, CilOperation.Return, new CilOperand.None()),
            ]);
        var unreachable = method.ControlFlow.Graph.Blocks.Single(block => block.StartOffset == 4);
        var reachable = method.ControlFlow.Graph.Blocks.Single(block => block.StartOffset == 2);
        method = new ManagedMethodBody(method.Method, method.ControlFlow with
        {
            EntryStacks = method.ControlFlow.EntryStacks.Remove(unreachable.Index)
                .SetItem(reachable.Index, [CliValueKind.I4]),
        });
        var program = ValidationTestData.Program() with
        {
            Methods = ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty
                .Add(method.Method.Definition.Key, method),
        };
        var builder = Assert.IsAssignableFrom<ICompilerProgramSnapshotBuilder>(
            new CompilerProgramSnapshotBuilder(new CompilerCilOperandFormatter()));
        using var snapshot = JsonDocument.Parse(Serialize(builder.BuildProgram(
            ValidationTestData.Symbols(), program)));
        var blocks = snapshot.RootElement.GetProperty("Methods")[0].GetProperty("Blocks");
        var block = blocks.EnumerateArray().Single(value =>
            value.GetProperty("StartOffset").GetInt32() == 4);

        Assert.Empty(block.GetProperty("EntryStack").EnumerateArray());
        var reachableBlock = blocks.EnumerateArray().Single(value =>
            value.GetProperty("StartOffset").GetInt32() == 2);
        Assert.Equal("I4", Assert.Single(reachableBlock.GetProperty("EntryStack")
            .EnumerateArray()).GetString());
        Assert.Equal(3, blocks.GetArrayLength());
    }

    [Fact]
    public void ProgramSnapshotRecordsResolvedExceptionFacts()
    {
        var filtered = ValidationTestData.ManagedMethodBody(
            [
                new CilInstruction(0, 1, CilOperation.Nop, new CilOperand.None()),
                new CilInstruction(1, 2, CilOperation.EndFilter, new CilOperand.None()),
                new CilInstruction(2, 3, CilOperation.Nop, new CilOperand.None()),
                new CilInstruction(3, 4, CilOperation.Return, new CilOperand.None()),
            ],
            exceptionRegions:
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Filter,
                    0,
                    1,
                    2,
                    1,
                    null,
                    1),
            ]);
        var key = filtered.Method.Definition.Key;
        var program = ValidationTestData.Program() with
        {
            Methods = ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(key, filtered),
        };

        var snapshot = new CompilerProgramSnapshotBuilder(
            new CompilerCilOperandFormatter()).BuildProgram(
                ValidationTestData.Symbols(),
                program);

        var text = Serialize(snapshot);
        Assert.Contains("\"handlerOffset\":2", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"filterOffset\":1", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProgramSnapshotRecordsConstructedCatchIdentity()
    {
        var identity = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                ValidationTestData.Assembly,
                "Tests",
                "Failure`1",
                isValueType: false),
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var caught = ValidationTestData.ManagedMethodBody(
            [
                new CilInstruction(0, 1, CilOperation.Nop, new CilOperand.None()),
                new CilInstruction(1, 2, CilOperation.Leave, new CilOperand.BranchTarget(4)),
                new CilInstruction(2, 3, CilOperation.Pop, new CilOperand.None()),
                new CilInstruction(3, 4, CilOperation.Leave, new CilOperand.BranchTarget(4)),
                new CilInstruction(4, 5, CilOperation.Return, new CilOperand.None()),
            ],
            exceptionRegions:
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Catch,
                    0,
                    2,
                    2,
                    2,
                    null,
                    null)
                {
                    CatchTypeIdentity = identity,
                },
            ]);
        var program = ValidationTestData.Program() with
        {
            Methods = ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(
                caught.Method.Definition.Key,
                caught),
        };

        using var snapshot = JsonDocument.Parse(Serialize(
            new CompilerProgramSnapshotBuilder(new CompilerCilOperandFormatter()).BuildProgram(
                ValidationTestData.Symbols(),
                program)));
        var region = snapshot.RootElement.GetProperty("Methods")[0]
            .GetProperty("ExceptionRegions")[0];

        Assert.Equal(identity.CanonicalName,
            region.GetProperty("CatchTypeIdentity").GetString());
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value);

    private sealed class StubMethodBodies(
        CilMethodBody body,
        MethodDefinitionModel failing) : IMethodBodyReader
    {
        public CilMethodBody ReadMethodBody(MethodDefinitionModel method) =>
            method.Name == failing.Name
                ? throw new InvalidOperationException("decode failed")
                : body;

        public CilMethodBody ReadMethodBody(MethodInstanceModel method) =>
            throw new NotSupportedException();
    }
}
