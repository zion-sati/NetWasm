using System.Reflection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class GenericArgumentSpecialConstraintValidatorTests
{
    [Theory]
    [InlineData("value", 4, false)]
    [InlineData("class", 4, true)]
    [InlineData("array", 4, true)]
    [InlineData("value", 8, true)]
    [InlineData("nullable", 8, false)]
    [InlineData("class", 8, false)]
    [InlineData("array", 8, false)]
    [InlineData("value", 16, true)]
    [InlineData("nullable", 16, true)]
    [InlineData("public-constructor", 16, true)]
    [InlineData("private-constructor", 16, false)]
    [InlineData("static-constructor", 16, false)]
    [InlineData("argument-constructor", 16, false)]
    [InlineData("wrong-name", 16, false)]
    [InlineData("abstract", 16, false)]
    [InlineData("class", 16, false)]
    [InlineData("array", 16, false)]
    [InlineData("value", 24, true)]
    [InlineData("nullable", 24, false)]
    [InlineData("open-type", 24, true)]
    [InlineData("open-method", 24, true)]
    [InlineData("class", 0, true)]
    [InlineData("ref-like", 0, false)]
    [InlineData("ref-like", 8, false)]
    [InlineData("ref-like", 32, true)]
    [InlineData("ref-like", 40, true)]
    [InlineData("ref-like", 36, false)]
    [InlineData("value", 32, true)]
    [InlineData("array", 32, true)]
    public void ChecksSpecialConstraintsAgainstTheActualArgument(string shape, int flags, bool accepted)
    {
        var owner = new EntityKey(new("Types"), 0x02000001);
        var constructor = new MethodDefinitionModel(new(owner.Assembly, 0x06000001), owner,
            shape == "wrong-name" ? "Other" : ".ctor", shape == "static-constructor",
            new(CliValueKind.Void, shape == "argument-constructor" ? [CliValueKind.I4] : []), 0)
        {
            IsPublic = shape != "private-constructor",
        };
        var hasConstructor = shape.EndsWith("constructor", StringComparison.Ordinal) || shape is "abstract" or "wrong-name";
        var valueType = shape is "value" or "nullable" or "ref-like";
        var definition = new TypeDefinitionModel(owner, "Fixture", "Argument", valueType, [],
            hasConstructor ? [constructor.Key] : [])
        { IsAbstract = shape == "abstract", IsByRefLike = shape == "ref-like" };
        var argument = shape switch
        {
            "open-type" => CliTypeIdentity.GenericParameter(false, 0),
            "open-method" => CliTypeIdentity.GenericParameter(true, 0),
            "array" => CliTypeIdentity.SzArray(CliTypeIdentity.Primitive("i4", CliValueKind.I4)),
            "nullable" => CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(owner.Assembly, "System", "Nullable`1", true),
                [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]),
            _ => CliTypeIdentity.FromDefinition(definition),
        };
        var definitions = new Definitions(definition);
        var validator = Assert.IsAssignableFrom<IGenericArgumentSpecialConstraintValidator>(
            new GenericArgumentSpecialConstraintValidator(definitions, new Methods(constructor), new NullableTypeResolver()));
        Assert.Throws<ArgumentNullException>(() => new GenericArgumentSpecialConstraintValidator(null!, new Methods(constructor), new NullableTypeResolver()));
        Assert.Throws<ArgumentNullException>(() => new GenericArgumentSpecialConstraintValidator(definitions, null!, new NullableTypeResolver()));
        Assert.Throws<ArgumentNullException>(() => new GenericArgumentSpecialConstraintValidator(definitions, new Methods(constructor), null!));
        var request = new GenericArgumentSpecialConstraintRequest(argument, (GenericParameterAttributes)flags, "Caller.Read", 11);
        if (accepted)
            Assert.Null(Record.Exception(() => validator.Validate(request)));
        else
        {
            var error = Assert.Throws<CompilerException>(() => validator.Validate(request));
            Assert.Equal(DiagnosticCode.AssemblyResolution, error.Diagnostic.Code);
            Assert.Equal(request.Caller, error.Diagnostic.Method);
            Assert.Equal(request.IlOffset, error.Diagnostic.IlOffset);
        }
        Assert.Equal(shape is "open-type" or "open-method" or "array" ? 0 : 1, definitions.Calls);
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
    }

    private sealed class Definitions(TypeDefinitionModel definition) : ITypeDefinitionResolver
    {
        public int Calls { get; private set; }
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            Calls++;
            return definition;
        }
    }

    private sealed class Methods(MethodDefinitionModel method) : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key)
        {
            Assert.Equal(method.Key, key);
            return method;
        }
    }
}
