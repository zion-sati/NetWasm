using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class TypeTestInstructionEmitterTests
{
    [Fact]
    public void CastClassEmitsManagedInvalidCastPath()
    {
        var request = CreateRequest(CilOperation.CastClass);

        Emit(AsProvider(CreateEmitter()), request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
        Assert.True(ContainsManagedThrow(GetCodeBytes(request)));
    }

    [Fact]
    public void IsInstanceWritesNullOnFailureWithoutThrowing()
    {
        var request = CreateRequest(CilOperation.IsInstance);

        Emit(AsProvider(CreateEmitter()), request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.False(ContainsManagedThrow(GetCodeBytes(request)));
    }

    [Fact]
    public void RuntimeTypeOperandUsesResolvedTypeIdentity()
    {
        var request = CreateRequest(
            CilOperation.CastClass,
            new CilOperand.TypeIdentity(ValueType()));

        AsTypeTest(CreateEmitter()).Test(
            request,
            GetCodeWriter(request),
            returnNullOnFailure: false);

        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
    }

    [Fact]
    public void KnownCallerWithoutPlannedSiteFallsBackToRuntimeTest()
    {
        var caller = CreateCaller();
        var request = WithCaller(
            CreateRequest(
                CilOperation.CastClass,
                new CilOperand.TypeIdentity(ValueType())),
            caller);

        AsTypeTest(CreateEmitter()).Test(
            request,
            GetCodeWriter(request),
            returnNullOnFailure: false);

        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
        Assert.True(ContainsManagedThrow(GetCodeBytes(request)));
    }

    [Fact]
    public void NullableFallbackTestsTheBoxedUnderlyingTypeId()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new TypeTestInstructionEmitter(
            layouts,
            new AddressInstructionEmitter(layouts),
            layouts,
            CreateTypeOperands(new FakeProgram()),
            WasmRuntimeImports.CreateCatalog(),
            new ImplicitExceptionEmitter(layouts),
            new NullableTypeResolver());
        var underlying = ValueType();
        var nullable = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System", "Nullable`1", isValueType: true),
            [underlying]);
        var request = WithCaller(
            CreateRequest(
                CilOperation.IsInstance,
                new CilOperand.TypeIdentity(nullable)),
            CreateCaller());

        AsTypeTest(emitter).Test(
            request,
            GetCodeWriter(request),
            returnNullOnFailure: true);

        Assert.Equal(underlying, layouts.ObjectIdentityRequest);
    }

    [Fact]
    public void PlannedSiteWithNoMatchingTypesThrowsOnFailure()
    {
        var caller = CreateCaller();
        var request = WithSite(
            CreateRequest(CilOperation.CastClass),
            caller,
            []);

        AsTypeTest(CreateEmitter()).Test(
            request,
            GetCodeWriter(request),
            returnNullOnFailure: false);

        Assert.True(ContainsManagedThrow(GetCodeBytes(request)));
    }

    [Fact]
    public void PlannedSiteReturnsNullForOneMatchingType()
    {
        var caller = CreateCaller();
        var request = WithSite(
            CreateRequest(CilOperation.IsInstance),
            caller,
            [ValueType()]);

        AsTypeTest(CreateEmitter()).Test(
            request,
            GetCodeWriter(request),
            returnNullOnFailure: true);

        Assert.Contains(WasmOpcodes.I32Constant, GetCodeBytes(request));
        Assert.False(ContainsManagedThrow(GetCodeBytes(request)));
    }

    [Fact]
    public void PlannedSiteCombinesMultipleMatchingTypes()
    {
        var caller = CreateCaller();
        var request = WithSite(
            CreateRequest(CilOperation.CastClass),
            caller,
            [ValueType(), ReferenceType()]);

        AsTypeTest(CreateEmitter(new IdentifiedTypes(new() { [ValueType()] = 5, [ReferenceType()] = 7 }))).Test(
            request,
            GetCodeWriter(request),
            returnNullOnFailure: false);

        Assert.Contains(WasmOpcodes.I32Or, GetCodeBytes(request));
        Assert.True(ContainsManagedThrow(GetCodeBytes(request)));
    }

    // Types with consecutive ids are tested together, by one range check.
    [Fact]
    public void ConsecutiveMatchingIdsBecomeOneRangeCheck()
    {
        var types = Types(Enumerable.Range(20, 8));
        var request = WithSite(CreateRequest(CilOperation.CastClass), CreateCaller(), [.. types.Keys]);

        AsTypeTest(CreateEmitter(new IdentifiedTypes(types))).Test(
            request,
            GetCodeWriter(request),
            returnNullOnFailure: false);

        var code = GetCodeBytes(request);
        Assert.Contains(WasmOpcodes.I32LessThanUnsigned, code);
        Assert.DoesNotContain(WasmOpcodes.I32Equal, code);
        Assert.DoesNotContain(WasmOpcodes.I32Or, code);
    }

    // Scattered ids, as an interface's implementations have, are searched by
    // halving instead of compared one by one.
    [Fact]
    public void ScatteredMatchingIdsAreSearched()
    {
        var types = Types(Enumerable.Range(1, 12).Select(index => index * 2));
        var request = WithSite(CreateRequest(CilOperation.IsInstance), CreateCaller(), [.. types.Keys]);

        AsTypeTest(CreateEmitter(new IdentifiedTypes(types))).Test(
            request,
            GetCodeWriter(request),
            returnNullOnFailure: true);

        var code = GetCodeBytes(request).ToArray();
        Assert.Contains(Enumerable.Range(0, code.Length - 1),
            index => code[index] == WasmOpcodes.If && code[index + 1] == 0x7f);
        Assert.Contains(WasmOpcodes.Else, code);
    }

    // A class's allocated subtypes have scattered ids among those of the
    // subtypes it never allocates; the interval of ids the layout compiler
    // numbers for its subtypes covers them all with one range check, and a
    // match outside it, as a covariant array is, keeps its own.
    [Fact]
    public void MatchingIdsInTheTargetsSubtypeIntervalBecomeOneRangeCheck()
    {
        var types = Types([12, 15, 21, 30, 39]);
        types[ValueType()] = 10;
        var request = WithSite(CreateRequest(CilOperation.IsInstance), CreateCaller(), [.. types.Keys.Where(type => types[type] != 10)]);

        AsTypeTest(CreateEmitter(new IdentifiedTypes(types, new() { [ValueType()] = 40 }))).Test(
            request,
            GetCodeWriter(request),
            returnNullOnFailure: true);

        var code = GetCodeBytes(request).ToArray();
        Assert.Single(code, WasmOpcodes.I32LessThanUnsigned);
        Assert.DoesNotContain(WasmOpcodes.I32Equal, code);
        Assert.DoesNotContain(WasmOpcodes.I32Or, code);
        Assert.DoesNotContain(WasmOpcodes.Else, code);
    }

    [Fact]
    public void MatchesOutsideTheTargetsSubtypeIntervalKeepTheirOwnRuns()
    {
        var types = Types([12, 30, 70]);
        types[ValueType()] = 10;
        var request = WithSite(CreateRequest(CilOperation.IsInstance), CreateCaller(), [.. types.Keys.Where(type => types[type] != 10)]);

        AsTypeTest(CreateEmitter(new IdentifiedTypes(types, new() { [ValueType()] = 40 }))).Test(
            request,
            GetCodeWriter(request),
            returnNullOnFailure: true);

        var code = GetCodeBytes(request).ToArray();
        Assert.Single(code, WasmOpcodes.I32LessThanUnsigned);
        Assert.Single(code, WasmOpcodes.I32Equal);
        Assert.Single(code, WasmOpcodes.I32Or);
    }

    private static Dictionary<CliTypeIdentity, int> Types(IEnumerable<int> ids) =>
        ids.ToDictionary(id => CliTypeIdentity.Named(Assembly, "Tests", $"Type{id}", false), id => id);

    // Gives each type its own id, and the types given one the last id of their
    // subtypes, as a linked program does.
    private sealed class IdentifiedTypes(
        Dictionary<CliTypeIdentity, int> ids,
        Dictionary<CliTypeIdentity, int>? lastSubtypeIds = null) : ITypeLayoutProvider
    {
        public ObjectLayout GetObjectLayout(CliTypeIdentity type) => new(ids[type], 16, [])
        {
            LastSubtypeId = lastSubtypeIds?.GetValueOrDefault(type) ?? 0,
        };

        public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout)
        {
            layout = ids.ContainsKey(type) ? GetObjectLayout(type) : default;
            return ids.ContainsKey(type);
        }

        public ObjectLayout GetObjectLayout(EntityKey type) => throw new NotSupportedException();


        public int StringTypeId => 0;

        public int TypeTypeId => 0;
    }

    private static TypeTestInstructionEmitter CreateEmitter(ITypeLayoutProvider? typeLayouts = null)
    {
        var layouts = new RecordingLayoutProvider();
        return new TypeTestInstructionEmitter(layouts, new AddressInstructionEmitter(layouts), typeLayouts ?? layouts,
            CreateTypeOperands(new FakeProgram()),
            WasmRuntimeImports.CreateCatalog(),
            new ImplicitExceptionEmitter(layouts),
            new NullableTypeResolver());
    }

    private static ITypeTestEmitter AsTypeTest(TypeTestInstructionEmitter emitter) =>
        new[] { emitter }.Cast<ITypeTestEmitter>().Single();

    private static IInstructionCommandProvider AsProvider(
        TypeTestInstructionEmitter emitter) =>
        new[] { emitter }.Cast<IInstructionCommandProvider>().Single();

    private static void Emit(
        IInstructionCommandProvider provider,
        InstructionEmissionRequest request)
    {
        var command = provider.Commands.Single(candidate =>
            candidate.Operation == request.Instruction.Operation);
        command.Emit(
            request,
            GetCodeWriter(request),
            CreateFunctionIndexResolver());
    }

    private static InstructionEmissionRequest CreateRequest(
        CilOperation operation,
        CilOperand? operand = null) => CreateInstructionRequest(
            operation,
            [CliValueKind.ManagedReference],
            operand ?? new CilOperand.Entity(TypeKey));

    private static MethodInstanceModel CreateCaller()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey);
        return new(
            method,
            ReferenceType(),
            [],
            method.Signature);
    }

    private static InstructionEmissionRequest WithCaller(
        InstructionEmissionRequest request,
        MethodInstanceModel caller)
    {
        var updated = request with
        {
            Header = request.Header with { MethodInstance = caller },
        };
        RegisterInstructionWriter(updated, new RecordingInstructionWriter());
        return updated;
    }

    private static InstructionEmissionRequest WithSite(
        InstructionEmissionRequest request,
        MethodInstanceModel caller,
        ImmutableArray<CliTypeIdentity> matchingTypes)
    {
        var site = new TypeTestSiteModel(
            caller.CanonicalName,
            request.Instruction.Offset,
            ValueType(),
            matchingTypes);
        var updated = request with
        {
            Header = request.Header with { MethodInstance = caller },
            Target = request.Target with
            {
                TypeTestSites = Lookups.Of(new Dictionary<string, TypeTestSiteModel>
                {
                    [site.Key] = site,
                }),
            },
        };
        RegisterInstructionWriter(updated, new RecordingInstructionWriter());
        return updated;
    }

    private static CliTypeIdentity ValueType() =>
        CliTypeIdentity.Named(Assembly, "Test", "Value", isValueType: true);

    private static CliTypeIdentity ReferenceType() =>
        CliTypeIdentity.Named(Assembly, "Test", "Reference", isValueType: false);
}
