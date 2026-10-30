using System.Collections.Immutable;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Core.Tests.Types;

public sealed class StaticFieldInitializationClassifierTests
{
    private static readonly EntityKey Type = new(new AssemblyIdentity("Initialization"), 0x02000001);
    private static readonly EntityKey OtherType = new(Type.Assembly, 0x02000002);
    private static readonly MethodDefinitionModel Method = new(
        new EntityKey(Type.Assembly, 0x06000001), Type, "Read", false,
        MethodSignatureModel.Create(CliValueKind.Void), 1);
    private static readonly FieldDefinitionModel Field = new(
        new EntityKey(Type.Assembly, 0x04000001), Type, "s_value", CliValueKind.I4, true);
    private static readonly FieldDefinitionModel OtherField = new(
        new EntityKey(Type.Assembly, 0x04000002), OtherType, "s_other", CliValueKind.I4, true);
    private static readonly CliTypeIdentity Owner = CliTypeIdentity.Named(Type.Assembly, "", "Owner", false);
    private static readonly CliTypeIdentity Generic = CliTypeIdentity.Named(Type.Assembly, "", "Owner`1", false);
    private static readonly CliTypeIdentity OfInt = CliTypeIdentity.GenericInstantiation(
        Generic, [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
    private static readonly CliTypeIdentity OfLong = CliTypeIdentity.GenericInstantiation(
        Generic, [CliTypeIdentity.Primitive("i8", CliValueKind.I8)]);

    // Only an instance method of a precise reference type reading its own
    // type's field finds that type initialized.
    [Theory]
    [InlineData(false, false, false, false, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    public void TreatsOnlyAPreciseInstanceOwnerAsInitialized(
        bool isStatic, bool isValueType, bool beforeFieldInit, bool otherType, bool expected)
    {
        var classifier = Classifier(isValueType, beforeFieldInit);
        var access = Access(0, CilOperation.LoadStaticField, otherType ? OtherField : Field);

        Assert.Equal(expected, classifier.IsInitializedFor(
            Method with { IsStatic = isStatic }, null, [access], access));
    }

    [Fact]
    public void RequiresTheAccessorsOwnInstantiation()
    {
        var classifier = Classifier(isValueType: false, beforeFieldInit: false);
        var ofInt = Access(0, CilOperation.LoadStaticField, Field, OfInt);
        var ofLong = Access(0, CilOperation.LoadStaticField, Field, OfLong);
        var definition = Access(0, CilOperation.LoadStaticField, Field);

        Assert.True(classifier.IsInitializedFor(Method, OfInt, [ofInt], ofInt));
        Assert.False(classifier.IsInitializedFor(Method, OfInt, [ofLong], ofLong));
        Assert.False(classifier.IsInitializedFor(Method, null, [ofInt], ofInt));
        Assert.False(classifier.IsInitializedFor(Method, OfInt, [definition], definition));
    }

    // An address taken and discarded at the body's entry checks its type
    // before anything else runs, so the body's other accesses to that type,
    // and to no other, find it initialized.
    [Fact]
    public void TreatsAnEntryAccessAsInitializingItsTypeForTheRestOfTheBody()
    {
        var classifier = Classifier(isValueType: false, beforeFieldInit: true);
        var entry = Access(0, CilOperation.LoadStaticFieldAddress, Field);
        var read = Access(2, CilOperation.LoadStaticField, Field);
        var other = Access(3, CilOperation.StoreStaticField, OtherField);
        var accessor = Method with { IsStatic = true };
        ImmutableArray<CilInstruction> body = [entry, new(1, 2, CilOperation.Pop, new CilOperand.None()), read, other];

        Assert.True(classifier.IsInitializedFor(accessor, null, body, read));
        Assert.False(classifier.IsInitializedFor(accessor, null, body, entry));
        Assert.False(classifier.IsInitializedFor(accessor, null, body, other));
        // A field address the body keeps is an ordinary access, not an entry check.
        Assert.False(classifier.IsInitializedFor(accessor, null, [entry, read], read));
    }

    [Fact]
    public void RejectsMissingDependenciesAndInput()
    {
        var types = new Types(new(Type, "", "Owner", false, [], []));
        Assert.Throws<ArgumentNullException>(() => new StaticFieldInitializationClassifier(null!, new Fields()));
        Assert.Throws<ArgumentNullException>(() => new StaticFieldInitializationClassifier(types, null!));
        var classifier = Classifier(isValueType: false, beforeFieldInit: false);
        var access = Access(0, CilOperation.LoadStaticField, Field);
        Assert.Throws<ArgumentNullException>(() => classifier.IsInitializedFor(null!, null, [access], access));
        Assert.Throws<ArgumentNullException>(() => classifier.IsInitializedFor(Method, null, [access], null!));
    }

    private static CilInstruction Access(
        int offset, CilOperation operation, FieldDefinitionModel field, CliTypeIdentity? declaringType = null) =>
        new(offset, offset + 1, operation, new CilOperand.FieldInstance(new FieldInstanceModel(
            field, declaringType ?? Owner, CliTypeIdentity.Primitive("i4", CliValueKind.I4))));

    private static StaticFieldInitializationClassifier Classifier(bool isValueType, bool beforeFieldInit) =>
        new(new Types(new(Type, "", "Owner", isValueType, [], [])
        {
            IsBeforeFieldInit = beforeFieldInit,
        }), new Fields());

    private sealed class Types(TypeDefinitionModel definition) : ITypeRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key)
        {
            Assert.Equal(Type, key);
            return definition;
        }
    }

    private sealed class Fields : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) =>
            key == Field.Key ? Field : key == OtherField.Key ? OtherField : throw new KeyNotFoundException();
    }
}
