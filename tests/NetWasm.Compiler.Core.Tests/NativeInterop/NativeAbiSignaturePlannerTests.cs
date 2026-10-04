using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Core.Tests.NativeInterop;

public sealed class NativeAbiSignaturePlannerTests
{
    [Theory]
    [InlineData("i4", CliValueKind.I4)]
    [InlineData("u4", CliValueKind.I4)]
    [InlineData("i8", CliValueKind.I8)]
    [InlineData("u8", CliValueKind.I8)]
    [InlineData("f4", CliValueKind.F4)]
    [InlineData("f8", CliValueKind.F8)]
    [InlineData("nativeint", CliValueKind.NativeInt)]
    [InlineData("nativeuint", CliValueKind.NativeInt)]
    public void BothProfilesPreserveScalarSignatures(string name, CliValueKind kind)
    {
        var type = CliTypeIdentity.Primitive(name, kind);
        foreach (var profile in new[] { NativeAbiSignatureKind.Import, NativeAbiSignatureKind.Callback })
        {
            var result = Planner().Plan(MethodSignatureModel.Create(type, type), profile, "callback");
            Assert.Equal(type, result.PhysicalSignature.ReturnSignatureType);
            Assert.Equal(type, Assert.Single(result.PhysicalSignature.ParameterSignatureTypes));
        }
    }

    [Theory]
    [InlineData(NativeAbiSignatureKind.Import)]
    [InlineData(NativeAbiSignatureKind.Callback)]
    public void VoidAndClosedPointersUseOnePhysicalSignatureAuthority(NativeAbiSignatureKind kind)
    {
        var pointer = CliTypeIdentity.UnmanagedPointer(CliTypeIdentity.FromStackKind(CliValueKind.Void));
        var result = Planner().Plan(MethodSignatureModel.Create(pointer, pointer), kind, "callback");
        Assert.Equal(CliValueKind.NativeInt, result.PhysicalSignature.ReturnType);
        Assert.Equal(CliValueKind.NativeInt, Assert.Single(result.PhysicalSignature.ParameterTypes));
        Assert.Equal(CliValueKind.Void,
            Planner().Plan(MethodSignatureModel.Create(CliValueKind.Void), kind, "callback").PhysicalSignature.ReturnType);
    }

    [Theory]
    [InlineData("i4", CliValueKind.I4)]
    [InlineData("u4", CliValueKind.I4)]
    [InlineData("i8", CliValueKind.I8)]
    [InlineData("u8", CliValueKind.I8)]
    [InlineData("f4", CliValueKind.F4)]
    [InlineData("f8", CliValueKind.F8)]
    [InlineData("nativeint", CliValueKind.NativeInt)]
    [InlineData("nativeuint", CliValueKind.NativeInt)]
    public void ScalarByrefsBelongOnlyToImportParameters(string name, CliValueKind kind)
    {
        var byref = CliTypeIdentity.ManagedByReference(CliTypeIdentity.Primitive(name, kind));
        var signature = MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), byref);
        Assert.Equal(CliValueKind.NativeInt,
            Assert.Single(Planner().Plan(signature, NativeAbiSignatureKind.Import, "import").PhysicalSignature.ParameterTypes));
        Reject(signature, NativeAbiSignatureKind.Callback);
        Reject(MethodSignatureModel.Create(byref), NativeAbiSignatureKind.Import);
        Reject(MethodSignatureModel.Create(byref), NativeAbiSignatureKind.Callback);
    }

    [Theory]
    [InlineData(0x01, false)]
    [InlineData(0x09, false)]
    [InlineData(0x09, true)]
    public void ClosedCdeclFunctionPointersLowerOnlyForImportParameters(
        byte header,
        bool encodedConvention)
    {
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var nativeInteger = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
        var callbackReturn = encodedConvention
            ? CliTypeIdentity.Modified(
                integer,
                Convention("CallConvCdecl"),
                isRequired: false)
            : integer;
        var functionPointer = CliTypeIdentity.FunctionPointer(new(
            header,
            0,
            2,
            MethodSignatureModel.Create(callbackReturn, integer, nativeInteger)));
        var import = MethodSignatureModel.Create(
            CliTypeIdentity.FromStackKind(CliValueKind.Void),
            functionPointer);

        var plan = Planner().Plan(
            import,
            NativeAbiSignatureKind.Import,
            "native-register");

        Assert.Equal(
            CliValueKind.NativeInt,
            Assert.Single(plan.PhysicalSignature.ParameterTypes));
        Assert.Equal(functionPointer, Assert.Single(plan.Parameters).Value.LogicalType);
        Reject(import, NativeAbiSignatureKind.Callback);
        Reject(MethodSignatureModel.Create(functionPointer), NativeAbiSignatureKind.Import);
    }

    [Fact]
    public void FunctionPointerParametersRejectManagedVariableGenericAndUnsupportedConventions()
    {
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var voidType = CliTypeIdentity.FromStackKind(CliValueKind.Void);
        var validSignature = MethodSignatureModel.Create(voidType, integer);
        var invalid = new[]
        {
            CliTypeIdentity.FunctionPointer(new(0x00, 0, 1, validSignature)),
            CliTypeIdentity.FunctionPointer(new(0x05, 0, 1, validSignature)),
            CliTypeIdentity.FunctionPointer(new(0x09, 1, 1, validSignature)),
            CliTypeIdentity.FunctionPointer(new(0x09, 0, 0, validSignature)),
            CliTypeIdentity.FunctionPointer(new(
                0x09,
                0,
                1,
                MethodSignatureModel.Create(
                    CliTypeIdentity.Modified(
                        voidType,
                        Convention("CallConvSuppressGCTransition"),
                        isRequired: false),
                    integer))),
            CliTypeIdentity.FunctionPointer(new(
                0x01,
                0,
                1,
                MethodSignatureModel.Create(
                    CliTypeIdentity.Modified(
                        integer,
                        Convention("CallConvCdecl"),
                        isRequired: false),
                    integer))),
            CliTypeIdentity.FunctionPointer(new(
                0x09,
                0,
                1,
                MethodSignatureModel.Create(
                    CliTypeIdentity.Modified(
                        CliTypeIdentity.Modified(
                            integer,
                            Convention("CallConvCdecl"),
                            isRequired: false),
                        Convention("CallConvCdecl"),
                        isRequired: false),
                    integer))),
            CliTypeIdentity.FunctionPointer(new(
                0x09,
                0,
                1,
                MethodSignatureModel.Create(
                    voidType,
                    CliTypeIdentity.GenericParameter(method: false, 0)))),
        };

        foreach (var functionPointer in invalid)
        {
            Reject(
                MethodSignatureModel.Create(voidType, functionPointer),
                NativeAbiSignatureKind.Import);
        }
    }

    [Theory]
    [InlineData(NativeAbiSignatureKind.Import)]
    [InlineData(NativeAbiSignatureKind.Callback)]
    public void UnsupportedShapesRejectWithTheirOriginalMethodIdentity(NativeAbiSignatureKind kind)
    {
        var assembly = new AssemblyIdentity("NativeFixture");
        var aggregate = CliTypeIdentity.Named(assembly, "Fixture", "Pair", true);
        var reference = CliTypeIdentity.Named(assembly, "Fixture", "Node", false);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        foreach (var type in new[]
        {
            CliTypeIdentity.Primitive("bool", CliValueKind.I4),
            CliTypeIdentity.Primitive("char", CliValueKind.I4),
            CliTypeIdentity.Primitive("i1", CliValueKind.I4),
            CliTypeIdentity.Primitive("u1", CliValueKind.I4),
            CliTypeIdentity.Primitive("i2", CliValueKind.I4),
            CliTypeIdentity.Primitive("u2", CliValueKind.I4),
            CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference),
            CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference),
            aggregate,
            reference,
            CliTypeIdentity.SzArray(scalar),
            CliTypeIdentity.Array(scalar, 2),
            CliTypeIdentity.UnmanagedPointer(CliTypeIdentity.GenericParameter(false, 0)),
            CliTypeIdentity.Modified(scalar, reference, false),
        })
        {
            Reject(MethodSignatureModel.Create(type), kind);
            Reject(MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), type), kind);
            Reject(MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void),
                CliTypeIdentity.ManagedByReference(type)), kind);
        }
        Reject(MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void),
            CliTypeIdentity.FromStackKind(CliValueKind.Void)), kind);
        Reject(MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void),
            CliTypeIdentity.ManagedByReference(CliTypeIdentity.FromStackKind(CliValueKind.Void))), kind);
        Reject(MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void),
            CliTypeIdentity.ManagedByReference(CliTypeIdentity.UnmanagedPointer(scalar))), kind);
        var functionPointer = CliTypeIdentity.FunctionPointer(new(
            1,
            0,
            0,
            MethodSignatureModel.Create(CliValueKind.Void)));
        Reject(MethodSignatureModel.Create(functionPointer), kind);
        if (kind == NativeAbiSignatureKind.Callback)
        {
            Reject(
                MethodSignatureModel.Create(
                    CliTypeIdentity.FromStackKind(CliValueKind.Void),
                    functionPointer),
                kind);
        }
    }

    [Fact]
    public void RequiredSignatureProfileAndMethodIdentityRejectAtTheBoundary()
    {
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        Assert.Throws<ArgumentNullException>(() => Planner().Plan(null!, NativeAbiSignatureKind.Import, "method"));
        Assert.Throws<ArgumentNullException>(() => Planner().Plan(signature, NativeAbiSignatureKind.Import, null!));
        Assert.Throws<ArgumentException>(() => Planner().Plan(signature, NativeAbiSignatureKind.Import, " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => Planner().Plan(signature, (NativeAbiSignatureKind)2, "method"));
        Assert.Throws<ArgumentNullException>(() => new NativeAbiSignaturePlanner(null!));
    }

    [Fact]
    public void AggregateMappingsPreserveLogicalIndicesWhilePlanningHiddenResultsAndIgnoredOperands()
    {
        var assembly = new AssemblyIdentity("NativeFixture");
        var empty = CliTypeIdentity.Named(assembly, "Fixture", "Empty", true);
        var singleton = CliTypeIdentity.Named(assembly, "Fixture", "Single", true);
        var pair = CliTypeIdentity.Named(assembly, "Fixture", "Pair", true);
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var pointer = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
        var aggregates = new FixedAggregatePlanner(new Dictionary<CliTypeIdentity, NativeAbiValuePlan>
        {
            [empty] = new(NativeAbiValueKind.IgnoredAggregate, empty, null, 1, 1),
            [singleton] = new(NativeAbiValueKind.ScalarizedAggregate, singleton, integer, 4, 4, integer),
            [pair] = new(NativeAbiValueKind.IndirectAggregate, pair, pointer, 16, 8),
        });
        var planner = Assert.IsAssignableFrom<INativeAbiSignaturePlanner>(new NativeAbiSignaturePlanner(aggregates));
        var logical = MethodSignatureModel.Create(pair, empty, singleton,
            CliTypeIdentity.FromStackKind(CliValueKind.I8), pair);

        var plan = planner.Plan(logical, NativeAbiSignatureKind.Import, "native-call");

        Assert.Same(logical, plan.LogicalSignature);
        Assert.Equal(CliValueKind.Void, plan.PhysicalSignature.ReturnType);
        Assert.Equal([CliValueKind.NativeInt, CliValueKind.I4, CliValueKind.I8, CliValueKind.NativeInt],
            plan.PhysicalSignature.ParameterTypes.ToArray());
        Assert.Equal(0, plan.HiddenResultParameterIndex);
        Assert.Equal([0, 1, 2, 3], plan.Parameters.Select(parameter => parameter.LogicalIndex));
        Assert.Equal(new int?[] { null, 1, 2, 3 }, plan.Parameters.Select(parameter => parameter.PhysicalIndex));
        Assert.Equal(aggregates.Values[pair], plan.Result);
        Assert.Equal([pair, empty, singleton, pair], aggregates.Calls);
        Assert.Equal(aggregates.Values[empty], plan.Parameters[0].Value);
        Assert.Equal(aggregates.Values[singleton], plan.Parameters[1].Value);
        Assert.Equal(aggregates.Values[pair], plan.Parameters[3].Value);
        aggregates.Calls.Clear();

        var direct = planner.Plan(MethodSignatureModel.Create(singleton), NativeAbiSignatureKind.Import, "native-call");
        Assert.Equal(CliValueKind.I4, direct.PhysicalSignature.ReturnType);
        Assert.Empty(direct.PhysicalSignature.ParameterTypes);
        Assert.Null(direct.HiddenResultParameterIndex);
        var ignored = planner.Plan(MethodSignatureModel.Create(empty), NativeAbiSignatureKind.Import, "native-call");
        Assert.Equal(CliValueKind.Void, ignored.PhysicalSignature.ReturnType);
        Assert.Empty(ignored.PhysicalSignature.ParameterTypes);
        Assert.Null(ignored.HiddenResultParameterIndex);
        var before = aggregates.Calls.Count;
        RejectWith(planner, logical, NativeAbiSignatureKind.Callback);
        Assert.Equal(before, aggregates.Calls.Count);
    }

    private sealed class FixedAggregatePlanner(Dictionary<CliTypeIdentity, NativeAbiValuePlan> values) : INativeAggregateAbiPlanner
    {
        public Dictionary<CliTypeIdentity, NativeAbiValuePlan> Values { get; } = values;
        public List<CliTypeIdentity> Calls { get; } = [];

        public NativeAbiValuePlan Plan(CliTypeIdentity type, string methodName)
        {
            Assert.Equal("native-call", methodName);
            Calls.Add(type);
            return Values[type];
        }
    }

    private static void RejectWith(INativeAbiSignaturePlanner planner, MethodSignatureModel signature, NativeAbiSignatureKind kind)
    {
        var error = Assert.Throws<CompilerException>(() => planner.Plan(signature, kind, "native-call"));
        Assert.Equal(DiagnosticCode.NativeInterop, error.Diagnostic.Code);
        Assert.Equal("native-call", error.Diagnostic.Method);
    }

    private static INativeAbiSignaturePlanner Planner() =>
        Assert.IsAssignableFrom<INativeAbiSignaturePlanner>(NativeAbiTestSupport.ScalarSignatures());

    private static CliTypeIdentity Convention(string name) =>
        CliTypeIdentity.Named(
            new AssemblyIdentity("System.Private.CoreLib"),
            "System.Runtime.CompilerServices",
            name,
            isValueType: false);

    private static void Reject(MethodSignatureModel signature, NativeAbiSignatureKind kind)
    {
        var error = Assert.Throws<CompilerException>(() => Planner().Plan(signature, kind, "qualified-method"));
        Assert.Equal(DiagnosticCode.NativeInterop, error.Diagnostic.Code);
        Assert.Equal("qualified-method", error.Diagnostic.Method);
    }
}
