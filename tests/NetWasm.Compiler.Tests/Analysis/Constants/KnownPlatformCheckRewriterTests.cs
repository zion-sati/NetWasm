using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

// OperatingSystem.IsOSPlatform("Linux") folds to false unless a branch, switch, handler or filter enters the check;
// another type's IsOSPlatform is left alone.
public sealed class KnownPlatformCheckRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("PlatformFixture");

    [Theory]
    [InlineData("folds", true)]
    [InlineData("switch target", false)]
    [InlineData("filter target", false)]
    [InlineData("other type", false)]
    public void ALiteralPlatformCheckFoldsWhereNothingEntersIt(string scenario, bool folds)
    {
        var owner = scenario == "other type"
            ? CliTypeIdentity.Named(Assembly, "Fixture", "Checks", false)
            : CliTypeIdentity.Named(Assembly, "System", "OperatingSystem", false);
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.ManagedReference);
        var check = new MethodInstanceModel(new(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "IsOSPlatform", true,
            signature, 0), owner, [], signature);
        var body = new CilMethodBody(check.Definition, 2, [],
        [
            new(0, 1, CilOperation.Switch, new CilOperand.SwitchTargets([scenario == "switch target" ? 2 : 4])),
            new(1, 2, CilOperation.LoadString, new CilOperand.UserString("Linux")),
            new(2, 3, CilOperation.Call, new CilOperand.MethodInstance(check)),
            new(3, 4, CilOperation.Pop, new CilOperand.None()),
            new(4, 5, CilOperation.Return, new CilOperand.None()),
        ])
        {
            ExceptionRegions =
            [
                new(CilExceptionRegionKind.Filter, 0, 1, 3, 1, null, scenario == "filter target" ? 2 : 4),
                new(CilExceptionRegionKind.Finally, 0, 1, 3, 1, null, null),
            ],
        };

        var rewritten = new KnownPlatformCheckRewriter(new OperandMethods()).Rewrite(body);

        if (!folds)
        {
            Assert.Same(body, rewritten);
            return;
        }
        Assert.Equal([CilOperation.Switch, CilOperation.Nop, CilOperation.LoadInt32, CilOperation.Pop, CilOperation.Return],
            rewritten.Instructions.Select(instruction => instruction.Operation));
    }

    private sealed class OperandMethods : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => (instruction.Operand as CilOperand.MethodInstance)?.Value;
    }
}
