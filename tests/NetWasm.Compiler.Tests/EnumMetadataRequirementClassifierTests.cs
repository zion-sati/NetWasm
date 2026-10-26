using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class EnumMetadataRequirementClassifierTests
{
    public static IEnumerable<object[]> Requests()
    {
        foreach (var closed in new[] { false, true })
        {
            foreach (var intrinsic in new[]
                     {
                         RuntimeIntrinsic.EnumEquals, RuntimeIntrinsic.EnumGetHashCode,
                         RuntimeIntrinsic.EnumCompareTo, RuntimeIntrinsic.EnumGetTypeCode,
                         RuntimeIntrinsic.EnumHasFlag, RuntimeIntrinsic.EnumGetUnderlyingType,
                         RuntimeIntrinsic.EnumToObject, RuntimeIntrinsic.EnumConvert,
                     })
            {
                yield return [intrinsic, closed, EnumMetadataPayload.None];
            }

            yield return [RuntimeIntrinsic.EnumGetValues, closed, EnumMetadataPayload.Values];
            yield return [RuntimeIntrinsic.EnumGetNames, closed, EnumMetadataPayload.Names];
            yield return [RuntimeIntrinsic.EnumGetName, closed, NamesAndValues];
            yield return [RuntimeIntrinsic.EnumGetMetadata, closed, ManagedAlgorithms];
            yield return [RuntimeIntrinsic.EnumIsDefined, closed,
                closed ? EnumMetadataPayload.Values : NamesAndValues];
            yield return [RuntimeIntrinsic.EnumToString, closed, ManagedAlgorithms];
            yield return [RuntimeIntrinsic.EnumFormat, closed, ManagedAlgorithms];
        }
    }

    [Theory]
    [MemberData(nameof(Requests))]
    public void ClassifiesOnlyThePayloadRequiredByTheOperation(
        RuntimeIntrinsic intrinsic, bool closed, EnumMetadataPayload expected)
    {
        var classifier = As<IEnumMetadataRequirementClassifier>(new EnumMetadataRequirementClassifier());

        Assert.Equal(expected, classifier.Classify(intrinsic, closed, "InternalToInt32"));
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.StringLength)]
    [InlineData((RuntimeIntrinsic)(-1))]
    public void RejectsRequestsOutsideItsEnumContract(RuntimeIntrinsic intrinsic)
    {
        var classifier = As<IEnumMetadataRequirementClassifier>(new EnumMetadataRequirementClassifier());

        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            classifier.Classify(intrinsic, false, "NotAnEnumOperation"));

        Assert.Equal("intrinsic", error.ParamName);
    }

    private const EnumMetadataPayload NamesAndValues =
        EnumMetadataPayload.Names | EnumMetadataPayload.Values;
    private const EnumMetadataPayload ManagedAlgorithms =
        NamesAndValues | EnumMetadataPayload.RuntimeDescriptor;

    [Fact]
    public void FactoryRejectsMissingRequirementClassifier()
    {
        Assert.Throws<ArgumentNullException>(() => new ReachabilityImportClassifierFactory(
            null!, new NetWasm.Compiler.Core.NativeInterop.NativeDeclarationValidator()));
    }

    private static TContract As<TContract>(TContract actor) => actor;
}
