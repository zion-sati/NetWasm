using NetWasm.Compiler.Core;
using NetWasm.Compiler.Symbols;

namespace NetWasm.Compiler.Tests.Symbols;

// Names form as .NET forms them, with each generic argument's assembly display name in FullName.
public sealed class RuntimeTypeNameFormatterTests
{
    private static readonly AssemblyIdentity Assembly = new("type-facts");
    private static readonly Dictionary<CliTypeIdentity, string> NoParameterNames = [];

    [Fact]
    public void FormsConstructedArrayNamesWithTheArgumentsAssembly()
    {
        var box = Definition("Box`1", "T");
        var int32 = new TypeDefinitionModel(new EntityKey(Assembly, 2), "System", "Int32", IsValueType: true, [], []);
        var constructed = CliTypeIdentity.GenericInstantiation(Identity(box), [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);

        Assert.Equal(
            new RuntimeTypeSymbol(
                7,
                "Box`1[]",
                "Fixtures",
                "Fixtures.Box`1[[System.Int32, " + AssemblyName(CliTypeIdentity.Primitive("i4", CliValueKind.I4)) + "]][]",
                "Fixtures.Box`1[System.Int32][]"),
            RuntimeTypeNameFormatter.Format(
                7, CliTypeIdentity.SzArray(constructed), new TypeDefinitions(box, int32), NoParameterNames, AssemblyName));
    }

    [Fact]
    public void ShowsOpenGenericParameterNamesOnlyInTheDisplayText()
    {
        var symbol = Format(Identity(Definition("Pair`2", "TLeft", "TRight")), Definition("Pair`2", "TLeft", "TRight"));

        Assert.Equal("Pair`2", symbol.Name);
        Assert.Equal("Fixtures.Pair`2", symbol.FullName);
        Assert.Equal("Fixtures.Pair`2[TLeft,TRight]", symbol.DisplayName);
    }

    [Fact]
    public void RejectsInconsistentGenericParameterNames()
    {
        var definition = new TypeDefinitionModel(new EntityKey(Assembly, 1), "Fixtures", "Box`1", IsValueType: false, [], [])
        {
            GenericArity = 1,
        };

        var exception = Assert.Throws<CompilerException>(() => Format(Identity(definition), definition));
        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
    }

    [Fact]
    public void NamesANestedTypeByItsSimpleNameAndQualifiedFullName()
    {
        var nested = Definition("Outer+Inner");

        Assert.Equal(
            new RuntimeTypeSymbol(1, "Inner", "Fixtures", "Fixtures.Outer+Inner", "Fixtures.Outer+Inner"),
            Format(Identity(nested), nested));
    }

    [Fact]
    public void ReadsAnEmptyNamespaceAsNone()
    {
        var global = new TypeDefinitionModel(new EntityKey(Assembly, 1), "", "Global", IsValueType: false, [], []);

        Assert.Null(Format(CliTypeIdentity.Named(Assembly, "", "Global", isValueType: false), global).Namespace);
    }

    [Theory]
    [InlineData(1, "Sample[*]")]
    [InlineData(2, "Sample[,]")]
    public void DistinguishesBoundedArraysFromVectors(int rank, string expected)
    {
        var sample = Definition("Sample");

        Assert.Equal(expected, Format(CliTypeIdentity.Array(Identity(sample), rank), sample).Name);
    }

    [Theory]
    [InlineData(false, "Sample&")]
    [InlineData(true, "Sample*")]
    public void AppendsManagedAddressAndPointerSuffixes(bool unmanaged, string expected)
    {
        var sample = Definition("Sample");
        var element = Identity(sample);

        var symbol = Format(
            unmanaged ? CliTypeIdentity.UnmanagedPointer(element) : CliTypeIdentity.ManagedByReference(element), sample);

        Assert.Equal(expected, symbol.Name);
        Assert.Equal("Fixtures." + expected, symbol.FullName);
        Assert.Equal("Fixtures." + expected, symbol.DisplayName);
    }

    [Theory]
    [InlineData(false, "!0")]
    [InlineData(true, "!!0")]
    public void NamesAGenericParameterByItsPositionWithoutADeclaredName(bool method, string expected)
    {
        var symbol = Format(CliTypeIdentity.GenericParameter(method, 0));

        Assert.Equal(new RuntimeTypeSymbol(1, expected, null, null, expected), symbol);
    }

    [Fact]
    public void NamesAGenericParameterByItsDeclaredName()
    {
        var parameter = CliTypeIdentity.ScopedGenericParameter("Fixtures.Box`1", method: false, 0);

        var symbol = RuntimeTypeNameFormatter.Format(
            3, CliTypeIdentity.SzArray(parameter), new TypeDefinitions(), new Dictionary<CliTypeIdentity, string>
            {
                [parameter] = "T",
            },
            AssemblyName);

        Assert.Equal(new RuntimeTypeSymbol(3, "T[]", null, null, "T[]"), symbol);
    }

    [Fact]
    public void NamesAClosedArrayArgumentWithItsAssembly()
    {
        var box = Definition("Box`1", "T");
        var sample = Definition("Sample", token: 2);

        var symbol = Format(
            CliTypeIdentity.GenericInstantiation(Identity(box), [CliTypeIdentity.SzArray(Identity(sample))]), box, sample);

        Assert.Equal("Fixtures.Box`1[[Fixtures.Sample[], " + AssemblyName(CliTypeIdentity.SzArray(Identity(sample))) + "]]",
            symbol.FullName);
    }

    [Fact]
    public void GivesAnOpenConstructedTypeNoFullName()
    {
        var box = Definition("Box`1", "T");

        var symbol = Format(
            CliTypeIdentity.GenericInstantiation(Identity(box), [CliTypeIdentity.GenericParameter(method: false, 0)]), box);

        Assert.Null(symbol.FullName);
        Assert.Equal("Fixtures.Box`1[!0]", symbol.DisplayName);
    }

    private static RuntimeTypeSymbol Format(CliTypeIdentity identity, params TypeDefinitionModel[] definitions) =>
        RuntimeTypeNameFormatter.Format(1, identity, new TypeDefinitions(definitions), NoParameterNames, AssemblyName);

    private static string AssemblyName(CliTypeIdentity type) => "Assembly of " + type.CanonicalName;

    private static TypeDefinitionModel Definition(string name, params string[] genericParameters) =>
        Definition(name, 1, genericParameters);

    private static TypeDefinitionModel Definition(string name, int token, params string[] genericParameters) =>
        new(new EntityKey(Assembly, token), "Fixtures", name, IsValueType: false, [], [])
        {
            GenericArity = genericParameters.Length,
            GenericParameterNames = [.. genericParameters],
        };

    private static CliTypeIdentity Identity(TypeDefinitionModel definition) =>
        CliTypeIdentity.Named(Assembly, definition.Namespace, definition.Name, isValueType: false);

    private sealed class TypeDefinitions(params TypeDefinitionModel[] definitions) : ITypeDefinitionResolver
    {
        private readonly Dictionary<string, TypeDefinitionModel> _definitions =
            definitions.ToDictionary(definition => definition.FullName, StringComparer.Ordinal);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            _definitions[identity.FullName ?? identity.ElementType?.FullName ??
                (identity.CanonicalName == "primitive:i4" ? "System.Int32" : identity.CanonicalName)];
    }
}
