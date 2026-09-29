using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

/// <summary>Selects optional enum payloads independently of representation.</summary>
internal sealed class EnumMetadataRequirementClassifier : IEnumMetadataRequirementClassifier
{
    private const EnumMetadataPayload NameValues =
        EnumMetadataPayload.Names | EnumMetadataPayload.Values;
    private const EnumMetadataPayload ManagedAlgorithms =
        NameValues | EnumMetadataPayload.RuntimeDescriptor;

    private static readonly ImmutableDictionary<RuntimeIntrinsic, EnumMetadataPayload> Requirements =
        new Dictionary<RuntimeIntrinsic, EnumMetadataPayload>
        {
            [RuntimeIntrinsic.EnumEquals] = EnumMetadataPayload.None,
            [RuntimeIntrinsic.EnumGetHashCode] = EnumMetadataPayload.None,
            [RuntimeIntrinsic.EnumCompareTo] = EnumMetadataPayload.None,
            [RuntimeIntrinsic.EnumGetTypeCode] = EnumMetadataPayload.None,
            [RuntimeIntrinsic.EnumHasFlag] = EnumMetadataPayload.None,
            [RuntimeIntrinsic.EnumGetUnderlyingType] = EnumMetadataPayload.None,
            [RuntimeIntrinsic.EnumToObject] = EnumMetadataPayload.None,
            [RuntimeIntrinsic.EnumConvert] = EnumMetadataPayload.None,
            [RuntimeIntrinsic.EnumGetValues] = EnumMetadataPayload.Values,
            [RuntimeIntrinsic.EnumGetNames] = EnumMetadataPayload.Names,
            [RuntimeIntrinsic.EnumGetName] = NameValues,
            [RuntimeIntrinsic.EnumIsDefined] = EnumMetadataPayload.Values,
            [RuntimeIntrinsic.EnumGetMetadata] = ManagedAlgorithms,
            [RuntimeIntrinsic.EnumToString] = ManagedAlgorithms,
            [RuntimeIntrinsic.EnumFormat] = ManagedAlgorithms,
        }.ToImmutableDictionary();

    public EnumMetadataPayload Classify(RuntimeIntrinsic intrinsic, bool hasClosedEnum, string methodName)
    {
        if (!Requirements.TryGetValue(intrinsic, out var payload))
        {
            throw new ArgumentOutOfRangeException(nameof(intrinsic), intrinsic,
                "An enum metadata request requires an enum intrinsic.");
        }

        // The existing conversion intrinsic handles both numeric conversions
        // and ToType's string branch. Only the latter consumes formatting data.
        if (intrinsic == RuntimeIntrinsic.EnumConvert &&
            methodName is "InternalToType" or "System.IConvertible.ToType")
        {
            return ManagedAlgorithms;
        }

        return intrinsic == RuntimeIntrinsic.EnumIsDefined && !hasClosedEnum
            ? NameValues
            : payload;
    }
}
