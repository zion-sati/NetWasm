// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// The x86 and Arm instruction sets' IsSupported queries are false on NetWasm whatever the program, so the rule folds
// them at every level, before the constant-branch rules remove what they guard; other queries keep their calls.
public sealed class HardwareIntrinsicSupportCallRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("CoreLib");
    private static readonly MethodDefinitionModel Caller = new(
        new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Round", IsStatic: true,
        MethodSignatureModel.Create(CliValueKind.Void), 1);

    [Theory]
    [InlineData("System.Runtime.Intrinsics.X86.Fma")]
    [InlineData("System.Runtime.Intrinsics.X86.Fma+X64")]
    [InlineData("System.Runtime.Intrinsics.X86.Avx2")]
    [InlineData("System.Runtime.Intrinsics.Arm.AdvSimd+Arm64")]
    [InlineData("System.Runtime.Intrinsics.Arm.ArmBase")]
    public void AnotherInstructionSetsSupportIsFalse(string type)
    {
        var rewritten = Rewrite(type, "get_IsSupported", isStatic: true);

        Assert.Equal(CilOperation.LoadInt32, rewritten.Instructions[0].Operation);
        Assert.Equal(0, Assert.IsType<CilOperand.ConstantI4>(rewritten.Instructions[0].Operand).Value);
    }

    // WebAssembly's own instruction sets and the vector types' acceleration follow the compilation's settings.
    [Theory]
    [InlineData("System.Runtime.Intrinsics.Wasm.PackedSimd", "get_IsSupported", true)]
    [InlineData("System.Runtime.Intrinsics.Vector128", "get_IsHardwareAccelerated", true)]
    [InlineData("System.Runtime.Intrinsics.X86.Fma", "MultiplyAdd", true)]
    [InlineData("System.Runtime.Intrinsics.X86.Fma", "get_IsSupported", false)]
    [InlineData("Fixtures.System.Runtime.Intrinsics.X86.Fma", "get_IsSupported", true)]
    public void OtherCallsAreLeftAlone(string type, string name, bool isStatic)
    {
        var body = Body(CilOperation.Call);

        Assert.Same(body, Rewriter(type, name, isStatic).Rewrite(body));
    }

    [Fact]
    public void AnUnresolvedOrVirtualCallIsLeftAlone()
    {
        var unresolved = Body(CilOperation.Call);
        Assert.Same(unresolved, new HardwareIntrinsicSupportCallRewriter(new FixedMethodResolver(null), new Symbols(""))
            .Rewrite(unresolved));
        var virtualCall = Body(CilOperation.CallVirtual);
        Assert.Same(virtualCall, Rewriter("System.Runtime.Intrinsics.X86.Fma", "get_IsSupported", true).Rewrite(virtualCall));
        Assert.Throws<ArgumentNullException>(() => Rewriter("Fixtures.Caller", "Run", true).Rewrite(null!));
    }

    private static CilMethodBody Rewrite(string type, string name, bool isStatic) =>
        Rewriter(type, name, isStatic).Rewrite(Body(CilOperation.Call));

    private static HardwareIntrinsicSupportCallRewriter Rewriter(string type, string name, bool isStatic)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), name,
            isStatic, signature, 0);
        var target = new MethodInstanceModel(definition,
            CliTypeIdentity.Named(Assembly, "Fixtures", "Query", isValueType: false), [], signature);
        return new(new FixedMethodResolver(target), new Symbols(type));
    }

    private static CilMethodBody Body(CilOperation callOperation) => new(
        Caller,
        1,
        [],
        [
            new(0, 1, callOperation, new CilOperand.None()),
            new(1, 2, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(3)),
            new(2, 3, CilOperation.Return, new CilOperand.None()),
            new(3, 4, CilOperation.Return, new CilOperand.None()),
        ]);

    private sealed class FixedMethodResolver(MethodInstanceModel? method) : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => method;
    }

    private sealed class Symbols(string type) : ISymbolFormatter
    {
        public string Format(EntityKey key) => key.MetadataToken == 0x02000002 ? type : "Fixtures.Caller";

        public string Format(MethodDefinitionModel method) => $"{Format(method.DeclaringType)}::{method.Name}";
    }
}
