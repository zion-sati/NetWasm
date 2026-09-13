using NetWasm.Wit.Bindings.FlatSlots;
using NetWasm.Compiler.Core;

namespace NetWasm.Wit.Bindings.Tests.FlatSlots;

public sealed class WitFlatSlotCoercionFormatterTests
{
    private const string Expression = "value";

    public static TheoryData<CliValueKind, CliValueKind, string> SupportedCases => new()
    {
        { CliValueKind.I4, CliValueKind.I4, "unchecked((int)(value))" },
        { CliValueKind.I8, CliValueKind.I8, "unchecked((long)(value))" },
        { CliValueKind.F4, CliValueKind.F4, "unchecked((float)(value))" },
        { CliValueKind.F8, CliValueKind.F8, "unchecked((double)(value))" },
        { CliValueKind.ManagedAddress, CliValueKind.ManagedAddress, "unchecked((nuint)(value))" },
        { CliValueKind.F4, CliValueKind.I4, "CanonicalAbi.SingleToInt32Bits((value))" },
        { CliValueKind.I4, CliValueKind.F4, "CanonicalAbi.Int32BitsToSingle((value))" },
        { CliValueKind.I4, CliValueKind.I8, "unchecked((long)(uint)(value))" },
        { CliValueKind.F4, CliValueKind.I8, "unchecked((long)(uint)CanonicalAbi.SingleToInt32Bits((value)))" },
        { CliValueKind.I8, CliValueKind.I4, "unchecked((int)(value))" },
        { CliValueKind.I8, CliValueKind.F4, "CanonicalAbi.Int32BitsToSingle(unchecked((int)(value)))" },
        { CliValueKind.F8, CliValueKind.I8, "CanonicalAbi.DoubleToInt64Bits((value))" },
        { CliValueKind.I8, CliValueKind.F8, "CanonicalAbi.Int64BitsToDouble((value))" },
        { CliValueKind.F4, CliValueKind.F8, "(double)(value)" },
        { CliValueKind.F8, CliValueKind.F4, "(float)(value)" },
        { CliValueKind.ManagedAddress, CliValueKind.I8, "unchecked((long)(value))" },
        { CliValueKind.I8, CliValueKind.ManagedAddress, "unchecked((nuint)(value))" },
        { CliValueKind.I4, CliValueKind.ManagedAddress, "unchecked((nuint)(uint)(value))" },
        { CliValueKind.ManagedAddress, CliValueKind.I4, "unchecked((int)(value))" },
        { CliValueKind.F4, CliValueKind.ManagedAddress, "unchecked((nuint)(uint)CanonicalAbi.SingleToInt32Bits((value)))" },
        { CliValueKind.ManagedAddress, CliValueKind.F4, "CanonicalAbi.Int32BitsToSingle(unchecked((int)(value)))" },
    };

    [Theory]
    [MemberData(nameof(SupportedCases))]
    public void FormatsEverySupportedFlatSlotPair(
        CliValueKind source,
        CliValueKind destination,
        string expected)
    {
        var actual = Format(new WitFlatSlotCoercionFormatter(),
            new WitFlatSlotCoercionRequest(
            Expression,
            source,
            destination));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SameKindCoercionTargetTypesNestedExpressions()
    {
        var actual = Format(new WitFlatSlotCoercionFormatter(),
            new WitFlatSlotCoercionRequest(
            "tag switch { 0 => 0L, _ => value }",
            CliValueKind.I8,
            CliValueKind.I8));

        Assert.Equal(
            "unchecked((long)(tag switch { 0 => 0L, _ => value }))",
            actual);
    }

    [Fact]
    public void RejectsMissingRequest()
    {
        Assert.Throws<ArgumentNullException>(() => Format(
            new WitFlatSlotCoercionFormatter(),
            null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsBlankExpression(string expression)
    {
        Assert.Throws<ArgumentException>(() => Format(
            new WitFlatSlotCoercionFormatter(),
            new WitFlatSlotCoercionRequest(
                expression!,
                CliValueKind.I4,
                CliValueKind.I4)));
    }

    [Fact]
    public void RejectsNullExpression()
    {
        Assert.Throws<ArgumentNullException>(() => Format(
            new WitFlatSlotCoercionFormatter(),
            new WitFlatSlotCoercionRequest(
                null!,
                CliValueKind.I4,
                CliValueKind.I4)));
    }

    [Theory]
    [InlineData(CliValueKind.I4, CliValueKind.F8)]
    [InlineData(CliValueKind.Void, CliValueKind.Void)]
    public void RejectsUnsupportedFlatSlotPairs(
        CliValueKind source,
        CliValueKind destination)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Format(
            new WitFlatSlotCoercionFormatter(),
            new WitFlatSlotCoercionRequest(Expression, source, destination)));
    }

    private static string Format(
        object formatter,
        WitFlatSlotCoercionRequest request) =>
        ((IWitFlatSlotCoercionFormatter)formatter).Format(request);
}
