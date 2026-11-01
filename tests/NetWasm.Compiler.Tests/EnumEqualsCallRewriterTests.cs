using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// a.Equals(b) on a type parameter that is an enum boxes b, then calls
// Object.Equals constrained to the enum; the rule compares the values instead.
// It takes the Equals of Object, ValueType or Enum and an enum only.
public sealed class EnumEqualsCallRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Shade = CliTypeIdentity.Named(Assembly, "Fixture", "Shade", true);

    [Theory]
    [InlineData("System.Object", true)]
    [InlineData("System.ValueType", true)]
    [InlineData("System.Enum", true)]
    [InlineData("Fixture.Other", false)]
    public void ComparesAnEnumThroughTheEqualsOfItsBaseTypes(string declaringType, bool rewritten)
    {
        var body = Body(Shade);

        var result = Rule(declaringType, isEnum: true).Rewrite(body);

        Assert.Equal(rewritten, !ReferenceEquals(body, result));
    }

    // A struct that is no enum and a primitive keep their call.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LeavesOtherValueTypesAlone(bool primitive)
    {
        var body = Body(primitive ? CliTypeIdentity.Primitive("i4", CliValueKind.I4) : Shade);

        Assert.Same(body, Rule("System.Object", isEnum: false).Rewrite(body));
    }

    private static EnumEqualsCallRewriter Rule(string declaringType, bool isEnum)
    {
        var program = new Program(declaringType, isEnum);
        return new(program, program, program, enabled: true);
    }

    private static CilMethodBody Body(CliTypeIdentity type)
    {
        var equals = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Equals", false,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.ManagedReference), 1)
        { IsVirtual = true };
        var call = new CilOperand.MethodInstance(new(equals,
            CliTypeIdentity.Named(Assembly, "System", "Object", false), [], equals.Signature));
        return new(
            new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), "Run", true,
                MethodSignatureModel.Create(CliValueKind.I4, type.StackKind, type.StackKind), 1),
            2, [],
            [
                I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
                I(1, CilOperation.LoadArgument, new CilOperand.Index(1)),
                I(2, CilOperation.Box, new CilOperand.TypeIdentity(type)),
                I(3, CilOperation.Constrained, new CilOperand.TypeIdentity(type)),
                I(4, CilOperation.CallVirtual, call),
                I(5, CilOperation.Return),
            ])
        { LocalSignatureTypes = [] };
    }

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    private sealed class Program(string declaringType, bool isEnum) :
        ITypeDefinitionResolver, ICalledMethodResolver, ISymbolFormatter
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new(new(Assembly, 0x02000003), "Fixture", "Shade", true, [], []) { IsEnum = isEnum };

        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            (instruction.Operand as CilOperand.MethodInstance)?.Value;

        public string Format(EntityKey key) => declaringType;

        public string Format(MethodDefinitionModel method) => method.Name;
    }
}
