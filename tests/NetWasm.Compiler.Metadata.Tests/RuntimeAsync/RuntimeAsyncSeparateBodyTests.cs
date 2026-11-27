// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests.RuntimeAsync;

// A method written as runtime async runs its lowered body in a method beside it: the method's entry takes the call mode
// and calls the body inside the catch, or outside it for a direct call where no context can change; the body takes the
// mode as its last argument and has no catch of its own; a direct await calls a callee's body, with no request, under the
// same condition, keeping its opcode.
public sealed class RuntimeAsyncSeparateBodyTests : IDisposable
{
    private static readonly AssemblyIdentity Assembly = new("runtime-async-bodies");
    private static readonly CliTypeIdentity Owner = CliTypeIdentity.Named(Assembly, "", "Shapes", false);
    private readonly TestAssets _assets = TestAssets.Create();
    private readonly IMetadataCompilationLease _lease;
    private readonly RuntimeAsyncMemberResolver _members;
    private readonly RuntimeAsyncFrameLayoutResolver _frames;
    private readonly IMethodRepository _methods;
    private readonly ITypeFinder _types;
    private readonly ITypeDefinitionResolver _definitions;
    private readonly IFieldRepository _fields;

    public RuntimeAsyncSeparateBodyTests()
    {
        var library = _assets.CompileSource("RuntimeAsyncBodies", "public static class Placeholder { }");
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

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void TheEntryCallsTheBodyInsideItsCatchAndOutsideItForAVirtualDirectCall(bool isStatic, bool isVirtual)
    {
        var (method, body) = Pair(0x06000001, isStatic, [Identity("System.String")], isVirtual);
        var entry = new RuntimeAsyncEntryBuilder(() => _members, new Repository(method.Definition, body), new CilBodyLayout(),
            new RuntimeAsyncCatchEmitter(() => _members), new Symbols()).Build(method);

        var instructions = entry.Instructions;
        var region = Assert.Single(entry.ExceptionRegions);
        Assert.Equal(CilExceptionRegionKind.Catch, region.Kind);
        Assert.NotNull(region.PassThroughLocal);
        var bodyCalls = instructions.Where(instruction => Calls(instruction, body.Key)).ToArray();
        // A virtual method's first call, a virtual direct one's where no context can change, lies outside the protected
        // block; the last, every other call's, inside. Any other method's body has that one caller.
        Assert.Equal(isVirtual ? 2 : 1, bodyCalls.Length);
        Assert.Equal(isVirtual, !Inside(region, bodyCalls[0].Offset));
        Assert.True(Inside(region, bodyCalls[^1].Offset));
        var names = Names(instructions);
        Assert.Equal(isVirtual ? "TakeVirtualCallMode" : "TakeCallMode", names[0]);
        Assert.Equal(isVirtual, names.Contains("ContextsMayChange"));
        Assert.Contains("ClearDirect", names);
        Assert.Contains("Fault", names);
        // Each body call passes every argument, `this` first where there is one, then the mode.
        var arguments = (isStatic ? 0 : 1) + method.Signature.ParameterSignatureTypes.Length;
        var index = instructions.IndexOf(bodyCalls[^1]);
        Assert.Equal(Enumerable.Range(0, arguments).Select(argument => new CilOperand.Index(argument)),
            instructions[(index - arguments - 1)..(index - 1)].Select(instruction => instruction.Operand));
        Assert.Equal(CilOperation.LoadLocal, instructions[index - 1].Operation);
    }

    [Fact]
    public void TheBodyTakesItsModeFromItsLastArgumentAndHasNoCatch()
    {
        var (method, body) = Pair(0x06000002, true, [Identity("System.String")]);
        var bodyInstance = RuntimeAsyncBodies.For(new Repository(method.Definition, body), method);
        var source = new CilMethodBody(method.Definition, 1, [],
        [
            new(0, 1, CilOperation.Return, new CilOperand.None()),
        ]) { MethodInstance = method };

        var lowered = Builder(new Repository(method.Definition, body)).Build(bodyInstance,
            source with { Instructions = [new(0, 1, CilOperation.Call, new CilOperand.MethodInstance(CompletedTask())), new(1, 2, CilOperation.Return, new CilOperand.None())] });

        Assert.Empty(lowered.ExceptionRegions);
        var names = Names(lowered.Instructions);
        Assert.DoesNotContain("TakeCallMode", names);
        Assert.DoesNotContain("TakeVirtualCallMode", names);
        Assert.Contains(lowered.Instructions, instruction =>
            instruction.Operation == CilOperation.LoadArgument && instruction.Operand == new CilOperand.Index(1));
        Assert.IsType<EntryGuardedBody>(Selector().Select(bodyInstance));
        Assert.IsType<SelfGuardedActivation>(Selector().Select(method));
    }

    [Theory]
    [InlineData(CilOperation.Call, true)]
    [InlineData(CilOperation.CallVirtual, false)]
    public void ADirectAwaitCallsTheCalleesBodyUnlessContextsMayChange(CilOperation operation, bool isStatic)
    {
        var (callee, calleeBody) = Pair(0x06000003, isStatic, []);
        var caller = Instance(new MethodDefinitionModel(new(Assembly, 0x06000004), new(Assembly, 0x02000002), "Caller", true,
            new MethodSignatureModel(Identity("System.Threading.Tasks.Task"), []), 1) { IsRuntimeAsync = true });
        ImmutableArray<CilInstruction> instructions = isStatic
            ?
            [
                new(0, 1, operation, new CilOperand.MethodInstance(callee)),
                new(1, 2, CilOperation.Call, new CilOperand.MethodInstance(AwaitTask())),
                new(2, 3, CilOperation.Return, new CilOperand.None()),
            ]
            :
            [
                new(0, 1, CilOperation.LoadNull, new CilOperand.None()),
                new(1, 2, operation, new CilOperand.MethodInstance(callee)),
                new(2, 3, CilOperation.Call, new CilOperand.MethodInstance(AwaitTask())),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ];
        var source = new CilMethodBody(caller.Definition, 2, [], instructions) { MethodInstance = caller };
        var repository = new Repository(callee.Definition, calleeBody);

        var lowered = Builder(repository).Build(caller, source).Instructions;

        var query = lowered.Single(instruction => Names([instruction]) is ["ContextsMayChange"]);
        var atBody = Assert.Single(lowered, instruction => Calls(instruction, calleeBody.Key));
        var atEntry = Assert.Single(lowered, instruction => Calls(instruction, callee.Definition.Key));
        // The body's call keeps the call's opcode (a callvirt still tests its receiver) and takes Direct mode; the entry's
        // keeps its request.
        Assert.Equal(operation, atBody.Operation);
        Assert.Equal(operation, atEntry.Operation);
        Assert.Equal(new CilOperand.ConstantI4(1), lowered[lowered.IndexOf(atBody) - 1].Operand);
        Assert.Equal("CallDirect", Names([lowered[lowered.IndexOf(atEntry) - 1]]).Single());
        Assert.True(query.Offset < atBody.Offset && atBody.Offset < atEntry.Offset);
    }

    // A runtime-async method and its separate body, as ManagedAssembly registers them.
    private (MethodInstanceModel Method, MethodDefinitionModel Body) Pair(int token, bool isStatic,
        ImmutableArray<CliTypeIdentity> parameters, bool isVirtual = false)
    {
        var signature = new MethodSignatureModel(Identity("System.Threading.Tasks.Task"), parameters);
        var bodyKey = new EntityKey(Assembly, RuntimeAsyncBodies.Token(token));
        var definition = new MethodDefinitionModel(new(Assembly, token), new(Assembly, 0x02000002), "M" + token, isStatic,
            signature, 1)
        {
            IsRuntimeAsync = true,
            RuntimeAsyncBody = bodyKey,
            IsVirtual = isVirtual,
        };
        var body = new MethodDefinitionModel(bodyKey, new(Assembly, 0x02000002), "M" + token, isStatic,
            RuntimeAsyncBodies.Signature(signature), 0)
        {
            RuntimeAsyncBodyOf = definition.Key,
        };
        Assert.True(RuntimeAsyncBodies.IsBody(bodyKey.MetadataToken));
        Assert.Equal(token, RuntimeAsyncBodies.Method(bodyKey.MetadataToken));
        return (Instance(definition), body);
    }

    private static MethodInstanceModel Instance(MethodDefinitionModel definition) => new(definition, Owner, [], definition.Signature);

    private RuntimeAsyncBodyBuilder Builder(IMethodRepository methods) => new(() => _members, () => _frames, _definitions,
        new CilBodyLayout(), new Symbols(), methods, _fields, new MetadataStackTypeResolver(_definitions),
        new AnyRuntimeAsyncMethodMaySuspend(), Selector(), new RuntimeAsyncDirectSiteEmitter(() => _members, methods, new SeparateRuntimeAsyncBodies()));

    private RuntimeAsyncActivationGuardSelector Selector() => new(
        new SelfGuardedActivation(() => _members, new RuntimeAsyncCatchEmitter(() => _members)), new EntryGuardedBody());

    private static bool Calls(CilInstruction instruction, EntityKey method) =>
        instruction.Operation is CilOperation.Call or CilOperation.CallVirtual &&
        instruction.Operand is CilOperand.MethodInstance { Value.Definition.Key: var key } && key == method;

    private static bool Inside(CilExceptionRegion region, int offset) =>
        offset >= region.TryOffset && offset < region.TryOffset + region.TryLength;

    private static string[] Names(IEnumerable<CilInstruction> instructions) => [.. instructions
        .Where(instruction => instruction.Operation == CilOperation.Call)
        .Select(instruction => instruction.Operand is CilOperand.MethodInstance call ? call.Value.Definition.Name : null)
        .OfType<string>()];

    // AsyncHelpers.Await(Task).
    private MethodInstanceModel AwaitTask()
    {
        var helpers = _types.FindType("System.Runtime.CompilerServices.AsyncHelpers");
        var await = helpers.Methods.Select(_methods.GetMethod).Single(method => method.Name == "Await" &&
            method.GenericArity == 0 && method.Signature.ParameterSignatureTypes is [{ FullName: "System.Threading.Tasks.Task" }]);
        return new(await, CliTypeIdentity.FromDefinition(helpers), [], await.Signature);
    }

    // Task.get_CompletedTask.
    private MethodInstanceModel CompletedTask()
    {
        var task = _types.FindType("System.Threading.Tasks.Task");
        var getter = task.Methods.Select(_methods.GetMethod).Single(method => method.Name == "get_CompletedTask");
        return new(getter, CliTypeIdentity.FromDefinition(task), [], getter.Signature);
    }

    private CliTypeIdentity Identity(string fullName) => CliTypeIdentity.FromDefinition(_types.FindType(fullName));

    private sealed class Repository(MethodDefinitionModel method, MethodDefinitionModel body) : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => key == body.Key ? body : key == method.Key ? method :
            throw new KeyNotFoundException(key.ToString());
    }

    private sealed class Symbols : ISymbolFormatter
    {
        public string Format(EntityKey key) => "Shapes";

        public string Format(MethodDefinitionModel method) => "Shapes::" + method.Name;
    }
}
