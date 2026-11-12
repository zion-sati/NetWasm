using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Layout;

// The table builder joins every registered type's descriptors with its pending facts, asks the
// encoder for the table, and places the table as one segment at the cursor.
public sealed class RuntimeTypeTableBuilderTests
{
    private static readonly AssemblyIdentity Assembly = new("TypeTable");
    private static readonly CliTypeIdentity Outer = CliTypeIdentity.Named(Assembly, "Fixtures", "Outer`1", false);
    private static readonly CliTypeIdentity Inner = CliTypeIdentity.Named(Assembly, "Fixtures", "Inner", false);
    private static readonly CliTypeIdentity Parameter =
        CliTypeIdentity.ScopedGenericParameter(Outer.CanonicalName, method: false, index: 0);

    [Fact]
    public void RejectsMissingDependencies()
    {
        var program = Program(declaring: false);
        var target = WasmTargetLayout.For(WasmTarget.Wasm32);
        var state = new ManagedStaticDataBuildState();
        var identities = new Identities();
        var assemblies = new Assemblies();
        var interfaces = new Interfaces();
        var encoder = new CapturingEncoder();

        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeTableBuilder(null!, target, state, identities, assemblies, interfaces, encoder));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeTableBuilder(program, null!, state, identities, assemblies, interfaces, encoder));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeTableBuilder(program, target, null!, identities, assemblies, interfaces, encoder));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeTableBuilder(program, target, state, null!, assemblies, interfaces, encoder));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeTableBuilder(program, target, state, identities, null!, interfaces, encoder));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeTableBuilder(program, target, state, identities, assemblies, null!, encoder));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeTableBuilder(program, target, state, identities, assemblies, interfaces, null!));
    }

    // Each registered type takes its descriptor's layout and its facts; a nested type its enclosing
    // definition and a generic type parameter its owner, when the program asks for declaring types.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncodesEveryRegisteredTypeAndPlacesTheTableAtTheCursor(bool declaring)
    {
        var state = State();
        var encoder = new CapturingEncoder();
        var cursor = state.Cursor;

        Builder(Program(declaring), state, encoder).Build();

        var request = Assert.IsType<RuntimeTypeTableRequest>(encoder.Request);
        Assert.Equal([1, 2, 3], request.Entries.Select(entry => entry.TypeId));
        Assert.Equal(declaring, request.IncludeRelatedTypes);
        Assert.False(request.IncludeGenericArguments);
        Assert.Equal(declaring ? [0, 1, 1] : [0, 0, 0], request.Entries.Select(entry => entry.RelatedTypeId));
        Assert.Equal(16, request.Entries[1].ObjectSize);
        Assert.Equal([4], request.Entries[1].ReferenceOffsets);
        Assert.Equal(cursor, state.TypeTableAddress);
        Assert.Equal(cursor + 3, state.Cursor);
        Assert.Equal(encoder.Bytes, Assert.Single(state.Segments).Data);
    }

    // With Type.Assembly reachable, each type's assembly takes its index in type ID order, a generic
    // parameter its owner's, and the entry point's assembly is the entry assembly; the order is published
    // for the type-symbols sidecar.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssemblyIndexesFollowTypeIdOrder(bool assemblies)
    {
        var state = State();
        var encoder = new CapturingEncoder();

        Builder(Program(declaring: false) with { RequiresAssemblies = assemblies }, state, encoder).Build();

        var request = Assert.IsType<RuntimeTypeTableRequest>(encoder.Request);
        Assert.Equal(assemblies, request.IncludeAssemblies);
        Assert.Equal(assemblies ? [1, 2, 1] : [0, 0, 0], request.Entries.Select(entry => entry.AssemblyIndex));
        Assert.Equal(assemblies ? 1 : 0, request.EntryAssemblyIndex);
        Assert.Equal(assemblies ? [Assembly, Other] : [], state.RuntimeAssemblies);
    }

    // An array takes its element in the related slot when GetElementType is reachable, beside the nested
    // types' declaring types; the section is there when either is.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ElementTypesShareTheRelatedSlotWithDeclaringTypes(bool declaring, bool elements)
    {
        var state = State();
        var array = CliTypeIdentity.SzArray(Inner);
        state.MetadataTypeDescriptors.Add(new(array, 4));
        state.PendingTypeFacts.Add(new(array, null, 4));
        var encoder = new CapturingEncoder();

        Builder(Program(declaring) with { RequiresElementTypes = elements }, state, encoder).Build();

        var request = Assert.IsType<RuntimeTypeTableRequest>(encoder.Request);
        Assert.Equal(declaring || elements, request.IncludeRelatedTypes);
        Assert.Equal([0, declaring ? 1 : 0, declaring ? 1 : 0, elements ? 2 : 0],
            request.Entries.Select(entry => entry.RelatedTypeId));
    }

    // A generic parameter is based on ValueType under the struct constraint and on Object otherwise,
    // as .NET reports them, and takes its owner's attributes into its flags.
    [Theory]
    [InlineData(0, 4)]
    [InlineData(0x18, 5)]
    public void AGenericParameterIsBasedOnObjectOrValueType(int attributes, int expectedBase)
    {
        var state = State();
        state.PendingTypeFacts[0] = state.PendingTypeFacts[0] with
        {
            Definition = state.PendingTypeFacts[0].Definition! with { GenericParameterAttributes = [attributes] },
        };
        state.MetadataTypeDescriptors.Add(new(CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference, false), 4));
        state.PendingTypeFacts.Add(new(CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference, false),
            Definition("Object"), 4));
        var valueType = CliTypeIdentity.Named(Assembly, "System", "ValueType", false);
        state.MetadataTypeDescriptors.Add(new(valueType, 5));
        state.PendingTypeFacts.Add(new(valueType, Definition("ValueType"), 5));
        var encoder = new CapturingEncoder();

        Builder(Program(declaring: false) with { RequiresTypeFacts = true }, state, encoder).Build();

        var parameter = Assert.Single(encoder.Request!.Entries, entry => entry.TypeId == 3);
        Assert.Equal(expectedBase, parameter.BaseTypeId);
        Assert.Equal(attributes, (int)(parameter.Flags >> RuntimeTypeFactsEncoding.ArrayRankShift & 0x3f));
    }

    // A definition's own TypeAttributes, which its instantiations share; an array is public, sealed and
    // serializable, and a parameter or pointer public, as .NET 11 reports them; none without the demand.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypesTakeTheirAttributesWhenAsked(bool attributes)
    {
        var state = State();
        state.PendingTypeFacts[0] = state.PendingTypeFacts[0] with
        {
            Definition = state.PendingTypeFacts[0].Definition! with { Attributes = 0x100001 },
        };
        state.PendingTypeFacts[1] = state.PendingTypeFacts[1] with
        {
            Definition = state.PendingTypeFacts[1].Definition! with { Attributes = 0x100002 },
        };
        var array = CliTypeIdentity.SzArray(Inner);
        var pointer = CliTypeIdentity.UnmanagedPointer(Inner);
        state.MetadataTypeDescriptors.Add(new(array, 4));
        state.PendingTypeFacts.Add(new(array, null, 4));
        state.MetadataTypeDescriptors.Add(new(pointer, 5));
        state.PendingTypeFacts.Add(new(pointer, null, 5));
        var encoder = new CapturingEncoder();

        Builder(Program(declaring: false) with { RequiresTypeAttributes = attributes }, state, encoder).Build();

        var request = encoder.Request!;
        Assert.Equal(attributes, request.IncludeTypeAttributes);
        Assert.Equal(attributes ? [0x100001, 0x100002, 0x1, 0x2101, 0x1] : [0, 0, 0, 0, 0],
            request.Entries.Select(entry => entry.TypeAttributes));
    }

    // A type implements the registered assignable interfaces it declares, and a vector the generic
    // collection interfaces of its own element; others (reached through variance) are not; none without
    // the demand.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypesTakeTheInterfacesTheyImplementWhenAsked(bool interfaces)
    {
        var state = new ManagedStaticDataBuildState();
        var array = CliTypeIdentity.SzArray(Inner);
        var list = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System.Collections.Generic", "IList`1", false), [Inner]);
        var objects = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System.Collections.Generic", "IEnumerable`1", false), [Outer]);
        state.TypeDescriptors.Add(new(new EntityKey(Assembly, 3), 2, 0, 16, [], null) { AssignableTypeIds = [4, 5] });
        state.ConstructedTypeDescriptors.Add(new(array, 3, 0, 16, [], null) { AssignableTypeIds = [6, 7] });
        foreach (var (identity, id) in new[] { (Shape, 4), (Other2, 5), (list, 6), (objects, 7) })
        {
            state.MetadataTypeDescriptors.Add(new(identity, id));
            state.PendingTypeFacts.Add(new(identity, null, id));
        }
        state.PendingTypeFacts.Add(new(Inner, Definition("Inner"), 2));
        state.PendingTypeFacts.Add(new(array, null, 3));
        var encoder = new CapturingEncoder();

        Builder(Program(declaring: false) with { RequiresInterfaces = interfaces }, state, encoder).Build();

        var request = encoder.Request!;
        Assert.Equal(interfaces, request.IncludeInterfaces);
        Assert.Equal(interfaces ? [4] : [], Assert.Single(request.Entries, entry => entry.TypeId == 2).ImplementedInterfaceTypeIds);
        Assert.Equal(interfaces ? [6] : [], Assert.Single(request.Entries, entry => entry.TypeId == 3).ImplementedInterfaceTypeIds);
    }

    [Fact]
    public void RejectsAnElementThatIsNotRegistered()
    {
        var state = State();
        var array = CliTypeIdentity.SzArray(CliTypeIdentity.Named(Assembly, "Fixtures", "Lost", false));
        state.MetadataTypeDescriptors.Add(new(array, 4));
        state.PendingTypeFacts.Add(new(array, null, 4));

        AssertRuntimeContract(() => Builder(Program(declaring: false) with { RequiresElementTypes = true }, state,
            new CapturingEncoder()).Build());
    }

    [Fact]
    public void RejectsAGenericParameterWhoseOwnerIsNotRegistered()
    {
        var state = new ManagedStaticDataBuildState();
        var parameter = CliTypeIdentity.ScopedGenericParameter("Unregistered", method: false, index: 0);
        state.MetadataTypeDescriptors.Add(new(parameter, 1));
        state.PendingTypeFacts.Add(new(parameter, null, 1));

        AssertRuntimeContract(() => Builder(Program(declaring: false) with { RequiresAssemblies = true }, state,
            new CapturingEncoder()).Build());
    }

    [Fact]
    public void RejectsATypeWhoseFactsHaveNoDescriptor()
    {
        var state = State();
        state.PendingTypeFacts.Add(new(CliTypeIdentity.Named(Assembly, "Fixtures", "Lost", false), null, 4));

        AssertRuntimeContract(() => Builder(Program(declaring: false), state, new CapturingEncoder()).Build());
    }

    [Fact]
    public void RejectsAValueLayoutForAnUnregisteredType()
    {
        var state = State();
        state.ValueTypeDescriptors.Add(new(CliTypeIdentity.Named(Assembly, "Fixtures", "Lost", true), 4, 4, 4, []));

        AssertRuntimeContract(() => Builder(Program(declaring: false), state, new CapturingEncoder()).Build());
    }

    [Fact]
    public void RejectsATypeIdRegisteredTwice()
    {
        var state = State();
        state.MetadataTypeDescriptors.Add(new(Inner, 2));

        AssertRuntimeContract(() => Builder(Program(declaring: false), state, new CapturingEncoder()).Build());
    }

    [Fact]
    public void RejectsADeclaringTypeThatIsNotRegistered()
    {
        var state = new ManagedStaticDataBuildState();
        state.TypeDescriptors.Add(new(new EntityKey(Assembly, 3), 2, 0, 16, [], null));
        state.PendingTypeFacts.Add(new(Inner, Definition("Inner") with { DeclaringType = new EntityKey(Assembly, 2) }, 2));

        AssertRuntimeContract(() => Builder(Program(declaring: true), state, new CapturingEncoder()).Build());
    }

    private static ManagedStaticDataBuildState State()
    {
        var state = new ManagedStaticDataBuildState();
        state.TypeDescriptors.Add(new(new EntityKey(Assembly, 2), 1, 0, 8, [], null));
        state.TypeDescriptors.Add(new(new EntityKey(Assembly, 3), 2, 0, 16, [4], null));
        state.MetadataTypeDescriptors.Add(new(Parameter, 3));
        state.PendingTypeFacts.Add(new(Outer, Definition("Outer`1") with { GenericArity = 1 }, 1));
        state.PendingTypeFacts.Add(new(Inner, Definition("Inner") with { DeclaringType = new EntityKey(Assembly, 2) }, 2));
        state.PendingTypeFacts.Add(new(Parameter, null, 3));
        return state;
    }

    private static RuntimeTypeTableBuilder Builder(
        ReachableProgram program, ManagedStaticDataBuildState state, CapturingEncoder encoder) =>
        new(program, WasmTargetLayout.For(WasmTarget.Wasm32), state, new Identities(), new Assemblies(), new Interfaces(), encoder);

    private static ReachableProgram Program(bool declaring)
    {
        var method = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000001),
            new EntityKey(Assembly, 2),
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            0);
        return new ReachableProgram(method, [], [], [], [], [], [], [], [], [], [])
        {
            RequiresDeclaringTypes = declaring,
        };
    }

    private static TypeDefinitionModel Definition(string name) =>
        new(new EntityKey(Assembly, name == "Inner" ? 3 : 2), "Fixtures", name, false, [], []);

    private static void AssertRuntimeContract(Action build) =>
        Assert.Equal(DiagnosticCode.RuntimeContract, Assert.Throws<CompilerException>(build).Diagnostic.Code);

    private static readonly AssemblyIdentity Other = new("Other");

    // Inner declares IShape; a vector of Inner declares the generic collection interfaces of Inner.
    private static readonly CliTypeIdentity Shape = CliTypeIdentity.Named(Assembly, "Fixtures", "IShape", false);
    private static readonly CliTypeIdentity Other2 = CliTypeIdentity.Named(Assembly, "Fixtures", "IOther", false);

    private sealed class Interfaces : IImplementedInterfaceResolver
    {
        public ImmutableArray<CliTypeIdentity> GetInterfaces(CliTypeIdentity type) => type.Equals(Inner) ? [Shape] : [];
    }

    // Inner lives in another assembly.
    private sealed class Assemblies : IRuntimeTypeAssemblyResolver
    {
        public AssemblyIdentity Resolve(CliTypeIdentity type) => type.Equals(Inner) ? Other : Assembly;
    }

    private sealed class Identities : ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey type) => type.MetadataToken == 2
            ? Outer
            : CliTypeIdentity.Named(Assembly, "Fixtures", "Unregistered", false);
    }

    private sealed class CapturingEncoder : IRuntimeTypeTableEncoder
    {
        internal ImmutableArray<byte> Bytes { get; } = [1, 2, 3];

        internal RuntimeTypeTableRequest? Request { get; private set; }

        public ImmutableArray<byte> Encode(RuntimeTypeTableRequest request)
        {
            Request = request;
            return Bytes;
        }
    }
}
