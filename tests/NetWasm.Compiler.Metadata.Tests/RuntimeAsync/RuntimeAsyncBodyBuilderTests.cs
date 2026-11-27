// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests.RuntimeAsync;

// The shapes the first runtime async lowering names (NW1001) rather than lowers, on bodies built here over CoreLib's
// types; Roslyn's output for the shapes it lowers runs in the compiler's tests.
public sealed class RuntimeAsyncBodyBuilderTests : IDisposable
{
    private static readonly AssemblyIdentity Assembly = new("runtime-async-shapes");
    private readonly TestAssets _assets = TestAssets.Create();
    private readonly IMetadataCompilationLease _lease;
    private readonly RuntimeAsyncMemberResolver _members;
    private readonly RuntimeAsyncFrameLayoutResolver _frames;
    private readonly IMethodRepository _methods;
    private readonly ITypeFinder _types;
    private readonly ITypeDefinitionResolver _definitions;
    private readonly IFieldRepository _fields;

    public RuntimeAsyncBodyBuilderTests()
    {
        var library = _assets.CompileSource("RuntimeAsyncShapes", "public static class Placeholder { }");
        _lease = MetadataCompilationTestFactory.Load(library, [_assets.CoreLib]);
        _members = MetadataCapabilityTestData.RuntimeAsyncMembers(_lease.Snapshot);
        _frames = MetadataCapabilityTestData.RuntimeAsyncFrames(_lease.Snapshot);
        _methods = MetadataCapabilityTestData.Methods(_lease.Snapshot);
        _types = MetadataCapabilityTestData.TypeFinder(_lease.Snapshot);
        _definitions = MetadataCapabilityTestData.TypeDefinitions(_lease.Snapshot);
        _fields = MetadataCapabilityTestData.Fields(_lease.Snapshot);
    }

    public void Dispose()
    {
        _lease.Dispose();
        _assets.Dispose();
    }

    public static TheoryData<string> Problems => new()
    {
        "returns", "by reference", "follow", "stack", "try", "handler", "filter",
    };

    [Theory]
    [MemberData(nameof(Problems))]
    public void NamesWhatItDoesNotLower(string problem)
    {
        var task = Identity("System.Threading.Tasks.Task");
        var integer = CliTypeIdentity.Primitive("int", CliValueKind.I4);
        var (returnType, parameters, isStatic, valueType) = problem switch
        {
            "returns" => (integer, ImmutableArray<CliTypeIdentity>.Empty, true, false),
            "by reference" => (task, [CliTypeIdentity.ManagedByReference(integer)], true, false),
            _ => (task, [], true, false),
        };
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000002), "M", isStatic,
            new MethodSignatureModel(returnType, parameters), 1);
        var method = new MethodInstanceModel(definition,
            CliTypeIdentity.Named(Assembly, "", "Shapes", valueType), [], definition.Signature);
        var await = new CilOperand.MethodInstance(AwaitTask());
        ImmutableArray<CilInstruction> instructions = problem switch
        {
            "follow" => [new(0, 1, CilOperation.CallVirtual, new CilOperand.None()), new(1, 2, CilOperation.Return, new CilOperand.None())],
            // A filter starts with the exception on the stack, which it awaits here; only the filter holds an await.
            "filter" => [
                new(0, 1, CilOperation.LoadNull, new CilOperand.None()),
                new(1, 2, CilOperation.Call, await),
                new(2, 3, CilOperation.Leave, new CilOperand.BranchTarget(7)),
                new(3, 4, CilOperation.CastClass, new CilOperand.TypeIdentity(task)),
                new(4, 5, CilOperation.Call, await),
                new(5, 6, CilOperation.Leave, new CilOperand.BranchTarget(7)),
                new(6, 7, CilOperation.Pop, new CilOperand.None()),
                new(7, 8, CilOperation.Return, new CilOperand.None())],
            // A type token's handle is a value whose type the lowering does not follow, so it cannot spill it.
            "stack" => [
                new(0, 1, CilOperation.LoadTypeToken, new CilOperand.None()),
                new(1, 2, CilOperation.LoadNull, new CilOperand.None()),
                new(2, 3, CilOperation.Call, await),
                new(3, 4, CilOperation.Pop, new CilOperand.None()),
                new(4, 5, CilOperation.Return, new CilOperand.None())],
            _ => [
                new(0, 1, CilOperation.LoadNull, new CilOperand.None()),
                new(1, 2, CilOperation.Call, await),
                new(2, 3, CilOperation.Leave, new CilOperand.BranchTarget(6)),
                new(3, 4, CilOperation.LoadNull, new CilOperand.None()),
                new(4, 5, CilOperation.Call, await),
                new(5, 6, CilOperation.Leave, new CilOperand.BranchTarget(6)),
                new(6, 7, CilOperation.Return, new CilOperand.None())],
        };
        var regions = problem switch
        {
            "try" => [new CilExceptionRegion(CilExceptionRegionKind.Finally, 0, 3, 3, 3, null, null)],
            "handler" => [new CilExceptionRegion(CilExceptionRegionKind.Fault, 6, 1, 3, 3, null, null)],
            "filter" => [new CilExceptionRegion(CilExceptionRegionKind.Filter, 2, 1, 6, 1, null, 3)],
            _ => ImmutableArray<CilExceptionRegion>.Empty,
        };
        var body = new CilMethodBody(definition, 4, [], instructions) { ExceptionRegions = regions, MethodInstance = method };
        var builder = new RuntimeAsyncBodyBuilder(() => _members, () => _frames, _definitions, new CilBodyLayout(), new Symbols(),
            new Stubs([]), _fields,
            new MetadataStackTypeResolver(_definitions), new AnyRuntimeAsyncMethodMaySuspend(), Guards(), DirectSites());

        var exception = Assert.Throws<CompilerException>(() => builder.Build(method, body));

        Assert.Equal(DiagnosticCode.UnsupportedCil, exception.Diagnostic.Code);
        Assert.Equal("Shapes::M", exception.Diagnostic.Method);
        Assert.Contains(problem switch
        {
            "returns" => "returns neither",
            "by reference" => "takes a parameter by reference",
            "follow" => "does not follow",
            "stack" => "a value it cannot type, on the evaluation stack across an await",
            _ => "inside an exception handler",
        }, exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    // A loop that starts the body, and locals the lowering does not save: pinned, by reference and byref-like ones,
    // which read their defaults after a resumption. An await no path reaches is named.
    [Fact]
    public void LowersALoopAtTheStartAndSkipsLocalsItCannotSave()
    {
        var task = Identity("System.Threading.Tasks.Task");
        var integer = CliTypeIdentity.Primitive("int", CliValueKind.I4);
        var span = CliTypeIdentity.GenericInstantiation(Identity("System.Span`1"), [integer]);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000002), "M", true,
            new MethodSignatureModel(task, []), 1);
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(Assembly, "", "Shapes", false), [],
            definition.Signature);
        var await = new CilOperand.MethodInstance(AwaitTask());
        var body = new CilMethodBody(definition, 4, [CliValueKind.I4, CliValueKind.ManagedAddress, CliValueKind.ValueType, CliValueKind.I4],
        [
            new(0, 1, CilOperation.LoadNull, new CilOperand.None()),
            new(1, 2, CilOperation.Call, await),
            new(2, 3, CilOperation.LoadLocal, new CilOperand.Index(3)),
            new(3, 4, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(0)),
            new(4, 5, CilOperation.Return, new CilOperand.None()),
        ])
        {
            LocalSignatureTypes = [integer, CliTypeIdentity.ManagedByReference(integer), span, integer],
            PinnedLocals = [0],
            MethodInstance = method,
        };
        var builder = new RuntimeAsyncBodyBuilder(() => _members, () => _frames, _definitions, new CilBodyLayout(), new Symbols(),
            new Stubs([]), _fields,
            new MetadataStackTypeResolver(_definitions), new AnyRuntimeAsyncMethodMaySuspend(), Guards(), DirectSites());

        var lowered = builder.Build(method, body);

        // Only local 3 is saved, besides the await's own temporary: the frame's type keeps an int and a Task in fields
        // of their own types, RuntimeAsyncFrame<RuntimeAsyncSlots<int, Task>, Task>, made from the method's delegate.
        var newFrame = lowered.Instructions.ToList().FindIndex(instruction => instruction is
            { Operation: CilOperation.NewObject, Operand: CilOperand.MethodInstance { Value.DeclaringType.ElementType.FullName: { } name } } &&
            name.StartsWith("System.Runtime.CompilerServices.RuntimeAsyncFrame", StringComparison.Ordinal));
        var frameType = Assert.IsType<CilOperand.MethodInstance>(lowered.Instructions[newFrame].Operand).Value.DeclaringType;
        Assert.Equal(["System.Runtime.CompilerServices.RuntimeAsyncSlots`2", "System.Threading.Tasks.Task"],
            frameType.TypeArguments.Select(type => type.ElementType?.FullName ?? type.FullName));
        Assert.Equal([CliValueKind.I4, CliValueKind.ManagedReference],
            frameType.TypeArguments[0].TypeArguments.Select(type => type.StackKind));
        // The int is saved and restored, and the Task saved, restored and then let go of (null stored), so a suspended
        // frame keeps only what its suspension keeps.
        Assert.Equal(1, Stores(lowered, "Item1"));
        Assert.Equal(2, Stores(lowered, "Item2"));
        Assert.Equal(1, lowered.Instructions.Count(instruction => instruction is
            { Operation: CilOperation.LoadField, Operand: CilOperand.FieldInstance { Value.Definition.Name: "Item2" } }));
        Assert.DoesNotContain(lowered.Instructions, instruction => instruction.Operation is CilOperation.Box or CilOperation.UnboxAny);
        // The frame is made at an activation's first suspension only: a later one, or a resumed activation's, branches
        // past its construction to reuse it, one call suspends it after its saves (where it resumes, the subscription,
        // the push), and a resumed activation takes it back as its own frame type.
        Assert.Equal(CilOperation.BranchIfTrue, lowered.Instructions[newFrame - 4].Operation);
        var instructions = lowered.Instructions.ToList();
        var suspend = instructions.FindIndex(instruction =>
            instruction.Operand is CilOperand.MethodInstance { Value.Definition.Name: "SuspendOn" });
        var save = instructions.FindIndex(newFrame, instruction =>
            instruction is { Operation: CilOperation.StoreField, Operand: CilOperand.FieldInstance { Value.Definition.Name: "Item2" } });
        Assert.InRange(save, newFrame, suspend);
        Assert.DoesNotContain(lowered.Instructions, instruction =>
            instruction.Operand is CilOperand.MethodInstance { Value.Definition.Name: "SuspendFrame" or "PushFrame" });
        Assert.Contains(lowered.Instructions, instruction =>
            instruction.Operand is CilOperand.MethodInstance { Value: { Definition.Name: "TakeResumingFrame", MethodArguments: [var taken] } } &&
            taken == frameType);
        var unreachable = body with
        {
            Instructions = [new(0, 1, CilOperation.Return, new CilOperand.None()), new(1, 2, CilOperation.LoadNull, new CilOperand.None()),
                new(2, 3, CilOperation.Call, await), new(3, 4, CilOperation.Return, new CilOperand.None())],
        };
        Assert.Contains("cannot reach", Assert.Throws<CompilerException>(() => builder.Build(method, unreachable)).Diagnostic.Message,
            StringComparison.Ordinal);
        // An await before a later loop is dispatched to directly, not through the loop's header.
        var before = body with
        {
            Instructions = [
                new(0, 1, CilOperation.LoadNull, new CilOperand.None()), new(1, 2, CilOperation.Call, await),
                new(2, 3, CilOperation.LoadNull, new CilOperand.None()), new(3, 4, CilOperation.Call, await),
                new(4, 5, CilOperation.LoadLocal, new CilOperand.Index(3)),
                new(5, 6, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(2)),
                new(6, 7, CilOperation.Return, new CilOperand.None())],
        };
        Assert.NotEmpty(builder.Build(method, before).Instructions);
    }

    // The dispatch tests a state only where it goes somewhere other than its default, the target most states share: at
    // the method's start the two awaits outside the loop are tested and the two inside go to the loop's head untested;
    // at the loop's head one of the two awaits inside is tested and the other is the default. Those outside the loop
    // never reach its head with a nonzero state. The tests stay a chain of equalities wasm-opt can make one br_table.
    [Fact]
    public void TheDispatchTestsEachTargetOnce()
    {
        var task = Identity("System.Threading.Tasks.Task");
        var integer = CliTypeIdentity.Primitive("int", CliValueKind.I4);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000002), "M", true,
            new MethodSignatureModel(task, []), 1);
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(Assembly, "", "Shapes", false), [],
            definition.Signature);
        var await = new CilOperand.MethodInstance(AwaitTask());
        var body = new CilMethodBody(definition, 4, [CliValueKind.I4],
        [
            new(0, 1, CilOperation.LoadNull, new CilOperand.None()),
            new(1, 2, CilOperation.Call, await),
            new(2, 3, CilOperation.LoadNull, new CilOperand.None()),
            new(3, 4, CilOperation.Call, await),
            new(4, 5, CilOperation.LoadNull, new CilOperand.None()),
            new(5, 6, CilOperation.Call, await),
            new(6, 7, CilOperation.LoadLocal, new CilOperand.Index(0)),
            new(7, 8, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(2)),
            new(8, 9, CilOperation.LoadNull, new CilOperand.None()),
            new(9, 10, CilOperation.Call, await),
            new(10, 11, CilOperation.Return, new CilOperand.None()),
        ])
        {
            LocalSignatureTypes = [integer],
            MethodInstance = method,
        };
        var builder = new RuntimeAsyncBodyBuilder(() => _members, () => _frames, _definitions, new CilBodyLayout(), new Symbols(),
            new Stubs([]), _fields,
            new MetadataStackTypeResolver(_definitions), new AnyRuntimeAsyncMethodMaySuspend(), Guards(), DirectSites());

        var lowered = builder.Build(method, body);

        Assert.Equal(3, lowered.Instructions.Count(instruction => instruction.Operation == CilOperation.BranchIfEqual));
        // A resumed activation's exception goes to the fault slot, so the driver resumes it outside a protected block;
        // a direct or Normal call still rethrows or returns a faulted task.
        Assert.Contains(lowered.Instructions, instruction =>
            instruction.Operand is CilOperand.MethodInstance { Value.Definition.Name: "Fault" });
        Assert.Contains(lowered.Instructions, instruction => instruction.Operation == CilOperation.Rethrow);
    }

    // A method with more parameters than a frame's resumer delegate takes (Func<T1, ..., T16, TResult>) resumes through
    // its resumption stub: the frame takes no parameters, and its delegate is over the stub, not the method.
    [Fact]
    public void AMethodWithMoreParametersThanADelegateResumesThroughItsStub()
    {
        var task = Identity("System.Threading.Tasks.Task");
        var integer = CliTypeIdentity.Primitive("int", CliValueKind.I4);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000002), "M", true,
            new MethodSignatureModel(task, [.. Enumerable.Repeat(integer, 17)]), 1);
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(Assembly, "", "Shapes", false), [],
            definition.Signature);
        var stubKey = new EntityKey(Assembly, RuntimeAsyncResumers.Token(0x06000001));
        var stub = new MethodDefinitionModel(stubKey, new(Assembly, 0x02000002), "<resume>M", true,
            new MethodSignatureModel(task, []), 0) { RuntimeAsyncResumer = definition.Key };
        var body = new CilMethodBody(definition, 4, [],
        [
            new(0, 1, CilOperation.LoadNull, new CilOperand.None()),
            new(1, 2, CilOperation.Call, new CilOperand.MethodInstance(AwaitTask())),
            new(2, 3, CilOperation.Return, new CilOperand.None()),
        ]) { MethodInstance = method };
        var builder = new RuntimeAsyncBodyBuilder(() => _members, () => _frames, _definitions, new CilBodyLayout(), new Symbols(),
            new Stubs([stub]), _fields,
            new MetadataStackTypeResolver(_definitions), new AnyRuntimeAsyncMethodMaySuspend(), Guards(), DirectSites());

        var lowered = builder.Build(method, body);

        // The method names itself when it takes its call mode; the frame's delegate is over the stub.
        Assert.Equal(
            [definition.Key, stubKey],
            lowered.Instructions.Where(instruction => instruction.Operation == CilOperation.LoadFunction)
                .Select(instruction => Assert.IsType<CilOperand.MethodInstance>(instruction.Operand).Value.Definition.Key));
        Assert.Contains(lowered.Instructions, instruction => instruction is
        {
            Operation: CilOperation.NewObject,
            Operand: CilOperand.MethodInstance { Value.DeclaringType.TypeArguments.Length: 2 },
        });
    }

    // The production guards: a method's own catch, and none for a separate body.
    private RuntimeAsyncActivationGuardSelector Guards() => new(
        new SelfGuardedActivation(() => _members, new RuntimeAsyncCatchEmitter(() => _members)), new EntryGuardedBody());

    // The production direct calls: through a request, or at a separate body where the callee has one.
    private RuntimeAsyncDirectSiteEmitter DirectSites() => new(() => _members, new Stubs([]), new SeparateRuntimeAsyncBodies());

    private sealed class Stubs(ImmutableArray<MethodDefinitionModel> stubs) : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => stubs.Single(stub => stub.Key == key);
    }

    // An await of the task a statically bound call of a runtime-async method has just returned calls it directly: no
    // task, no subscription, the result from the slot. Any other callee, or an await a branch reaches, stays an await of
    // the task (a virtual call has its own request: AwaitsAVirtualCallThroughTheOverridesRequest).
    [Theory]
    [InlineData("direct", true)]
    [InlineData("not runtime async", false)]
    [InlineData("branch target", false)]
    public void CallsALoweredMethodDirectlyWhenItsTaskIsAwaitedAtOnce(string shape, bool direct)
    {
        var task = Identity("System.Threading.Tasks.Task");
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000002), "M", true,
            new MethodSignatureModel(task, []), 1);
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(Assembly, "", "Shapes", false), [],
            definition.Signature);
        var calleeDefinition = new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), "N",
            shape != "virtual", new MethodSignatureModel(task, []), 0)
        {
            IsRuntimeAsync = shape != "not runtime async",
            IsVirtual = shape == "virtual",
        };
        var callee = new CilOperand.MethodInstance(new MethodInstanceModel(calleeDefinition,
            CliTypeIdentity.Named(Assembly, "", "Shapes", false), [], calleeDefinition.Signature));
        ImmutableArray<CilInstruction> instructions =
        [
            new(0, 1, CilOperation.Call, callee),
            new(1, 2, CilOperation.Call, new CilOperand.MethodInstance(AwaitTask())),
            new(2, 3, CilOperation.Return, new CilOperand.None())];
        if (shape == "virtual")
        {
            // An instance call through the receiver: its target is chosen at run time.
            instructions =
            [
                new(0, 1, CilOperation.LoadNull, new CilOperand.None()),
                new(1, 2, CilOperation.CallVirtual, callee),
                new(2, 3, CilOperation.Call, new CilOperand.MethodInstance(AwaitTask())),
                new(3, 4, CilOperation.Return, new CilOperand.None())];
        }
        if (shape == "branch target")
        {
            // A path that reaches the await with a task of its own.
            instructions =
            [
                new(0, 1, CilOperation.LoadNull, new CilOperand.None()),
                new(1, 2, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
                new(2, 3, CilOperation.Call, callee),
                new(3, 4, CilOperation.Branch, new CilOperand.BranchTarget(5)),
                new(4, 5, CilOperation.LoadNull, new CilOperand.None()),
                new(5, 6, CilOperation.Call, new CilOperand.MethodInstance(AwaitTask())),
                new(6, 7, CilOperation.Return, new CilOperand.None())];
        }
        var body = new CilMethodBody(definition, 2, [], instructions) { MethodInstance = method };
        var builder = new RuntimeAsyncBodyBuilder(() => _members, () => _frames, _definitions, new CilBodyLayout(), new Symbols(),
            new Stubs([]), _fields,
            new MetadataStackTypeResolver(_definitions), new AnyRuntimeAsyncMethodMaySuspend(), Guards(), DirectSites());

        var called = builder.Build(method, body).Instructions
            .Select(instruction => instruction.Operand is CilOperand.MethodInstance call ? call.Value.Definition.Name : null)
            .OfType<string>()
            .ToArray();

        Assert.Equal(direct, called.Contains("CallDirect"));
        Assert.Equal(direct, called.Contains("TakeVoid"));
        Assert.Equal(!direct, called.Contains("SuspendOn"));
        Assert.Equal(direct, called.Contains("SuspendDirect"));
    }

    // A direct await of a non-virtual method that cannot suspend and whose Task's result is a reference takes the result as
    // the call's value (TakeReturned), and such a method completes through CompleteReturning, so no slot is written, read
    // and cleared; a value-type result, or a virtual callee (a base call), keeps the slot (TakeResult, Complete).
    [Theory]
    [InlineData("System.String", false, true)]
    [InlineData("System.Int64", false, false)]
    [InlineData("System.String", true, false)]
    public void ADirectAwaitTakesAReferenceResultAsTheCallsValue(string resultName, bool virtualCallee, bool returned)
    {
        var result = Identity(resultName);
        var taskOf = CliTypeIdentity.GenericInstantiation(Identity("System.Threading.Tasks.Task`1"), [result]);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000002), "M", true,
            new MethodSignatureModel(taskOf, []), 1);
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(Assembly, "", "Shapes", false), [],
            definition.Signature);
        var calleeDefinition = new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), "N",
            !virtualCallee, new MethodSignatureModel(taskOf, []), 0)
        {
            IsRuntimeAsync = true,
            IsVirtual = virtualCallee,
        };
        var callee = new CilOperand.MethodInstance(new MethodInstanceModel(calleeDefinition,
            CliTypeIdentity.Named(Assembly, "", "Shapes", false), [], calleeDefinition.Signature));
        ImmutableArray<CilInstruction> instructions = virtualCallee
            ?
            [
                new(0, 1, CilOperation.LoadNull, new CilOperand.None()),
                new(1, 2, CilOperation.Call, callee),
                new(2, 3, CilOperation.Call, new CilOperand.MethodInstance(AwaitTaskOf(result))),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ]
            :
            [
                new(0, 1, CilOperation.Call, callee),
                new(1, 2, CilOperation.Call, new CilOperand.MethodInstance(AwaitTaskOf(result))),
                new(2, 3, CilOperation.Return, new CilOperand.None()),
            ];
        var body = new CilMethodBody(definition, 2, [], instructions) { MethodInstance = method };
        var builder = new RuntimeAsyncBodyBuilder(() => _members, () => _frames, _definitions, new CilBodyLayout(), new Symbols(),
            new Stubs([]), _fields, new MetadataStackTypeResolver(_definitions), new NothingSuspends(), Guards(), DirectSites());

        var called = builder.Build(method, body).Instructions
            .Select(instruction => instruction.Operand is CilOperand.MethodInstance call ? call.Value.Definition.Name : null)
            .OfType<string>()
            .ToArray();

        Assert.Equal(returned, called.Contains("TakeReturned"));
        Assert.Equal(!returned, called.Contains("TakeResult"));
        // The method itself is non-virtual and never suspends: a reference result completes through the call's value.
        var referenceResult = resultName == "System.String";
        Assert.Equal(referenceResult, called.Contains("CompleteReturning"));
        Assert.Equal(!referenceResult, called.Contains("Complete"));
    }

    // An await of a virtual call asks the override that runs to take it (CallDirectVirtual); the await takes the direct
    // path or the task's, with the result from the slot either way. A call through a constrained. prefix stays an await of
    // the task.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AwaitsAVirtualCallThroughTheOverridesRequest(bool constrained)
    {
        var task = Identity("System.Threading.Tasks.Task");
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000002), "M", true,
            new MethodSignatureModel(task, []), 1);
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(Assembly, "", "Shapes", false), [],
            definition.Signature);
        var calleeDefinition = new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), "N",
            false, new MethodSignatureModel(task, []), 0) { IsVirtual = true };
        var callee = new CilOperand.MethodInstance(new MethodInstanceModel(calleeDefinition,
            CliTypeIdentity.Named(Assembly, "", "Shapes", false), [], calleeDefinition.Signature));
        var body = new CilMethodBody(definition, 2, [],
        [
            new(0, 1, CilOperation.LoadNull, new CilOperand.None()),
            constrained
                ? new(1, 2, CilOperation.Constrained, new CilOperand.TypeIdentity(CliTypeIdentity.Named(Assembly, "", "Shapes", false)))
                : new(1, 2, CilOperation.Nop, new CilOperand.None()),
            new(2, 3, CilOperation.CallVirtual, callee),
            new(3, 4, CilOperation.Call, new CilOperand.MethodInstance(AwaitTask())),
            new(4, 5, CilOperation.Return, new CilOperand.None()),
        ]) { MethodInstance = method };
        var builder = new RuntimeAsyncBodyBuilder(() => _members, () => _frames, _definitions, new CilBodyLayout(), new Symbols(),
            new Stubs([]), _fields,
            new MetadataStackTypeResolver(_definitions), new AnyRuntimeAsyncMethodMaySuspend(), Guards(), DirectSites());

        var called = builder.Build(method, body).Instructions
            .Select(instruction => instruction.Operand is CilOperand.MethodInstance call ? call.Value.Definition.Name : null)
            .OfType<string>()
            .ToArray();

        string[] viaRequest = ["CallDirectVirtual", "VirtualDone", "SuspendVirtual", "VirtualResumed", "VirtualVoid"];
        Assert.All(viaRequest, name => Assert.Equal(!constrained, called.Contains(name)));
        Assert.Equal(constrained, called.Contains("SuspendOn"));
        Assert.DoesNotContain("CallDirect", called);
    }

    // `await N().ConfigureAwait(b)` with a constant b calls N directly too: the constant and the ConfigureAwait call go,
    // and with false the frame suspends without its SynchronizationContext. A flag the method computes keeps the task.
    [Theory]
    [InlineData("false", true)]
    [InlineData("true", true)]
    [InlineData("computed", false)]
    public void CallsALoweredMethodDirectlyThroughConfigureAwait(string flag, bool direct)
    {
        var task = Identity("System.Threading.Tasks.Task");
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000002), "M", true,
            new MethodSignatureModel(task, [CliTypeIdentity.Primitive("bool", CliValueKind.I4)]), 1);
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(Assembly, "", "Shapes", false), [],
            definition.Signature);
        var calleeDefinition = new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), "N", true,
            new MethodSignatureModel(task, []), 0) { IsRuntimeAsync = true };
        var callee = new CilOperand.MethodInstance(new MethodInstanceModel(calleeDefinition,
            CliTypeIdentity.Named(Assembly, "", "Shapes", false), [], calleeDefinition.Signature));
        var taskType = _types.FindType("System.Threading.Tasks.Task");
        var configure = taskType.Methods.Select(_methods.GetMethod).Single(candidate => candidate.Name == "ConfigureAwait" &&
            candidate.Signature.ParameterSignatureTypes is [{ CanonicalName: "primitive:bool" }]);
        var helpers = _types.FindType("System.Runtime.CompilerServices.AsyncHelpers");
        var awaitConfigured = helpers.Methods.Select(_methods.GetMethod).Single(candidate => candidate.Name == "Await" &&
            candidate.GenericArity == 0 && candidate.Signature.ParameterSignatureTypes is
                [{ FullName: "System.Runtime.CompilerServices.ConfiguredTaskAwaitable" }]);
        var body = new CilMethodBody(definition, 2, [],
        [
            new(0, 1, CilOperation.Call, callee),
            flag == "computed"
                ? new(1, 2, CilOperation.LoadArgument, new CilOperand.Index(0))
                : new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(flag == "true" ? 1 : 0)),
            new(2, 3, CilOperation.CallVirtual, new CilOperand.MethodInstance(new MethodInstanceModel(configure, task, [],
                configure.Signature))),
            new(3, 4, CilOperation.Call, new CilOperand.MethodInstance(new MethodInstanceModel(awaitConfigured,
                CliTypeIdentity.FromDefinition(helpers), [], awaitConfigured.Signature))),
            new(4, 5, CilOperation.Return, new CilOperand.None()),
        ]) { MethodInstance = method };
        var builder = new RuntimeAsyncBodyBuilder(() => _members, () => _frames, _definitions, new CilBodyLayout(), new Symbols(),
            new Stubs([]), _fields,
            new MetadataStackTypeResolver(_definitions), new AnyRuntimeAsyncMethodMaySuspend(), Guards(), DirectSites());

        var called = builder.Build(method, body).Instructions
            .Select(instruction => instruction.Operand is CilOperand.MethodInstance call ? call.Value.Definition.Name : null)
            .OfType<string>()
            .ToArray();

        Assert.Equal(direct, called.Contains("CallDirect"));
        Assert.Equal(!direct, called.Contains("ConfigureAwait"));
        Assert.Equal(!direct, called.Contains("SuspendOn"));
        Assert.Equal(direct && flag == "false", called.Contains("SuspendDirectAnywhere"));
        Assert.Equal(direct && flag == "true", called.Contains("SuspendDirect"));
    }

    // A ValueTask's ConfigureAwait goes through a local (`call N; stloc v; ldloca v; ldc.i4.0; call ConfigureAwait`),
    // which Roslyn reuses across statements: the call is direct and the pattern dropped when only such patterns use the
    // local, and stays an await of the task when anything else reads it.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void CallsALoweredMethodDirectlyThroughAValueTasksConfigureAwait(bool readElsewhere, bool direct)
    {
        var valueTask = Identity("System.Threading.Tasks.ValueTask");
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000002), "M", true,
            new MethodSignatureModel(Identity("System.Threading.Tasks.Task"), []), 1);
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(Assembly, "", "Shapes", false), [],
            definition.Signature);
        var calleeDefinition = new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), "N", true,
            new MethodSignatureModel(valueTask, []), 0) { IsRuntimeAsync = true };
        var callee = new CilOperand.MethodInstance(new MethodInstanceModel(calleeDefinition,
            CliTypeIdentity.Named(Assembly, "", "Shapes", false), [], calleeDefinition.Signature));
        var valueTaskType = _types.FindType("System.Threading.Tasks.ValueTask");
        var configure = valueTaskType.Methods.Select(_methods.GetMethod).Single(candidate =>
            candidate.Name == "ConfigureAwait" && candidate.Signature.ParameterSignatureTypes is [{ CanonicalName: "primitive:bool" }]);
        var helpers = _types.FindType("System.Runtime.CompilerServices.AsyncHelpers");
        var awaitConfigured = helpers.Methods.Select(_methods.GetMethod).Single(candidate => candidate.Name == "Await" &&
            candidate.GenericArity == 0 && candidate.Signature.ParameterSignatureTypes is
                [{ FullName: "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable" }]);
        ImmutableArray<CilInstruction> instructions =
        [
            new(0, 1, CilOperation.Call, callee),
            new(1, 2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            new(2, 3, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            new(3, 4, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            new(4, 5, CilOperation.Call, new CilOperand.MethodInstance(new MethodInstanceModel(configure, valueTask, [],
                configure.Signature))),
            new(5, 6, CilOperation.Call, new CilOperand.MethodInstance(new MethodInstanceModel(awaitConfigured,
                CliTypeIdentity.FromDefinition(helpers), [], awaitConfigured.Signature))),
            .. readElsewhere
                ? new CilInstruction[] { new(6, 7, CilOperation.LoadLocal, new CilOperand.Index(0)), new(7, 8, CilOperation.Pop, new CilOperand.None()) }
                : [],
            new(readElsewhere ? 8 : 6, readElsewhere ? 9 : 7, CilOperation.Return, new CilOperand.None()),
        ];
        var body = new CilMethodBody(definition, 2, [CliValueKind.ValueType], instructions)
        {
            LocalSignatureTypes = [valueTask],
            MethodInstance = method,
        };
        var builder = new RuntimeAsyncBodyBuilder(() => _members, () => _frames, _definitions, new CilBodyLayout(), new Symbols(),
            new Stubs([]), _fields,
            new MetadataStackTypeResolver(_definitions), new AnyRuntimeAsyncMethodMaySuspend(), Guards(), DirectSites());

        var called = builder.Build(method, body).Instructions
            .Select(instruction => instruction.Operand is CilOperand.MethodInstance call ? call.Value.Definition.Name : null)
            .OfType<string>()
            .ToArray();

        Assert.Equal(direct, called.Contains("CallDirect"));
        Assert.Equal(direct, called.Contains("SuspendDirectAnywhere"));
        Assert.Equal(!direct, called.Contains("ConfigureAwait"));
    }

    [Fact]
    public void RejectsMissingArguments()
    {
        var builder = new RuntimeAsyncBodyBuilder(() => _members, () => _frames, _definitions, new CilBodyLayout(), new Symbols(),
            new Stubs([]), _fields,
            new MetadataStackTypeResolver(_definitions), new AnyRuntimeAsyncMethodMaySuspend(), Guards(), DirectSites());
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000002), "M", true,
            new MethodSignatureModel(CliValueKind.Void, []), 1);
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(Assembly, "", "Shapes", false), [], definition.Signature);
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!, new(definition, 1, [], [])));
        Assert.Throws<ArgumentNullException>(() => builder.Build(method, null!));
    }

    // Protected blocks that start where an enclosing one does, as Roslyn starts C#'s
    // `try { try { } finally { } await ...; } finally { }` at one offset, while the enclosing block holds an await
    // outside them: the enclosing block's dispatch runs before they start, so no branch but a leave crosses into or out
    // of a protected block. "none": the nested block holds no await; "await": it holds one of the two; "catch": nested
    // catches; "loop": a loop at the nested block's start, whose back edge stays inside it.
    [Theory]
    [InlineData("none")]
    [InlineData("await")]
    [InlineData("catch")]
    [InlineData("loop")]
    public void NestedProtectedBlocksAtOneStartAreEnteredAtTheirStarts(string shape)
    {
        var task = Identity("System.Threading.Tasks.Task");
        var exceptionType = _types.FindType("System.Exception");
        var exception = CliTypeIdentity.FromDefinition(exceptionType);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000002), "M", true,
            new MethodSignatureModel(task, []), 1);
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(Assembly, "", "Shapes", false), [],
            definition.Signature);
        var await = new CilOperand.MethodInstance(AwaitTask());
        var handlerEnd = shape == "catch" ? CilOperation.Leave : CilOperation.EndFinally;
        var kind = shape == "catch" ? CilExceptionRegionKind.Catch : CilExceptionRegionKind.Finally;
        EntityKey? caught = shape == "catch" ? exceptionType.Key : null;
        // 1-6: the enclosing protected block, holding the inner one (1-3, its handler 4) and an await (5-7).
        ImmutableArray<CilInstruction> instructions =
        [
            new(0, 1, CilOperation.Nop, new CilOperand.None()),
            .. shape switch
            {
                "await" => new CilInstruction[]
                {
                    new(1, 2, CilOperation.LoadNull, new CilOperand.None()),
                    new(2, 3, CilOperation.Call, await),
                },
                "loop" => [
                    new(1, 2, CilOperation.LoadNull, new CilOperand.None()),
                    new(2, 3, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(1)),
                ],
                _ => [new(1, 2, CilOperation.Nop, new CilOperand.None()), new(2, 3, CilOperation.Nop, new CilOperand.None())],
            },
            new(3, 4, CilOperation.Leave, new CilOperand.BranchTarget(5)),
            shape == "catch"
                ? new(4, 5, CilOperation.Leave, new CilOperand.BranchTarget(5))
                : new(4, 5, handlerEnd, new CilOperand.None()),
            new(5, 6, CilOperation.LoadNull, new CilOperand.None()),
            new(6, 7, CilOperation.Call, await),
            new(7, 8, CilOperation.Leave, new CilOperand.BranchTarget(9)),
            shape == "catch"
                ? new(8, 9, CilOperation.Leave, new CilOperand.BranchTarget(9))
                : new(8, 9, CilOperation.EndFinally, new CilOperand.None()),
            new(9, 10, CilOperation.Return, new CilOperand.None()),
        ];
        var regions = ImmutableArray.Create(
            new CilExceptionRegion(kind, 1, 3, 4, 1, caught, null) { CatchTypeIdentity = caught is null ? null : exception },
            new CilExceptionRegion(kind, 1, 7, 8, 1, caught, null) { CatchTypeIdentity = caught is null ? null : exception });
        var body = new CilMethodBody(definition, 4, [], instructions) { ExceptionRegions = regions, MethodInstance = method };
        var builder = new RuntimeAsyncBodyBuilder(() => _members, () => _frames, _definitions, new CilBodyLayout(), new Symbols(),
            new Stubs([]), _fields,
            new MetadataStackTypeResolver(_definitions), new AnyRuntimeAsyncMethodMaySuspend(), Guards(), DirectSites());

        var lowered = builder.Build(method, body);

        var crossing = lowered.Instructions
            .Where(instruction => instruction.Operation is not (CilOperation.Leave or CilOperation.EndFinally))
            .SelectMany(instruction => Targets(instruction).Select(target => (instruction.Offset, Target: target)))
            .Where(branch => !Zones(lowered, branch.Offset).SequenceEqual(Zones(lowered, branch.Target)))
            .ToArray();
        Assert.True(crossing.Length == 0, string.Join("\n", crossing.Select(branch => $"IL_{branch.Offset:x4} -> IL_{branch.Target:x4}")
            .Concat(lowered.ExceptionRegions.Select(region =>
                $"{region.Kind} try {region.TryOffset:x4}+{region.TryLength:x} handler {region.HandlerOffset:x4}+{region.HandlerLength:x}"))
            .Concat(lowered.Instructions.Select(instruction => $"IL_{instruction.Offset:x4}: {instruction.Operation} {instruction.Operand}"))));
        // Every protected block the source started at one offset still holds what it held, the inner one inside the
        // enclosing one.
        var tries = lowered.ExceptionRegions.Take(2).ToArray();
        Assert.True(tries[1].TryOffset < tries[0].TryOffset && tries[0].TryOffset + tries[0].TryLength <= tries[1].TryOffset + tries[1].TryLength);
    }

    // A value under the awaited one that the decoder names by its definition: a field of a type that is not generic
    // (C#'s `codec.Count = await ...`, with the codec read from such a field, leaves it there) and a narrowed integer.
    // Both are spilled across the await in locals of their types rather than named (NW1001).
    [Theory]
    [InlineData("field")]
    [InlineData("conversion")]
    public void SpillsAFieldNamedByItsDefinitionAndAConvertedInteger(string value)
    {
        var task = Identity("System.Threading.Tasks.Task");
        var stringType = _types.FindType("System.String");
        var empty = stringType.Fields.Select(_fields.GetField).Single(field => field.Name == "Empty");
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000002), "M", true,
            new MethodSignatureModel(task, []), 1);
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(Assembly, "", "Shapes", false), [],
            definition.Signature);
        var body = new CilMethodBody(definition, 4, [],
        [
            value == "field"
                ? new(0, 1, CilOperation.LoadStaticField, new CilOperand.Entity(empty.Key))
                : new(0, 1, CilOperation.ConvertNumeric, new CilOperand.NumericConversion(8, true, false, false, false)),
            new(1, 2, CilOperation.LoadNull, new CilOperand.None()),
            new(2, 3, CilOperation.Call, new CilOperand.MethodInstance(AwaitTask())),
            new(3, 4, CilOperation.Pop, new CilOperand.None()),
            new(4, 5, CilOperation.Return, new CilOperand.None()),
        ])
        {
            MethodInstance = method,
        };
        if (value == "conversion")
            body = body with { Instructions = [new(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(300)),
                .. body.Instructions.Select(instruction => instruction with
                {
                    Offset = instruction.Offset + 1,
                    NextOffset = instruction.NextOffset + 1,
                })] };
        var builder = new RuntimeAsyncBodyBuilder(() => _members, () => _frames, _definitions, new CilBodyLayout(), new Symbols(),
            new Stubs([]), _fields,
            new MetadataStackTypeResolver(_definitions), new AnyRuntimeAsyncMethodMaySuspend(), Guards(), DirectSites());

        var lowered = builder.Build(method, body);

        // The spilled value has a local of its own type: the field's, or the int the narrowing pushes.
        var expected = value == "field" ? empty.SignatureType : CliTypeIdentity.FromStackKind(CliValueKind.I4);
        Assert.Contains(expected, lowered.LocalSignatureTypes);
    }

    private static ImmutableArray<int> Targets(CilInstruction instruction) => instruction.Operand switch
    {
        CilOperand.BranchTarget target => [target.Offset],
        CilOperand.SwitchTargets targets => targets.Offsets,
        _ => [],
    };

    // The protected blocks and handlers that hold the offset, outermost first.
    private static IEnumerable<(int Start, int End)> Zones(CilMethodBody body, int offset) =>
        body.ExceptionRegions
            .SelectMany(region => new[]
            {
                (Start: region.TryOffset, End: region.TryOffset + region.TryLength),
                (Start: region.HandlerOffset, End: region.HandlerOffset + region.HandlerLength),
            })
            .Where(zone => offset >= zone.Start && offset < zone.End)
            .Distinct()
            .OrderBy(zone => zone.Start)
            .ThenByDescending(zone => zone.End);

    // AsyncHelpers.Await(Task).
    private MethodInstanceModel AwaitTask()
    {
        var helpers = _types.FindType("System.Runtime.CompilerServices.AsyncHelpers");
        var await = helpers.Methods.Select(_methods.GetMethod).Single(method => method.Name == "Await" &&
            method.GenericArity == 0 && method.Signature.ParameterSignatureTypes is [{ FullName: "System.Threading.Tasks.Task" }]);
        return new(await, CliTypeIdentity.FromDefinition(helpers), [], await.Signature);
    }

    private CliTypeIdentity Identity(string fullName) => CliTypeIdentity.FromDefinition(_types.FindType(fullName));

    // AsyncHelpers.Await<T>(Task<T>), instantiated.
    private MethodInstanceModel AwaitTaskOf(CliTypeIdentity result)
    {
        var helpers = _types.FindType("System.Runtime.CompilerServices.AsyncHelpers");
        var await = helpers.Methods.Select(_methods.GetMethod).Single(method => method.Name == "Await" &&
            method.GenericArity == 1 && method.Signature.ParameterSignatureTypes is [{ Shape: CliTypeShape.GenericInstantiation } parameter] &&
            parameter.ElementType!.FullName == "System.Threading.Tasks.Task`1");
        return new(await, CliTypeIdentity.FromDefinition(helpers), [result],
            new MethodSignatureModel(result, [CliTypeIdentity.GenericInstantiation(Identity("System.Threading.Tasks.Task`1"), [result])]));
    }

    private sealed class NothingSuspends : IRuntimeAsyncSuspensionFacts
    {
        public bool CanSuspend(MethodInstanceModel method) => false;
    }

    private sealed class Symbols : ISymbolFormatter
    {
        public string Format(EntityKey key) => "Shapes";

        public string Format(MethodDefinitionModel method) => "Shapes::" + method.Name;
    }

    private static int Stores(CilMethodBody body, string field) => body.Instructions.Count(instruction => instruction is
        { Operation: CilOperation.StoreField, Operand: CilOperand.FieldInstance { Value.Definition.Name: var name } } && name == field);
}
