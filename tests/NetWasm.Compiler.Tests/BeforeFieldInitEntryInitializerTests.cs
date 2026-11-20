// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// A method that reads a BeforeFieldInit type's static fields on every path
// checks the type once, at its entry: one check covers every field of the type,
// and a body that already checks at its entry stays as it is. A static method or
// constructor of a precise type checks its own type at its entry whatever its
// paths, since invoking it initializes that type.
public sealed class BeforeFieldInitEntryInitializerTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly EntityKey OwnerKey = new(Assembly, 0x02000001);
    private static readonly EntityKey InitializerKey = new(Assembly, 0x06000001);
    private static readonly EntityKey FirstKey = new(Assembly, 0x04000001);
    private static readonly EntityKey SecondKey = new(Assembly, 0x04000002);

    [Fact]
    public void ChecksATypeOnceForAllOfItsFields()
    {
        var body = Body(
            I(0, CilOperation.LoadStaticField, new CilOperand.Entity(FirstKey)), I(1, CilOperation.Pop),
            I(2, CilOperation.LoadStaticField, new CilOperand.Entity(SecondKey)), I(3, CilOperation.Pop),
            I(4, CilOperation.Return));

        var rewritten = Initializer().Rewrite(body);

        Assert.Equal(
            [CilOperation.LoadStaticFieldAddress, CilOperation.Pop, CilOperation.LoadStaticField, CilOperation.Pop,
                CilOperation.LoadStaticField, CilOperation.Pop, CilOperation.Return],
            rewritten.Instructions.Select(instruction => instruction.Operation));
        Assert.Equal(new CilOperand.Entity(FirstKey), rewritten.Instructions[0].Operand);
        Assert.Same(rewritten, Initializer().Rewrite(rewritten));
    }

    // if (input) return First; return 0; in a method of the fields' precise type, or of another type, or as an instance
    // method of their type.
    [Theory]
    [InlineData("own static", true)]
    [InlineData("own constructor", true)]
    [InlineData("own instance", false)]
    [InlineData("other type", false)]
    public void APreciseTypesOwnStaticMethodChecksItAtEntry(string method, bool checkedAtEntry)
    {
        var declaring = method == "other type" ? new EntityKey(Assembly, 0x02000002) : OwnerKey;
        var body = new CilMethodBody(
            new MethodDefinitionModel(new(Assembly, 0x06000003), declaring,
                method == "own constructor" ? ".ctor" : "Run", method is "own static" or "other type",
                MethodSignatureModel.Create(CliValueKind.I4, [CliValueKind.I4]), 1),
            1, [],
            [
                I(0, CilOperation.LoadArgument, new CilOperand.Index(0)), I(1, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(4)),
                I(2, CilOperation.LoadStaticField, new CilOperand.Entity(FirstKey)), I(3, CilOperation.Return),
                I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(5, CilOperation.Return),
            ]);

        var rewritten = Initializer(beforeFieldInit: false).Rewrite(body);

        Assert.Equal(checkedAtEntry, rewritten.Instructions[0].Operation == CilOperation.LoadStaticFieldAddress);
        if (checkedAtEntry)
        {
            Assert.Equal(CilOperation.Pop, rewritten.Instructions[1].Operation);
            Assert.Equal(6, ((CilOperand.BranchTarget)rewritten.Instructions[3].Operand).Offset);
        }
        else
        {
            Assert.Same(body, rewritten);
        }
    }

    private static BeforeFieldInitEntryInitializer Initializer(bool beforeFieldInit = true)
    {
        var program = new Program(beforeFieldInit);
        return new(new ControlFlowGraphBuilderFactory().Create(), new ControlFlowDominanceAnalyzer(),
            program, program, program, enabled: true);
    }

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    private static CilMethodBody Body(params CilInstruction[] instructions) =>
        new(new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void), 1), 1, [], [.. instructions]);

    private sealed class Program(bool beforeFieldInit = true) : ITypeRepository, IMethodRepository, IFieldRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) =>
            new(OwnerKey, "Fixture", "Cache", false, [FirstKey, SecondKey], [InitializerKey]) { IsBeforeFieldInit = beforeFieldInit };

        public MethodDefinitionModel GetMethod(EntityKey key) =>
            new(InitializerKey, OwnerKey, ".cctor", true, MethodSignatureModel.Create(CliValueKind.Void), 1);

        public FieldDefinitionModel GetField(EntityKey key) =>
            new(key, OwnerKey, key == FirstKey ? "First" : "Second", CliValueKind.I4, true)
            {
                SignatureType = CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            };
    }
}
