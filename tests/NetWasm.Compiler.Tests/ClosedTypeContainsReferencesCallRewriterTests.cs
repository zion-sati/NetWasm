using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// IsReferenceOrContainsReferences<T>() of a closed T folds to a constant: a
// modified type answers as the type it modifies, and a value type that lists
// itself among its fields, which no loadable type does, stops the fold.
public sealed class ClosedTypeContainsReferencesCallRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly EntityKey HelpersKey = new(Assembly, 0x02000001);
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Cycle = CliTypeIdentity.Named(Assembly, "Fixture", "Cycle", isValueType: true);

    [Fact]
    public void AModifiedTypeAnswersAsTheTypeItModifies()
    {
        var volatileInt = CliTypeIdentity.Modified(Int32, CliTypeIdentity.Named(Assembly, "System.Runtime.CompilerServices",
            "IsVolatile", isValueType: false), isRequired: true);

        var rewritten = Rewriter().Rewrite(Body(volatileInt));

        Assert.Equal(new CilOperand.ConstantI4(0), rewritten.Instructions[0].Operand);
    }

    [Fact]
    public void AValueTypeContainingItselfStopsTheFold() =>
        Assert.Contains("contains itself", Assert.Throws<InvalidOperationException>(() => Rewriter().Rewrite(Body(Cycle))).Message);

    private static ClosedTypeContainsReferencesCallRewriter Rewriter() =>
        new(new OperandCalledMethodResolver(), new Symbols(), new Types(), new Fields());

    private static CilMethodBody Body(CliTypeIdentity argument)
    {
        var signature = MethodSignatureModel.Create(CliTypeIdentity.Primitive("bool", CliValueKind.I4));
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), HelpersKey, "IsReferenceOrContainsReferences", true,
            signature, 1)
        { GenericArity = 1 };
        var helpers = CliTypeIdentity.Named(Assembly, "System.Runtime.CompilerServices", "RuntimeHelpers", isValueType: false);
        var call = new MethodInstanceModel(definition, helpers, [argument], signature);
        var caller = new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), "Run", true,
            MethodSignatureModel.Create(CliTypeIdentity.Primitive("bool", CliValueKind.I4)), 1);
        return new(caller, 1, [],
        [
            new(0, 1, CilOperation.Call, new CilOperand.MethodInstance(call)),
            new(1, 2, CilOperation.Return, new CilOperand.None()),
        ]);
    }

    private sealed class OperandCalledMethodResolver : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance method ? method.Value : null;
    }

    private sealed class Symbols : ISymbolFormatter
    {
        public string Format(EntityKey key) => key == HelpersKey ? "System.Runtime.CompilerServices.RuntimeHelpers" : key.ToString();

        public string Format(MethodDefinitionModel method) => method.Name;
    }

    private sealed class Types : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new(new(Assembly, 0x02000003), "Fixture", "Cycle", true, [new(Assembly, 0x04000001)], []);
    }

    private sealed class Fields : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) =>
            new(key, new(Assembly, 0x02000003), "self", Cycle, false);
    }
}
