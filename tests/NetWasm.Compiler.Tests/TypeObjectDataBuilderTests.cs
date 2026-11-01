using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

// A typeof constant's Type object is laid out as System.Type's, so a program
// that materializes one without that layout reports the broken runtime contract
// instead of writing objects of no type.
public sealed class TypeObjectDataBuilderTests
{
    private static readonly EntityKey TypeKey = new(ValidationTestData.Assembly, 0x02000040);

    [Fact]
    public void ATypeConstantWithoutSystemTypesLayoutReportsTheRuntimeContract()
    {
        var materialized = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var body = ValidationTestData.ManagedMethodBody(
        [
            new CilInstruction(0, 1, CilOperation.MaterializeType, new CilOperand.TypeIdentity(materialized)),
            new CilInstruction(1, 2, CilOperation.Pop, new CilOperand.None()),
            new CilInstruction(2, 3, CilOperation.Return, new CilOperand.None()),
        ]);
        var program = ValidationTestData.Program() with
        {
            Methods = ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(body.Method.Definition.Key, body),
        };
        var target = WasmTargetLayout.For(WasmTarget.Wasm32);
        var types = new ManagedTypeLayouts([], [], [], [], new(), target, 0, 0, 0, 0);
        var collaborators = new Collaborators();

        var exception = Assert.Throws<CompilerException>(() => new TypeObjectDataBuilder(
            collaborators, program, types, collaborators, collaborators, target, new ManagedStaticDataBuildState()).Build());

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
        Assert.Contains("'System.Type'", exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    private sealed class Collaborators : ITypeFinder, IObjectLayoutResolver, ITypeOperandResolver
    {
        public TypeDefinitionModel FindType(string fullName) => new(TypeKey, "System", "Type", false, [], []);

        public ObjectLayout Resolve(EntityKey type) => throw new InvalidOperationException();

        public ObjectLayout Resolve(CliTypeIdentity type) => throw new InvalidOperationException();

        public CliTypeIdentity Resolve(CilInstruction instruction, MethodInstanceModel? methodInstance) =>
            ((CilOperand.TypeIdentity)instruction.Operand).Value;
    }
}
