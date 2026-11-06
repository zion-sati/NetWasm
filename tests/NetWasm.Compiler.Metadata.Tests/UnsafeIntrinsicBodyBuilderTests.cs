using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

namespace NetWasm.Compiler.Metadata.Tests;

// Each intrinsic's body is upstream's UnsafeIntrinsics.EmitIL body, typed by
// the instance's type argument; other methods keep the body they declare.
public sealed class UnsafeIntrinsicBodyBuilderTests
{
    private static readonly AssemblyIdentity CoreLib = new("NetWasm.CoreLib");
    private static readonly CliTypeIdentity Element = CliTypeIdentity.Primitive("i4", CliValueKind.I4);

    [Theory]
    [InlineData("AsPointer", "ldarg 0, conv.u")]
    [InlineData("As", "ldarg 0")]
    [InlineData("AsRef", "ldarg 0")]
    [InlineData("Add", "ldarg 1, sizeof T, conv.i, mul, ldarg 0, add")]
    [InlineData("AddByteOffset", "ldarg 0, ldarg 1, add")]
    [InlineData("Copy", "ldarg 0, ldarg 1, ldobj T, stobj T")]
    [InlineData("CopyBlock", "ldarg 0, ldarg 1, ldarg 2, cpblk")]
    [InlineData("CopyBlockUnaligned", "ldarg 0, ldarg 1, ldarg 2, unaligned 1, cpblk")]
    [InlineData("InitBlock", "ldarg 0, ldarg 1, ldarg 2, initblk")]
    [InlineData("InitBlockUnaligned", "ldarg 0, ldarg 1, ldarg 2, unaligned 1, initblk")]
    [InlineData("Read", "ldarg 0, ldobj T")]
    [InlineData("Write", "ldarg 0, ldarg 1, stobj T")]
    [InlineData("ReadUnaligned", "ldarg 0, unaligned 1, ldobj T")]
    [InlineData("WriteUnaligned", "ldarg 0, ldarg 1, unaligned 1, stobj T")]
    [InlineData("AreSame", "ldarg 0, ldarg 1, ceq")]
    [InlineData("IsAddressGreaterThan", "ldarg 0, ldarg 1, cgt.un")]
    [InlineData("IsAddressLessThan", "ldarg 0, ldarg 1, clt.un")]
    [InlineData("ByteOffset", "ldarg 1, ldarg 0, sub")]
    [InlineData("NullRef", "ldc.i4 0, conv.u")]
    [InlineData("IsNullRef", "ldarg 0, ldc.i4 0, conv.u, ceq")]
    [InlineData("SkipInit", "")]
    [InlineData("Subtract", "ldarg 0, ldarg 1, sizeof T, conv.i, mul, sub")]
    [InlineData("SubtractByteOffset", "ldarg 0, ldarg 1, sub")]
    [InlineData("Unbox", "ldarg 0, unbox T")]
    public void BuildsUpstreamsBodyForEachIntrinsic(string name, string expected)
    {
        var method = Method("System.Runtime.CompilerServices", "Unsafe", name);

        var body = new UnsafeIntrinsicBodyBuilder().Build(method);

        Assert.NotNull(body);
        Assert.Same(method, body.MethodInstance);
        Assert.Empty(body.Locals);
        Assert.Equal(CilOperation.Return, body.Instructions[^1].Operation);
        Assert.Equal(expected, string.Join(", ", body.Instructions.SkipLast(1).Select(Format)));
        Assert.Equal(Enumerable.Range(0, body.Instructions.Length), body.Instructions.Select(static instruction => instruction.Offset));
    }

    // SizeOf, BitCast and the or-equal comparisons keep their C# bodies, as in
    // NativeAOT, and a method of another type is never an Unsafe intrinsic.
    [Theory]
    [InlineData("System.Runtime.CompilerServices", "Unsafe", "SizeOf")]
    [InlineData("System.Runtime.CompilerServices", "Unsafe", "BitCast")]
    [InlineData("System.Runtime.CompilerServices", "Unsafe", "IsAddressGreaterThanOrEqualTo")]
    [InlineData("System.Runtime.CompilerServices", "RuntimeHelpers", "Add")]
    [InlineData("System", "Unsafe", "CopyBlock")]
    public void LeavesOtherMethodsTheirOwnBodies(string @namespace, string type, string name) =>
        Assert.Null(new UnsafeIntrinsicBodyBuilder().Build(Method(@namespace, type, name)));

    private static MethodInstanceModel Method(string @namespace, string type, string name)
    {
        var signature = new MethodSignatureModel(CliValueKind.Void, []);
        var definition = new MethodDefinitionModel(new(CoreLib, 0x06000001), new(CoreLib, 0x02000001), name, true, signature, 1)
        {
            GenericArity = 1,
        };
        return new(definition, CliTypeIdentity.Named(CoreLib, @namespace, type, false), [Element], signature);
    }

    private static string Format(CilInstruction instruction) => (instruction.Operation, instruction.Operand) switch
    {
        (CilOperation.LoadArgument, CilOperand.Index { Value: var index }) => $"ldarg {index}",
        (CilOperation.Unaligned, CilOperand.Index { Value: var alignment }) => $"unaligned {alignment}",
        (CilOperation.LoadInt32, CilOperand.ConstantI4 { Value: var value }) => $"ldc.i4 {value}",
        (var operation, CilOperand.TypeIdentity { Value: var type }) when type.Equals(Element) => $"{Name(operation)} T",
        (var operation, CilOperand.None) => Name(operation),
        var other => other.ToString(),
    };

    private static string Name(CilOperation operation) => operation switch
    {
        CilOperation.ConvertNativeUInt => "conv.u",
        CilOperation.ConvertNativeInt => "conv.i",
        CilOperation.SizeOf => "sizeof",
        CilOperation.Multiply => "mul",
        CilOperation.Add => "add",
        CilOperation.Subtract => "sub",
        CilOperation.LoadObject => "ldobj",
        CilOperation.StoreObject => "stobj",
        CilOperation.CopyBlock => "cpblk",
        CilOperation.InitializeBlock => "initblk",
        CilOperation.CompareEqual => "ceq",
        CilOperation.CompareGreaterThanUnsigned => "cgt.un",
        CilOperation.CompareLessThanUnsigned => "clt.un",
        CilOperation.Unbox => "unbox",
        _ => operation.ToString(),
    };
}
