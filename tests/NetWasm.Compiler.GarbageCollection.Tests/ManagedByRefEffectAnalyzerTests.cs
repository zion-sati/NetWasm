using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection.Tests;

public sealed class ManagedByRefEffectAnalyzerTests
{
    private static readonly AssemblyIdentity Assembly = new("ByRefEffects");
    private static readonly EntityKey OwnerKey = new(Assembly, 0x02000001);
    private static readonly EntityKey PairKey = new(Assembly, 0x02000002);
    private static readonly EntityKey ReadKey = new(Assembly, 0x06000001);
    private static readonly EntityKey WriteKey = new(Assembly, 0x06000002);
    private static readonly EntityKey AliasKey = new(Assembly, 0x06000003);
    private static readonly EntityKey HelperKey = new(Assembly, 0x06000004);
    private static readonly EntityKey WrapperKey = new(Assembly, 0x06000005);
    private static readonly EntityKey UnknownKey = new(Assembly, 0x06000006);
    private static readonly EntityKey UnsafeAsKey = new(Assembly, 0x06000007);
    private static readonly EntityKey RecursiveAKey = new(Assembly, 0x06000008);
    private static readonly EntityKey RecursiveBKey = new(Assembly, 0x06000009);
    private static readonly CliTypeIdentity Owner = CliTypeIdentity.Named(
        Assembly, "Tests", "Owner", false);
    private static readonly CliTypeIdentity Pair = CliTypeIdentity.Named(
        Assembly, "Tests", "Pair", true);
    private static readonly CliTypeIdentity PairAddress =
        CliTypeIdentity.ManagedByReference(Pair);
    private static readonly CliTypeIdentity Void =
        CliTypeIdentity.FromStackKind(CliValueKind.Void);

    [Fact]
    public void LoopCarriedLocalAliasesReachAStableUnion()
    {
        var repository = new Repository();
        var method = Method(AliasKey, PairAddress, PairAddress, PairAddress,
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        repository.Add(method);
        var body = BodyWithLocals(repository, method, [PairAddress],
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.StoreLocal, Index(0)),
            I(2, CilOperation.LoadArgument, Index(2)),
            I(3, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(7)),
            I(4, CilOperation.LoadArgument, Index(1)),
            I(5, CilOperation.StoreLocal, Index(0)),
            I(6, CilOperation.Branch, new CilOperand.BranchTarget(2)),
            I(7, CilOperation.LoadLocal, Index(0)),
            I(8, CilOperation.Return));

        var effect = Effect(Analyze(repository, [body]), body);

        Assert.Equal([0, 1], effect.ReturnedArgumentAliases.Order());
        Assert.Empty(effect.WrittenArguments);
        Assert.Empty(effect.EscapedArguments);
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.UnsafeAdd)]
    [InlineData(RuntimeIntrinsic.UnsafeAddByteOffset)]
    public void OffsetAliasIntrinsicsPreserveOnlyTheAddressArgument(RuntimeIntrinsic intrinsic)
    {
        var repository = new Repository();
        var offset = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
        var helper = Method(UnsafeAsKey, PairAddress, PairAddress, offset);
        var wrapper = Method(WrapperKey, PairAddress, PairAddress, offset);
        repository.Add(helper, wrapper);
        var body = Body(repository, wrapper,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.LoadArgument, Index(1)),
            I(2, CilOperation.Call, Entity(UnsafeAsKey)),
            I(3, CilOperation.Return));

        var effect = Effect(Analyze(repository, [body], new Intrinsics((UnsafeAsKey, intrinsic))), body);

        Assert.Equal([0], effect.ReturnedArgumentAliases);
        Assert.Empty(effect.WrittenArguments);
        Assert.Empty(effect.EscapedArguments);
    }

    [Fact]
    public void OtherIntrinsicsDoNotReceiveAliasPreservingTreatment()
    {
        var repository = new Repository();
        var result = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var helper = Method(UnsafeAsKey, result, PairAddress, PairAddress);
        var wrapper = Method(WrapperKey, result, PairAddress, PairAddress);
        repository.Add(helper, wrapper);
        var body = Body(repository, wrapper,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.LoadArgument, Index(1)),
            I(2, CilOperation.Call, Entity(UnsafeAsKey)),
            I(3, CilOperation.Return));

        var effect = Effect(Analyze(repository, [body],
            new Intrinsics((UnsafeAsKey, RuntimeIntrinsic.UnsafeIsAddressGreaterThan))), body);

        Assert.Equal([0, 1], effect.WrittenArguments.Order());
        Assert.Equal([0, 1], effect.EscapedArguments.Order());
        Assert.Empty(effect.ReturnedArgumentAliases);
    }

    [Fact]
    public void StaticScalarStoreDoesNotEscapeAnUnrelatedByRefArgument()
    {
        var repository = new Repository();
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = new FieldDefinitionModel(new(Assembly, 0x04000001), OwnerKey, "Value", scalar, true);
        repository.Fields.Add(field.Key, field);
        var method = Method(WriteKey, Void, PairAddress, scalar);
        repository.Add(method);
        var body = Body(repository, method,
            I(0, CilOperation.LoadArgument, Index(1)),
            I(1, CilOperation.StoreStaticField, new CilOperand.FieldInstance(new(field, Owner, scalar))),
            I(2, CilOperation.Return));

        Assert.Equal(ManagedByRefMethodEffects.Empty, Effect(Analyze(repository, [body]), body));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArrayElementStoresDoNotEscapeAnUnrelatedByRefArgument(bool reference)
    {
        var repository = new Repository();
        var element = reference ? Owner : CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var method = Method(WriteKey, Void, PairAddress, CliTypeIdentity.SzArray(element), element);
        repository.Add(method);
        var body = Body(repository, method,
            I(0, CilOperation.LoadArgument, Index(1)),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(2, CilOperation.LoadArgument, Index(2)),
            I(3, reference ? CilOperation.StoreArrayElementReference : CilOperation.StoreArrayElement, Type(element)),
            I(4, CilOperation.Return));

        Assert.Equal(ManagedByRefMethodEffects.Empty, Effect(Analyze(repository, [body]), body));
    }

    [Fact]
    public void CompareExchangeConservativelyOwnsItsAddressOperand()
    {
        var repository = new Repository();
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var method = Method(WriteKey, scalar, CliTypeIdentity.ManagedByReference(scalar), scalar, scalar);
        repository.Add(method);
        var body = Body(repository, method,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.LoadArgument, Index(1)),
            I(2, CilOperation.LoadArgument, Index(2)),
            I(3, CilOperation.CompareExchange, Type(scalar)),
            I(4, CilOperation.Return));

        var effect = Effect(Analyze(repository, [body]), body);

        Assert.Equal([0], effect.WrittenArguments);
        Assert.Equal([0], effect.EscapedArguments);
        Assert.Empty(effect.ReturnedArgumentAliases);
    }

    [Theory]
    [InlineData(CilOperation.CopyBlock)]
    [InlineData(CilOperation.InitializeBlock)]
    public void BlockWritesOwnTheDestinationWithoutEscapingTheSource(CilOperation operation)
    {
        var repository = new Repository();
        var source = operation == CilOperation.CopyBlock ? PairAddress : CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var method = Method(WriteKey, Void, PairAddress, source);
        repository.Add(method);
        var body = Body(repository, method,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.LoadArgument, Index(1)),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)),
            I(3, operation),
            I(4, CilOperation.Return));

        var effect = Effect(Analyze(repository, [body]), body);

        Assert.Equal([0], effect.WrittenArguments);
        Assert.Empty(effect.EscapedArguments);
        Assert.Empty(effect.ReturnedArgumentAliases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldAddressAndStoreKeepReceiverOwnershipDistinct(bool store)
    {
        var repository = new Repository();
        var fieldType = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = new FieldDefinitionModel(new(Assembly, 0x04000001), PairKey, "Value", fieldType, false);
        repository.Fields.Add(field.Key, field);
        var method = Method(WriteKey, store ? Void : CliTypeIdentity.ManagedByReference(fieldType), PairAddress);
        repository.Add(method);
        var instructions = new List<CilInstruction> { I(0, CilOperation.LoadArgument, Index(0)) };
        if (store)
        {
            instructions.Add(I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)));
            instructions.Add(I(2, CilOperation.StoreField, new CilOperand.FieldInstance(new(field, Pair, fieldType))));
        }
        else
            instructions.Add(I(1, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(new(field, Pair, fieldType))));
        instructions.Add(I(instructions.Count, CilOperation.Return));
        var body = Body(repository, method, [.. instructions]);

        var effect = Effect(Analyze(repository, [body]), body);

        Assert.Equal(store, effect.WrittenArguments.Contains(0));
        Assert.Equal(!store, effect.ReturnedArgumentAliases.Contains(0));
        Assert.Equal(store ? 1 : 0, effect.WrittenArguments.Count);
        Assert.Equal(store ? 0 : 1, effect.ReturnedArgumentAliases.Count);
        Assert.Empty(effect.EscapedArguments);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void InstanceCallsDistinguishKnownTargetsFromMissingDispatch(int shape)
    {
        var repository = new Repository();
        var helper = Method(HelperKey, Void, PairAddress) with { IsStatic = false };
        var wrapper = Method(WrapperKey, Void, Owner, PairAddress);
        repository.Add(helper, wrapper);
        var helperBody = Body(repository, helper,
            I(0, CilOperation.LoadArgument, Index(1)),
            I(1, CilOperation.InitializeObject, Type(Pair)),
            I(2, CilOperation.Return));
        var wrapperBody = Body(repository, wrapper,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.LoadArgument, Index(1)),
            I(2, shape == 4 ? CilOperation.Call : CilOperation.CallVirtual,
                new CilOperand.MethodInstance(helperBody.Method)),
            I(3, CilOperation.Return));
        var methods = ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(WrapperKey, wrapperBody);
        if (shape is 1 or 4)
            methods = methods.Add(HelperKey, helperBody);
        var dispatches = ImmutableDictionary<string, DispatchCallSiteModel>.Empty;
        if (shape is 1 or 2 or 3)
        {
            var dispatch = new DispatchCallSiteModel(wrapperBody.Method.CanonicalName, 2,
                helperBody.Method, shape == 2 ? [] : [new(Owner, helperBody.Method)]);
            dispatches = dispatches.Add(dispatch.Key, dispatch);
        }

        var effects = Create(repository).Analyze(
            new(methods, ImmutableDictionary<string, ManagedMethodBody>.Empty, dispatches));
        var effect = Effect(effects, wrapperBody);

        Assert.Equal([1], effect.WrittenArguments);
        Assert.Equal(shape is not (1 or 4), effect.EscapedArguments.Contains(1));
        Assert.Equal(shape is 1 or 4 ? 0 : 1, effect.EscapedArguments.Count);
        Assert.Empty(effect.ReturnedArgumentAliases);
    }

    [Fact]
    public void ValueTypeReceiverIsAnArgumentBackedAddress()
    {
        var repository = new Repository();
        var method = Method(AliasKey, PairAddress) with { IsStatic = false, DeclaringType = PairKey };
        repository.Add(method);
        var body = Body(repository, method,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.Return));

        var effect = Effect(Analyze(repository, [body]), body);

        Assert.Equal([0], effect.ReturnedArgumentAliases);
        Assert.Empty(effect.WrittenArguments);
        Assert.Empty(effect.EscapedArguments);
    }

    [Fact]
    public void ExceptionFlowConservativelyOwnsByRefsAndAddressTakenArguments()
    {
        var repository = new Repository
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Finally, 0, 3, 3, 1, null, null)],
        };
        var method = Method(WrapperKey, Void, PairAddress, Pair);
        repository.Add(method);
        var body = Body(repository, method,
            I(0, CilOperation.LoadArgumentAddress, Index(1)),
            I(1, CilOperation.Pop),
            I(2, CilOperation.Leave, new CilOperand.BranchTarget(4)),
            I(3, CilOperation.EndFinally),
            I(4, CilOperation.Return));

        var effect = Effect(Analyze(repository, [body]), body);

        Assert.Equal([0, 1], effect.WrittenArguments.Order());
        Assert.Equal([0, 1], effect.EscapedArguments.Order());
        Assert.Empty(effect.ReturnedArgumentAliases);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LocalsAndDuplicationPreserveOnlyManagedAddressOrigins(bool address)
    {
        var repository = new Repository();
        var type = address ? PairAddress : CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var method = Method(AliasKey, type, type);
        repository.Add(method);
        var body = BodyWithLocals(repository, method, [type],
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.StoreLocal, Index(0)),
            I(2, CilOperation.LoadLocal, Index(0)),
            I(3, CilOperation.Duplicate),
            I(4, CilOperation.Pop),
            I(5, CilOperation.Return));

        var effect = Effect(Analyze(repository, [body]), body);

        Assert.Equal(address ? 1 : 0, effect.ReturnedArgumentAliases.Count);
        Assert.Equal(address, effect.ReturnedArgumentAliases.Contains(0));
        Assert.Empty(effect.WrittenArguments);
        Assert.Empty(effect.EscapedArguments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndirectCallsConservativelyOwnSuppliedAliasesWithoutInventingReturnAliases(bool returnsAddress)
    {
        var repository = new Repository();
        var result = returnsAddress ? PairAddress : Void;
        var method = Method(WrapperKey, result, PairAddress,
            CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));
        repository.Add(method);
        var body = Body(repository, method,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.LoadArgument, Index(1)),
            I(2, CilOperation.CallIndirect, new CilOperand.CallSite(new(result, [PairAddress]))),
            I(3, CilOperation.Return));

        var effect = Effect(Analyze(repository, [body]), body);

        Assert.Equal([0], effect.WrittenArguments);
        Assert.Equal([0], effect.EscapedArguments);
        Assert.Empty(effect.ReturnedArgumentAliases);
    }

    [Fact]
    public void ConstructedClosedCallsPropagateEscapesFromTheirCallee()
    {
        var repository = new Repository();
        var unknown = Method(UnknownKey, Void, PairAddress);
        var helper = Method(HelperKey, Void, PairAddress);
        var wrapper = Method(WrapperKey, Void, PairAddress);
        repository.Add(unknown, helper, wrapper);
        var helperBody = Body(repository, helper,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.Call, Entity(UnknownKey)),
            I(2, CilOperation.Return));
        var wrapperBody = Body(repository, wrapper,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.Call, new CilOperand.MethodInstance(helperBody.Method)),
            I(2, CilOperation.Return));
        var request = new ManagedByRefEffectAnalysisRequest(
            ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(WrapperKey, wrapperBody),
            ImmutableDictionary<string, ManagedMethodBody>.Empty.Add(helperBody.Method.CanonicalName, helperBody),
            ImmutableDictionary<string, DispatchCallSiteModel>.Empty);

        var effects = Create(repository).Analyze(request);
        var effect = Effect(effects, wrapperBody);

        Assert.Equal([0], effect.WrittenArguments);
        Assert.Equal([0], effect.EscapedArguments);
        Assert.Empty(effect.ReturnedArgumentAliases);
    }

    [Theory]
    [InlineData(CilOperation.StoreObject)]
    [InlineData(CilOperation.CopyObject)]
    public void ObjectStoresAndCopiesMutateOnlyTheirDestination(CilOperation operation)
    {
        var repository = new Repository();
        var method = Method(WriteKey, Void, PairAddress,
            operation == CilOperation.CopyObject ? PairAddress : Pair);
        repository.Add(method);
        var body = Body(repository, method,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.LoadArgument, Index(1)),
            I(2, operation, Type(Pair)),
            I(3, CilOperation.Return));

        var effect = Effect(Analyze(repository, [body]), body);

        Assert.Equal([0], effect.WrittenArguments);
        Assert.Empty(effect.EscapedArguments);
        Assert.Empty(effect.ReturnedArgumentAliases);
    }

    [Fact]
    public void MergedReturnedAddressRetainsBothPossibleArgumentAliases()
    {
        var repository = new Repository();
        var method = Method(AliasKey, PairAddress, PairAddress, PairAddress,
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        repository.Add(method);
        var body = Body(repository, method,
            I(0, CilOperation.LoadArgument, Index(2)),
            I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
            I(2, CilOperation.LoadArgument, Index(0)),
            I(3, CilOperation.Branch, new CilOperand.BranchTarget(5)),
            I(4, CilOperation.LoadArgument, Index(1)),
            I(5, CilOperation.Return));

        var effect = Effect(Analyze(repository, [body]), body);

        Assert.Equal([0, 1], effect.ReturnedArgumentAliases.Order());
        Assert.Empty(effect.WrittenArguments);
        Assert.Empty(effect.EscapedArguments);
    }

    [Theory]
    [InlineData(CilOperation.Add, true)]
    [InlineData(CilOperation.Subtract, true)]
    [InlineData(CilOperation.Add, false)]
    [InlineData(CilOperation.Subtract, false)]
    public void ArithmeticPropagatesAliasesOnlyWhenItProducesAManagedAddress(CilOperation operation, bool address)
    {
        var repository = new Repository();
        var result = address ? PairAddress : CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var method = Method(AliasKey, result, result);
        repository.Add(method);
        var body = Body(repository, method,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(2, operation),
            I(3, CilOperation.Return));

        var effect = Effect(Analyze(repository, [body]), body);

        Assert.Equal(address ? 1 : 0, effect.ReturnedArgumentAliases.Count);
        Assert.Equal(address, effect.ReturnedArgumentAliases.Contains(0));
        Assert.Empty(effect.WrittenArguments);
        Assert.Empty(effect.EscapedArguments);
    }

    [Fact]
    public void DirectReadsDoNotClaimPointeeMutationOrEscape()
    {
        var repository = new Repository();
        var read = Method(ReadKey, Void, PairAddress);
        repository.Methods.Add(ReadKey, read);
        var body = Body(repository, read,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.LoadObject, Type(Pair)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Return));

        var result = Analyze(repository, [body]);

        Assert.Equal(ManagedByRefMethodEffects.Empty, Effect(result, body));
    }

    [Fact]
    public void DirectWritesAndReturnedAliasesAreDistinctEffects()
    {
        var repository = new Repository();
        var write = Method(WriteKey, Void, PairAddress);
        var alias = Method(AliasKey, PairAddress, PairAddress);
        repository.Methods.Add(WriteKey, write);
        repository.Methods.Add(AliasKey, alias);
        var writeBody = Body(repository, write,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.InitializeObject, Type(Pair)),
            I(2, CilOperation.Return));
        var aliasBody = Body(repository, alias,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.Return));

        var result = Analyze(repository, [writeBody, aliasBody]);

        Assert.Equal([0], Effect(result, writeBody).WrittenArguments);
        Assert.Empty(Effect(result, writeBody).ReturnedArgumentAliases);
        Assert.Empty(Effect(result, aliasBody).WrittenArguments);
        Assert.Empty(Effect(result, aliasBody).EscapedArguments);
        Assert.Equal([0], Effect(result, aliasBody).ReturnedArgumentAliases);
        Assert.True(Effect(result, aliasBody).RequiresOwnedStorage(0));
    }

    [Fact]
    public void ClosedCallsPropagateAliasAndOnlyTheWrittenPointee()
    {
        var repository = new Repository();
        var alias = Method(AliasKey, PairAddress, PairAddress);
        var helper = Method(HelperKey, Void, PairAddress, PairAddress);
        var wrapper = Method(WrapperKey, Void, Pair, Pair);
        repository.Add(alias, helper, wrapper);
        var aliasBody = Body(repository, alias,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.Return));
        var helperBody = Body(repository, helper,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.InitializeObject, Type(Pair)),
            I(2, CilOperation.Return));
        var wrapperBody = Body(repository, wrapper,
            I(0, CilOperation.LoadArgumentAddress, Index(0)),
            I(1, CilOperation.Call, Entity(AliasKey)),
            I(2, CilOperation.LoadArgumentAddress, Index(1)),
            I(3, CilOperation.Call, Entity(AliasKey)),
            I(4, CilOperation.Call, Entity(HelperKey)),
            I(5, CilOperation.Return));

        var result = Analyze(repository, [aliasBody, helperBody, wrapperBody]);
        var effect = Effect(result, wrapperBody);

        Assert.Equal([0], effect.WrittenArguments);
        Assert.Empty(effect.EscapedArguments);
        Assert.Empty(effect.ReturnedArgumentAliases);
        Assert.True(effect.RequiresOwnedStorage(0));
        Assert.False(effect.RequiresOwnedStorage(1));
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.UnsafeAs)]
    [InlineData(RuntimeIntrinsic.UnsafeAsRef)]
    [InlineData(RuntimeIntrinsic.UnsafeAsRefManaged)]
    public void UnsafeAliasIntrinsicRemainsTransparentToClosedMutationProof(RuntimeIntrinsic intrinsic)
    {
        var intrinsics = new Intrinsics((UnsafeAsKey, intrinsic));
        var repository = new Repository();
        var unsafeAs = Method(UnsafeAsKey, PairAddress, PairAddress);
        var helper = Method(HelperKey, Void, PairAddress, PairAddress);
        var wrapper = Method(WrapperKey, Void, Pair, Pair);
        repository.Add(unsafeAs, helper, wrapper);
        var helperBody = Body(repository, helper,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.InitializeObject, Type(Pair)),
            I(2, CilOperation.Return));
        var wrapperBody = Body(repository, wrapper,
            I(0, CilOperation.LoadArgumentAddress, Index(0)),
            I(1, CilOperation.Call, Entity(UnsafeAsKey)),
            I(2, CilOperation.LoadArgumentAddress, Index(1)),
            I(3, CilOperation.Call, Entity(UnsafeAsKey)),
            I(4, CilOperation.Call, Entity(HelperKey)),
            I(5, CilOperation.Return));

        var result = Analyze(repository, [helperBody, wrapperBody], intrinsics);

        Assert.Equal([0], Effect(result, wrapperBody).WrittenArguments);
        Assert.False(Effect(result, wrapperBody).RequiresOwnedStorage(1));
    }

    [Fact]
    public void UnknownManagedCallRequiresOwnershipForEverySuppliedAlias()
    {
        var repository = new Repository();
        var unknown = Method(UnknownKey, Void, PairAddress);
        var wrapper = Method(WrapperKey, Void, Pair);
        repository.Add(unknown, wrapper);
        var wrapperBody = Body(repository, wrapper,
            I(0, CilOperation.LoadArgumentAddress, Index(0)),
            I(1, CilOperation.Call, Entity(UnknownKey)),
            I(2, CilOperation.Return));

        var result = Analyze(repository, [wrapperBody]);
        var effect = Effect(result, wrapperBody);

        Assert.Equal([0], effect.WrittenArguments);
        Assert.Equal([0], effect.EscapedArguments);
        Assert.True(effect.RequiresOwnedStorage(0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void EffectsReachEveryCallerOfAChainListedCallersFirst(int workers)
    {
        var repository = new Repository();
        // Each link passes its address to the next; only the last writes through it.
        var links = Enumerable.Range(0, 6)
            .Select(index => Method(new EntityKey(Assembly, 0x06000100 + index), Void, PairAddress))
            .ToArray();
        var outsider = Method(new EntityKey(Assembly, 0x06000200), Void, Pair);
        repository.Add([.. links, outsider]);
        var bodies = links
            .Select((link, index) => index == links.Length - 1
                ? Body(repository, link,
                    I(0, CilOperation.LoadArgument, Index(0)),
                    I(1, CilOperation.InitializeObject, Type(Pair)),
                    I(2, CilOperation.Return))
                : Body(repository, link,
                    I(0, CilOperation.LoadArgument, Index(0)),
                    I(1, CilOperation.Call, Entity(links[index + 1].Key)),
                    I(2, CilOperation.Return)))
            .Append(Body(repository, outsider,
                I(0, CilOperation.LoadArgumentAddress, Index(0)),
                I(1, CilOperation.Call, Entity(links[0].Key)),
                I(2, CilOperation.Return)))
            .ToArray();
        var withoutAddresses = Method(new EntityKey(Assembly, 0x06000201), Void, Pair);
        repository.Add(withoutAddresses);
        var unrelated = Body(repository, withoutAddresses,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.Pop),
            I(2, CilOperation.Return));

        var effects = new ManagedByRefEffectAnalyzer(
            repository, new Intrinsics(), new IndexedWorkExecutor(), workers).Analyze(new(
                bodies.Append(unrelated).ToImmutableDictionary(body => body.Method.Definition.Key),
                ImmutableDictionary<string, ManagedMethodBody>.Empty,
                ImmutableDictionary<string, DispatchCallSiteModel>.Empty));

        Assert.All(bodies, body => Assert.Equal([0], Effect(effects, body).WrittenArguments));
        Assert.Same(ManagedByRefMethodEffects.Empty, Effect(effects, unrelated));
    }

    [Fact]
    public void MutuallyRecursiveClosedCallsReachTheirFixedPoint()
    {
        var repository = new Repository();
        var first = Method(RecursiveAKey, Void, PairAddress);
        var second = Method(RecursiveBKey, Void, PairAddress);
        repository.Add(first, second);
        var firstBody = Body(repository, first,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.Call, Entity(RecursiveBKey)),
            I(2, CilOperation.Return));
        var secondBody = Body(repository, second,
            I(0, CilOperation.LoadArgument, Index(0)),
            I(1, CilOperation.Call, Entity(RecursiveAKey)),
            I(2, CilOperation.LoadArgument, Index(0)),
            I(3, CilOperation.InitializeObject, Type(Pair)),
            I(4, CilOperation.Return));

        var result = Analyze(repository, [firstBody, secondBody]);

        Assert.Equal([0], Effect(result, firstBody).WrittenArguments);
        Assert.Equal([0], Effect(result, secondBody).WrittenArguments);
    }

    [Fact]
    public void ConstructorAndAnalysisRejectMissingDependenciesAndInput()
    {
        var repository = new Repository();
        var intrinsics = new Intrinsics();

        var work = new IndexedWorkExecutor();
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedByRefEffectAnalyzer(null!, intrinsics, work, 1));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedByRefEffectAnalyzer(repository, null!, work, 1));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedByRefEffectAnalyzer(repository, intrinsics, null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ManagedByRefEffectAnalyzer(repository, intrinsics, work, 0));
        Assert.Throws<ArgumentNullException>(() =>
            Create(repository, intrinsics).Analyze(null!));
    }

    // Two workers, so the tests also run each round's methods in parallel.
    private static ManagedByRefEffectAnalyzer Create(
        Repository repository,
        IRuntimeIntrinsicRegistry? intrinsics = null) =>
        new(repository, intrinsics ?? new Intrinsics(), new IndexedWorkExecutor(), 2);

    private static ManagedByRefEffects Analyze(
        Repository repository,
        IReadOnlyCollection<ManagedMethodBody> bodies,
        IRuntimeIntrinsicRegistry? intrinsics = null) =>
        Create(repository, intrinsics)
            .Analyze(new(
                bodies.ToImmutableDictionary(body => body.Method.Definition.Key),
                ImmutableDictionary<string, ManagedMethodBody>.Empty,
                ImmutableDictionary<string, DispatchCallSiteModel>.Empty));

    private static ManagedByRefMethodEffects Effect(
        ManagedByRefEffects effects,
        ManagedMethodBody body) => effects.Methods[body.Method.CanonicalName];

    private static ManagedMethodBody Body(
        Repository repository,
        MethodDefinitionModel definition,
        params CilInstruction[] instructions) => BodyWithLocals(repository, definition, [], instructions);

    private static ManagedMethodBody BodyWithLocals(
        Repository repository,
        MethodDefinitionModel definition,
        ImmutableArray<CliTypeIdentity> locals,
        params CilInstruction[] instructions)
    {
        var instance = new MethodInstanceModel(definition,
            definition.DeclaringType == PairKey ? Pair : Owner, [], definition.Signature);
        var body = new CilMethodBody(definition, 8, [.. locals.Select(type => type.StackKind)], [.. instructions])
        {
            MethodInstance = instance,
            LocalSignatureTypes = locals,
            ExceptionRegions = repository.ExceptionRegions,
        };
        var graph = ((IControlFlowGraphBuilderFactory)
            new ControlFlowGraphBuilderFactory()).Create().Build(body);
        var validated = new TypedStackValidator(
            repository,
            repository,
            repository,
            new StackTypeCompatibilityValidator()).Validate(graph);
        return new(instance, validated);
    }

    private static MethodDefinitionModel Method(
        EntityKey key,
        CliTypeIdentity result,
        params CliTypeIdentity[] parameters) => new(
            key,
            OwnerKey,
            $"Method{key.MetadataToken:x8}",
            true,
            new(result, [.. parameters]),
            1);

    private static CilInstruction I(
        int offset,
        CilOperation operation,
        CilOperand? operand = null) => new(
            offset,
            offset + 1,
            operation,
            operand ?? new CilOperand.None());

    private static CilOperand.Index Index(int index) => new(index);
    private static CilOperand.Entity Entity(EntityKey key) => new(key);
    private static CilOperand.TypeIdentity Type(CliTypeIdentity type) =>
        new CilOperand.TypeIdentity(type);

    private sealed class Intrinsics(
        params (EntityKey Method, RuntimeIntrinsic Intrinsic)[] entries) :
        IRuntimeIntrinsicRegistry
    {
        private readonly ImmutableDictionary<EntityKey, RuntimeIntrinsic> _entries =
            entries.ToImmutableDictionary(entry => entry.Method, entry => entry.Intrinsic);

        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic) =>
            _entries.TryGetValue(method, out intrinsic);
    }

    private sealed class Repository :
        ITypeRepository,
        IFieldRepository,
        IMethodRepository
    {
        public Dictionary<EntityKey, MethodDefinitionModel> Methods { get; } = [];
        public Dictionary<EntityKey, FieldDefinitionModel> Fields { get; } = [];
        public ImmutableArray<CilExceptionRegion> ExceptionRegions { get; init; } = [];

        public void Add(params MethodDefinitionModel[] methods)
        {
            foreach (var method in methods)
            {
                Methods.Add(method.Key, method);
            }
        }

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => new(
            key,
            "Tests",
            key == PairKey ? "Pair" : "Owner",
            key == PairKey,
            [],
            []);

        public FieldDefinitionModel GetField(EntityKey key) => Fields[key];

        public MethodDefinitionModel GetMethod(EntityKey key) => Methods[key];
    }
}
