using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class RunClassConstructorIntrinsicEmitterTests
{
    private static readonly AssemblyIdentity Fixture = new("class-constructors");

    [Fact]
    public void RunsTheGuardOfEachTypeWithACompiledInitializerOnce()
    {
        var program = new Program();
        var registrant = program.Type("Registrant", 31, initializer: 0x06000001);
        _ = program.Type("Plain", 32, initializer: null);
        var generic = program.Type("Generic`1", 0, initializer: 0x06000002, arity: 1);
        var closed = CliTypeIdentity.GenericInstantiation(generic.Identity, [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);
        program.Constructed(closed, generic.Definition, 33);
        _ = program.Type("Unplanned", 34, initializer: 0x06000003);
        var guards = ImmutableDictionary<string, StaticInitializerGuard>.Empty
            .Add(StaticInitializerGuard.KeyFor(new(Fixture, 0x06000001)),
                new StaticInitializerGuard(100, new(Fixture, 0x06000001), null) { FunctionIndex = OptionalFunctionIndex.At(7) })
            .Add($"{closed.CanonicalName}::0x06000002",
                new StaticInitializerGuard(104, null, $"{closed.CanonicalName}::0x06000002") { FunctionIndex = OptionalFunctionIndex.At(8) });
        var request = CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.RunClassConstructor, [CliValueKind.I4]);
        request = request with
        {
            Call = request.Call with
            {
                Instruction = request.Instruction with
                {
                    Target = request.Instruction.Target with
                    {
                        StaticInitializerGuards = Lookups.Of(guards),
                    },
                },
            },
        };
        var addresses = new RecordingAddresses();
        var emitter = Assert.IsAssignableFrom<IRuntimeIntrinsicEmitter>(
            new RunClassConstructorIntrinsicEmitter(program, program, program, program, program, addresses));
        var writer = new RecordingInstructionWriter();

        emitter.Emit(request, writer);

        var code = writer.ToInstructions();
        Assert.Equal([7u, 8u], code.Where(i => i.Opcode == WasmOpcodes.Call).Select(i => i.Operand.UnsignedValue));
        Assert.Equal([100, 104], addresses.Constants);
        var constants = code.Where(i => i.Opcode == WasmOpcodes.I32Constant).Select(i => i.Operand.SignedValue).ToArray();
        Assert.Equal([31, 2, 33, 2], constants);
        Assert.All(code.Where(i => i.Opcode == WasmOpcodes.LocalGet),
            get => Assert.Equal((uint)request.Local(0, CliValueKind.I4), get.Operand.UnsignedValue));
    }

    private sealed class Program :
        ITypeDescriptorSource, ITypeRepository, ITypeDefinitionResolver, IMethodRepository, ITypeLayoutProvider
    {
        private readonly Dictionary<EntityKey, TypeDefinitionModel> _types = [];
        private readonly Dictionary<string, TypeDefinitionModel> _identities = [];
        private readonly Dictionary<EntityKey, MethodDefinitionModel> _methods = [];
        private readonly Dictionary<string, int> _typeIds = [];
        private readonly List<TypeDescriptorLayout> _named = [];
        private readonly List<ConstructedTypeDescriptorLayout> _constructed = [];

        internal (TypeDefinitionModel Definition, CliTypeIdentity Identity) Type(
            string name, int typeId, int? initializer, int arity = 0)
        {
            var key = new EntityKey(Fixture, 0x02000001 + _types.Count);
            ImmutableArray<EntityKey> methods = initializer is { } token ? [new(Fixture, token)] : [];
            var definition = new TypeDefinitionModel(key, "Fixtures", name, false, [], methods) { GenericArity = arity };
            foreach (var method in methods)
                _methods.Add(method, new(method, key, ".cctor", true, MethodSignatureModel.Create(CliValueKind.Void), 0));
            var identity = CliTypeIdentity.FromDefinition(definition);
            _types.Add(key, definition);
            _identities.Add(identity.CanonicalName, definition);
            _typeIds.Add(identity.CanonicalName, typeId);
            _named.Add(new(key, typeId, 0, 16, [], null));
            return (definition, identity);
        }

        internal void Constructed(CliTypeIdentity type, TypeDefinitionModel definition, int typeId)
        {
            _identities.Add(type.CanonicalName, definition);
            _typeIds.Add(type.CanonicalName, typeId);
            _constructed.Add(new(type, typeId, 0, 16, [], null));
        }

        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => [.. _named];
        public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors => [.. _constructed];
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];
        // A type listed twice still runs its guard once.
        public ImmutableArray<MetadataTypeDescriptorLayout> MetadataTypeDescriptors =>
            [.. _named.Select(descriptor => new MetadataTypeDescriptorLayout(
                CliTypeIdentity.FromDefinition(_types[descriptor.Type]), descriptor.TypeId))];

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => _types[key];
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => _identities[identity.CanonicalName];
        public MethodDefinitionModel GetMethod(EntityKey key) => _methods[key];

        public ObjectLayout GetObjectLayout(CliTypeIdentity type) =>
            GetObjectLayout(type, out var layout) ? layout : throw new KeyNotFoundException(type.CanonicalName);
        public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout)
        {
            var found = _typeIds.TryGetValue(type.CanonicalName, out var typeId);
            layout = new(typeId, 16, []);
            return found;
        }
        public ObjectLayout GetObjectLayout(EntityKey type) => throw new NotSupportedException();
        public int StringTypeId => 13;
        public int TypeTypeId => 14;
    }

    private sealed class RecordingAddresses : IAddressInstructionEmitter
    {
        public List<int> Constants { get; } = [];
        public void Emit(IWasmInstructionWriter code, int constant) => Constants.Add(constant);
        public void Emit(IWasmInstructionWriter code, AddressOperation operation) => Assert.Fail(operation.ToString());
    }
}
