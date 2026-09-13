using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class CanonicalAbiFunctionTypePlannerTests
{
    private readonly CanonicalAbiFunctionTypePlanner _planner =
        new CanonicalAbiFunctionTypePlanner(
            new CanonicalAbiSignaturePlanner(new CanonicalAbiTypeFlattener()));
    private readonly CanonicalAbiTypeFlattener _flattener =
        new CanonicalAbiTypeFlattener();

    [Fact]
    public void PlansThroughInjectedSignatureCapability()
    {
        var function = Function([], null);
        var signature = new CanonicalAbiCoreSignature(
            [CliValueKind.ManagedAddress],
            CliValueKind.ManagedAddress,
            [CliValueKind.I4, CliValueKind.I8],
            [CliValueKind.I4, CliValueKind.I8],
            true,
            true);
        var signatures = new RecordingSignaturePlanner(signature);
        var planner = new CanonicalAbiFunctionTypePlanner(signatures);

        var result = ((ICanonicalAbiFunctionTypePlanner)planner).Plan(
            function,
            CanonicalAbiDirection.LiftedExport);

        Assert.Equal([CliValueKind.ManagedAddress], result.CoreType.Parameters.ToArray());
        Assert.Equal(CliValueKind.ManagedAddress, result.CoreType.Result);
        Assert.Equal([CliValueKind.ManagedAddress], result.PostReturnType.Parameters.ToArray());
        Assert.Equal([CliValueKind.I4, CliValueKind.I8], result.FlatParameters.ToArray());
        Assert.Equal([CliValueKind.I4, CliValueKind.I8], result.FlatResults.ToArray());
        Assert.True(result.IndirectParameters);
        Assert.True(result.IndirectResult);
        Assert.Equal(1, signatures.Calls);
        Assert.Same(function, signatures.Function);
        Assert.Equal(CanonicalAbiDirection.LiftedExport, signatures.Direction);
    }

    [Fact]
    public void RejectsNullCapabilityDependencies()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CanonicalAbiFunctionTypePlanner(null!));
    }

    [Fact]
    public void RejectsNullFunctions()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _planner.Plan(null!, CanonicalAbiDirection.LoweredImport));
    }

    [Fact]
    public void FlattensScalarsRecordsTextListsFlagsAndResources()
    {
        var record = Type(CanonicalAbiTypeKind.Record, CliValueKind.ValueType) with
        {
            Fields =
            [
                new("number", Type(CanonicalAbiTypeKind.U64, CliValueKind.I8)),
                new("name", Type(CanonicalAbiTypeKind.Text,
                    CliValueKind.ManagedReference)),
                new("values", Type(CanonicalAbiTypeKind.List,
                    CliValueKind.ManagedReference)),
                new("flags", Type(CanonicalAbiTypeKind.Flags, CliValueKind.I8) with
                {
                    FlagsCount = 40,
                }),
                new("handle", Type(CanonicalAbiTypeKind.OwnedResource,
                    CliValueKind.ManagedReference)),
            ],
        };

        var result = _flattener.Flatten(record);

        Assert.Equal(
            [CliValueKind.I8, CliValueKind.ManagedAddress, CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress, CliValueKind.ManagedAddress,
                CliValueKind.I4, CliValueKind.I4, CliValueKind.I4],
            result.ToArray());
    }

    [Fact]
    public void UsesIndirectResultForMultipleFlatValues()
    {
        var function = Function(
            [],
            Type(CanonicalAbiTypeKind.Text, CliValueKind.ManagedReference));

        var result = _planner.Plan(
            function,
            CanonicalAbiDirection.LoweredImport);

        Assert.True(result.IndirectResult);
        Assert.Equal(
            [CliValueKind.ManagedAddress],
            result.CoreType.Parameters.ToArray());
        Assert.Equal(CliValueKind.Void, result.CoreType.Result);
        Assert.Equal([CliValueKind.ManagedAddress], result.PostReturnType.Parameters.ToArray());
    }

    [Fact]
    public void LiftedExportReturnsIndirectResultAddress()
    {
        var function = Function(
            [],
            Type(CanonicalAbiTypeKind.Text, CliValueKind.ManagedReference));

        var result = _planner.Plan(
            function,
            CanonicalAbiDirection.LiftedExport);

        Assert.True(result.IndirectResult);
        Assert.Empty(result.CoreType.Parameters);
        Assert.Equal(CliValueKind.ManagedAddress, result.CoreType.Result);
        Assert.Equal(
            [CliValueKind.ManagedAddress],
            result.PostReturnType.Parameters.ToArray());
    }

    [Fact]
    public void UsesIndirectParameterBlockPastCanonicalLimit()
    {
        var parameters = Enumerable.Range(0, 17)
            .Select(index => new CanonicalAbiParameter(
                $"p{index}",
                Type(CanonicalAbiTypeKind.S32, CliValueKind.I4)))
            .ToImmutableArray();

        var result = _planner.Plan(
            Function(parameters, null),
            CanonicalAbiDirection.LoweredImport);

        Assert.True(result.IndirectParameters);
        Assert.Equal([CliValueKind.ManagedAddress], result.CoreType.Parameters.ToArray());
    }

    [Fact]
    public void JoinsVariantPayloadSlotsAndKeepsDiscriminant()
    {
        var variant = Type(CanonicalAbiTypeKind.Variant, CliValueKind.ValueType) with
        {
            Cases =
            [
                new("integer", Type(CanonicalAbiTypeKind.S32, CliValueKind.I4)),
                new("real", Type(CanonicalAbiTypeKind.F32, CliValueKind.F4)),
                new("wide", Type(CanonicalAbiTypeKind.U64, CliValueKind.I8)),
            ],
        };

        var result = _flattener.Flatten(variant);

        Assert.Equal([CliValueKind.I4, CliValueKind.I8], result.ToArray());
    }

    private static CanonicalAbiFunction Function(
        ImmutableArray<CanonicalAbiParameter> parameters,
        CanonicalAbiType? result) => new("", "run", default, parameters, result);

    private static CanonicalAbiType Type(
        CanonicalAbiTypeKind kind,
        CliValueKind managedKind) => new(
            kind,
            CliTypeIdentity.FromStackKind(managedKind));

    private sealed class RecordingSignaturePlanner(
        CanonicalAbiCoreSignature signature) : ICanonicalAbiSignaturePlanner
    {
        public int Calls { get; private set; }
        public CanonicalAbiFunction? Function { get; private set; }
        public CanonicalAbiDirection Direction { get; private set; }

        public CanonicalAbiCoreSignature Plan(
            CanonicalAbiFunction abiFunction,
            CanonicalAbiDirection direction)
        {
            Calls++;
            Function = abiFunction;
            Direction = direction;
            return signature;
        }
    }

}
