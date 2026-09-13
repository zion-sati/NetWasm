using System.Collections.Immutable;
using System.Reflection;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.Tests;

public sealed class CanonicalAbiSignaturePlannerTests
{
    private readonly CanonicalAbiSignaturePlanner _planner =
        new(new CanonicalAbiTypeFlattener());
    private readonly CanonicalAbiTypeFlattener _flattener = new();

    private static readonly EntityKey Method = new(
        new AssemblyIdentity("Fixture"), 0x06000001);

    [Fact]
    public void PlansDirectAndIndirectParametersAndResults()
    {
        var direct = Function(
            [new("value", Type(CanonicalAbiTypeKind.U32))],
            Type(CanonicalAbiTypeKind.U64));
        var directSignature = Plan(_planner,
            direct, CanonicalAbiDirection.LoweredImport);
        Assert.Equal([CliValueKind.I4], directSignature.Parameters.ToArray());
        Assert.Equal(CliValueKind.I8, directSignature.Result);
        Assert.Equal([CliValueKind.I4], directSignature.FlatParameters.ToArray());
        Assert.Equal([CliValueKind.I8], directSignature.FlatResults.ToArray());
        Assert.False(directSignature.IndirectParameters);
        Assert.False(directSignature.IndirectResult);
        Assert.Equal([CliValueKind.I8],
            CanonicalAbiSignaturePlanner.PostReturnParameters(directSignature).ToArray());
        Assert.Equal("value", direct.Parameters[0].Name);
        Assert.Same(direct.Parameters[0].Type, direct.Parameters[0].Type);

        var withoutResult = Plan(_planner,
            Function([], null), CanonicalAbiDirection.LoweredImport);
        Assert.Equal(CliValueKind.Void, withoutResult.Result);
        Assert.Empty(CanonicalAbiSignaturePlanner.PostReturnParameters(withoutResult));

        var wideParameters = Enumerable.Range(0, 17)
            .Select(_ => new CanonicalAbiParameter(
                "value", Type(CanonicalAbiTypeKind.U32)))
            .ToImmutableArray();
        var wideResult = Type(CanonicalAbiTypeKind.Text);
        var lowered = Plan(_planner,
            Function(wideParameters, wideResult), CanonicalAbiDirection.LoweredImport);
        Assert.Equal(
            [CliValueKind.ManagedAddress, CliValueKind.ManagedAddress],
            lowered.Parameters.ToArray());
        Assert.Equal(CliValueKind.Void, lowered.Result);
        Assert.True(lowered.IndirectParameters);
        Assert.True(lowered.IndirectResult);

        var lifted = Plan(_planner,
            Function(wideParameters, wideResult), CanonicalAbiDirection.LiftedExport);
        Assert.Equal([CliValueKind.ManagedAddress], lifted.Parameters.ToArray());
        Assert.Equal(CliValueKind.ManagedAddress, lifted.Result);
        Assert.Equal([CliValueKind.ManagedAddress],
            CanonicalAbiSignaturePlanner.PostReturnParameters(lifted).ToArray());
    }

    [Fact]
    public void PlansThroughTheInjectedFlatteningCapability()
    {
        var parameterType = Type(CanonicalAbiTypeKind.U32);
        var resultType = Type(CanonicalAbiTypeKind.U64);
        var flattener = new RecordingFlattener(
            (parameterType, ImmutableArray.Create(CliValueKind.I4)),
            (resultType, ImmutableArray.Create(CliValueKind.I8)));
        var planner = new CanonicalAbiSignaturePlanner(flattener);

        var signature = Plan(
            planner,
            Function([new("input", parameterType)], resultType),
            CanonicalAbiDirection.LiftedExport);

        Assert.Equal([CliValueKind.I4], signature.FlatParameters.ToArray());
        Assert.Equal([CliValueKind.I8], signature.FlatResults.ToArray());
        Assert.Equal([parameterType, resultType], flattener.Requests);
    }

    [Fact]
    public void FlattensEveryCanonicalAbiShape()
    {
        var i4Kinds = new[]
        {
            CanonicalAbiTypeKind.Bool,
            CanonicalAbiTypeKind.S8,
            CanonicalAbiTypeKind.U8,
            CanonicalAbiTypeKind.S16,
            CanonicalAbiTypeKind.U16,
            CanonicalAbiTypeKind.S32,
            CanonicalAbiTypeKind.U32,
            CanonicalAbiTypeKind.Character,
            CanonicalAbiTypeKind.Enum,
            CanonicalAbiTypeKind.OwnedResource,
            CanonicalAbiTypeKind.BorrowedResource,
        };
        Assert.Empty(Flatten(_flattener, Type(CanonicalAbiTypeKind.Unit)));
        foreach (var kind in i4Kinds)
        {
            Assert.Equal([CliValueKind.I4], Flatten(_flattener, Type(kind)).ToArray());
        }
        foreach (var kind in new[] { CanonicalAbiTypeKind.S64, CanonicalAbiTypeKind.U64 })
        {
            Assert.Equal([CliValueKind.I8], Flatten(_flattener, Type(kind)).ToArray());
        }
        Assert.Equal([CliValueKind.F4],
            Flatten(_flattener, Type(CanonicalAbiTypeKind.F32)).ToArray());
        Assert.Equal([CliValueKind.F8],
            Flatten(_flattener, Type(CanonicalAbiTypeKind.F64)).ToArray());
        foreach (var kind in new[] { CanonicalAbiTypeKind.Text, CanonicalAbiTypeKind.List })
        {
            Assert.Equal(
                [CliValueKind.ManagedAddress, CliValueKind.ManagedAddress],
                Flatten(_flattener, Type(kind)).ToArray());
        }

        var alias = Type(CanonicalAbiTypeKind.Alias) with
        {
            ElementType = Type(CanonicalAbiTypeKind.F32),
        };
        Assert.Equal([CliValueKind.F4], Flatten(_flattener, alias).ToArray());
        foreach (var kind in new[] { CanonicalAbiTypeKind.Record, CanonicalAbiTypeKind.Tuple })
        {
            var aggregate = Type(kind) with
            {
                Fields =
                [
                    new("left", Type(CanonicalAbiTypeKind.U32)),
                    new("right", Type(CanonicalAbiTypeKind.F64)),
                ],
            };
            Assert.Equal([CliValueKind.I4, CliValueKind.F8],
                Flatten(_flattener, aggregate).ToArray());
        }

        var option = Type(CanonicalAbiTypeKind.Option) with
        {
            ElementType = Type(CanonicalAbiTypeKind.U64),
        };
        Assert.Equal([CliValueKind.I4, CliValueKind.I8],
            Flatten(_flattener, option).ToArray());
        var result = Type(CanonicalAbiTypeKind.Result) with
        {
            SuccessType = Type(CanonicalAbiTypeKind.U32),
            ErrorType = Type(CanonicalAbiTypeKind.F64),
        };
        Assert.Equal([CliValueKind.I4, CliValueKind.I8],
            Flatten(_flattener, result).ToArray());
        Assert.Equal([CliValueKind.I4], Flatten(_flattener,
            Type(CanonicalAbiTypeKind.Variant)).ToArray());
        Assert.Equal([CliValueKind.I4], Flatten(_flattener,
            Type(CanonicalAbiTypeKind.Flags)).ToArray());
        Assert.Equal(
            [CliValueKind.I4, CliValueKind.I4, CliValueKind.I4],
            Flatten(_flattener,
                Type(CanonicalAbiTypeKind.Flags) with { FlagsCount = 65 }).ToArray());
    }

    [Fact]
    public void JoinsEveryFlatPayloadCombination()
    {
        var types = new Dictionary<CliValueKind, CanonicalAbiType>
        {
            [CliValueKind.I4] = Type(CanonicalAbiTypeKind.U32),
            [CliValueKind.I8] = Type(CanonicalAbiTypeKind.U64),
            [CliValueKind.F4] = Type(CanonicalAbiTypeKind.F32),
            [CliValueKind.F8] = Type(CanonicalAbiTypeKind.F64),
            [CliValueKind.ManagedAddress] = Type(CanonicalAbiTypeKind.Text),
        };
        foreach (var (leftKind, leftType) in types)
        {
            foreach (var (rightKind, rightType) in types)
            {
                var variant = Type(CanonicalAbiTypeKind.Variant) with
                {
                    Cases = [new("left", leftType), new("right", rightType)],
                };
                var flattened = Flatten(_flattener, variant);
                Assert.Equal(ExpectedJoin(leftKind, rightKind), flattened[1]);
            }
        }
    }

    [Fact]
    public void RejectsInvalidInputsAndImpossibleJoins()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Plan(_planner, null!, CanonicalAbiDirection.LoweredImport));
        Assert.Throws<ArgumentNullException>(() => Flatten(_flattener, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new CanonicalAbiSignaturePlanner(null!));
        Assert.Throws<ArgumentNullException>(() =>
            CanonicalAbiSignaturePlanner.PostReturnParameters(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Flatten(_flattener, Type((CanonicalAbiTypeKind)int.MaxValue)));

        var join = typeof(CanonicalAbiTypeFlattener).GetMethod(
            "Join", BindingFlags.NonPublic | BindingFlags.Static)!;
        var error = Assert.Throws<TargetInvocationException>(() =>
            join.Invoke(null, [CliValueKind.Void, CliValueKind.I4]));
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    private static CliValueKind ExpectedJoin(
        CliValueKind left,
        CliValueKind right)
    {
        if (left == right)
        {
            return left;
        }
        if (left == CliValueKind.ManagedAddress || right == CliValueKind.ManagedAddress)
        {
            var other = left == CliValueKind.ManagedAddress ? right : left;
            return other is CliValueKind.I4 or CliValueKind.F4
                ? CliValueKind.ManagedAddress
                : CliValueKind.I8;
        }
        if ((left == CliValueKind.I4 && right == CliValueKind.F4) ||
            (left == CliValueKind.F4 && right == CliValueKind.I4))
        {
            return CliValueKind.I4;
        }
        if (left == CliValueKind.I8 || right == CliValueKind.I8)
        {
            return CliValueKind.I8;
        }
        if ((left == CliValueKind.F4 && right == CliValueKind.F8) ||
            (left == CliValueKind.F8 && right == CliValueKind.F4))
        {
            return CliValueKind.F8;
        }
        return CliValueKind.I8;
    }

    private static CanonicalAbiFunction Function(
        ImmutableArray<CanonicalAbiParameter> parameters,
        CanonicalAbiType? result) => new("", "run", Method, parameters, result);

    private static CanonicalAbiCoreSignature Plan<TPlanner>(
        TPlanner planner,
        CanonicalAbiFunction function,
        CanonicalAbiDirection direction)
        where TPlanner : ICanonicalAbiSignaturePlanner => planner.Plan(function, direction);

    private static ImmutableArray<CliValueKind> Flatten<TFlattener>(
        TFlattener flattener,
        CanonicalAbiType type)
        where TFlattener : ICanonicalAbiTypeFlattener => flattener.Flatten(type);

    private static CanonicalAbiType Type(CanonicalAbiTypeKind kind) =>
        new(kind, CliTypeIdentity.FromStackKind(CliValueKind.Unknown));

    private sealed class RecordingFlattener(
        params (CanonicalAbiType Type, ImmutableArray<CliValueKind> Values)[] entries) :
        ICanonicalAbiTypeFlattener
    {
        private readonly Dictionary<CanonicalAbiType, ImmutableArray<CliValueKind>> _values =
            entries.ToDictionary(entry => entry.Type, entry => entry.Values);
        private readonly List<CanonicalAbiType> _requests = [];

        public IReadOnlyList<CanonicalAbiType> Requests => _requests;

        public ImmutableArray<CliValueKind> Flatten(CanonicalAbiType type)
        {
            _requests.Add(type);
            return _values[type];
        }
    }
}
