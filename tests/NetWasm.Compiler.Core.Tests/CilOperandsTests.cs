namespace NetWasm.Compiler.Core.Tests;

// A field access or call names its definition by key or by instance, and a
// typed instruction its type; no other operand names one.
public sealed class CilOperandsTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly EntityKey Owner = new(Assembly, 0x02000001);
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);

    [Fact]
    public void AFieldIsNamedByKeyOrInstance()
    {
        var definition = new FieldDefinitionModel(new(Assembly, 0x04000001), Owner, "value", Int32, false);
        var owner = CliTypeIdentity.Named(Assembly, "Fixture", "Owner", isValueType: false);

        Assert.Equal(definition.Key, CilOperands.FieldKey(new CilOperand.Entity(definition.Key)));
        Assert.Equal(definition.Key, CilOperands.FieldKey(new CilOperand.FieldInstance(new(definition, owner, Int32))));
        Assert.Throws<InvalidOperationException>(() => CilOperands.FieldKey(new CilOperand.Index(0)));
    }

    [Fact]
    public void AMethodIsNamedByKeyOrInstance()
    {
        var signature = MethodSignatureModel.Create(Int32);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), Owner, "Run", true, signature, 1);
        var owner = CliTypeIdentity.Named(Assembly, "Fixture", "Owner", isValueType: false);

        Assert.Equal(definition.Key, CilOperands.MethodKey(new CilOperand.Entity(definition.Key)));
        Assert.Equal(definition.Key, CilOperands.MethodKey(new CilOperand.MethodInstance(new(definition, owner, [], signature))));
        Assert.Throws<InvalidOperationException>(() => CilOperands.MethodKey(new CilOperand.None()));
    }

    [Fact]
    public void ATypedInstructionNamesItsType()
    {
        Assert.Equal(Int32, CilOperands.Type(new CilOperand.TypeIdentity(Int32)));
        Assert.Throws<InvalidOperationException>(() => CilOperands.Type(new CilOperand.None()));
    }
}
