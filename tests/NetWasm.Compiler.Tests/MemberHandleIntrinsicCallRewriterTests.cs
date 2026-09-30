using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class MemberHandleIntrinsicCallRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("CoreLib");
    private static readonly EntityKey CallerType = Key(0x02000001);
    private static readonly EntityKey MethodBaseType = Key(0x02000002);
    private static readonly EntityKey FieldInfoType = Key(0x02000003);
    private static readonly EntityKey TypeType = Key(0x02000004);

    [Theory]
    [InlineData(0, CilOperation.MaterializeMethod, 1)]
    [InlineData(1, CilOperation.MaterializeMethod, 2)]
    [InlineData(2, CilOperation.MaterializeField, 1)]
    [InlineData(3, CilOperation.MaterializeField, 2)]
    public void RewritesClosedHandleFactoriesWithTheirExactArity(
        int offset,
        CilOperation expectedOperation,
        int expectedArity)
    {
        var caller = Method(Key(0x06000001), CallerType, "Run", 0);
        var body = new CilMethodBody(
            caller,
            2,
            [],
            [new CilInstruction(offset, offset + 1, CilOperation.Call, new CilOperand.None())]);
        var rewriter = new IntrinsicCallRewriter(
            new HandleMethodResolver(),
            new HandleSymbols());

        var rewritten = rewriter.Rewrite(body);

        var instruction = Assert.Single(rewritten.Instructions);
        Assert.Equal(expectedOperation, instruction.Operation);
        Assert.Equal(
            expectedArity,
            Assert.IsType<CilOperand.Index>(instruction.Operand).Value);
    }

    [Fact]
    public void RewritesCompilerOwnedSemanticTypeMaterialization()
    {
        var caller = Method(Key(0x06000001), CallerType, "Run", 0);
        var body = new CilMethodBody(
            caller,
            1,
            [],
            [new CilInstruction(4, 5, CilOperation.Call, new CilOperand.None())]);
        var rewriter = new IntrinsicCallRewriter(
            new HandleMethodResolver(),
            new HandleSymbols());

        var rewritten = rewriter.Rewrite(body);

        var instruction = Assert.Single(rewritten.Instructions);
        Assert.Equal(CilOperation.MaterializeType, instruction.Operation);
        Assert.IsType<CilOperand.None>(instruction.Operand);
    }

    [Fact]
    public void RewritesOnlyTheExactCompilerOwnedTypeFactsLookup()
    {
        var caller = Method(Key(0x06000001), CallerType, "Run", 0);
        var body = new CilMethodBody(
            caller,
            1,
            [],
            [new CilInstruction(5, 6, CilOperation.Call, new CilOperand.None())]);
        var rewriter = new IntrinsicCallRewriter(
            new HandleMethodResolver(),
            new HandleSymbols());

        var instruction = Assert.Single(rewriter.Rewrite(body).Instructions);

        Assert.Equal(CilOperation.GetTypeFacts, instruction.Operation);
        Assert.IsType<CilOperand.None>(instruction.Operand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DoesNotRewriteMalformedTypeFactsLookupSignatures(int malformedPart)
    {
        var caller = Method(Key(0x06000001), CallerType, "Run", 0);
        var signature = malformedPart switch
        {
            1 => MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4),
            2 => MethodSignatureModel.Create(CliValueKind.NativeInt),
            3 => MethodSignatureModel.Create(CliValueKind.NativeInt, CliValueKind.I8),
            _ => MethodSignatureModel.Create(CliValueKind.NativeInt, CliValueKind.I4),
        };
        var definition = new MethodDefinitionModel(
            Key(0x06000015),
            TypeType,
            "InternalGetFacts",
            IsStatic: malformedPart != 0,
            signature,
            1);
        var method = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(
                Assembly,
                "System",
                "Type",
                isValueType: false),
            [],
            signature);
        var body = new CilMethodBody(
            caller,
            1,
            [],
            [new CilInstruction(0, 1, CilOperation.Call, new CilOperand.None())]);
        var rewriter = new IntrinsicCallRewriter(
            new FixedMethodResolver(method),
            new HandleSymbols());

        Assert.Same(body, rewriter.Rewrite(body));
    }

    private static EntityKey Key(int token) => new(Assembly, token);

    private static MethodDefinitionModel Method(
        EntityKey key,
        EntityKey declaringType,
        string name,
        int parameterCount) => new(
            key,
            declaringType,
            name,
            IsStatic: true,
            MethodSignatureModel.Create(
                CliValueKind.ManagedReference,
                Enumerable.Repeat(CliValueKind.ValueType, parameterCount).ToArray()),
            1);

    private sealed class HandleMethodResolver : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction)
        {
            var method = instruction.Offset switch
            {
                0 => Method(Key(0x06000010), MethodBaseType, "GetMethodFromHandle", 1),
                1 => Method(Key(0x06000011), MethodBaseType, "GetMethodFromHandle", 2),
                2 => Method(Key(0x06000012), FieldInfoType, "GetFieldFromHandle", 1),
                3 => Method(Key(0x06000013), FieldInfoType, "GetFieldFromHandle", 2),
                4 => Method(Key(0x06000014), TypeType, "GetTypeFromSemanticId", 1),
                5 => new MethodDefinitionModel(
                    Key(0x06000015),
                    TypeType,
                    "InternalGetFacts",
                    IsStatic: true,
                    MethodSignatureModel.Create(
                        CliValueKind.NativeInt,
                        CliValueKind.I4),
                    1),
                _ => throw new InvalidOperationException(),
            };
            return new(
                method,
                CliTypeIdentity.Named(
                    Assembly,
                    "System.Reflection",
                    method.DeclaringType == MethodBaseType ? "MethodBase" : "FieldInfo",
                    isValueType: false),
                [],
                method.Signature);
        }
    }

    private sealed class FixedMethodResolver(MethodInstanceModel method) :
        ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => method;
    }

    private sealed class HandleSymbols : ISymbolFormatter
    {
        public string Format(EntityKey key) => key == MethodBaseType
            ? "System.Reflection.MethodBase"
            : key == FieldInfoType
                ? "System.Reflection.FieldInfo"
                : key == TypeType
                    ? "System.Type"
                : "Fixtures.Caller";

        public string Format(MethodDefinitionModel method) =>
            $"{Format(method.DeclaringType)}::{method.Name}";
    }
}
