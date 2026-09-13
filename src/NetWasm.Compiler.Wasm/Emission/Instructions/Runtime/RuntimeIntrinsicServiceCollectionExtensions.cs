using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal static class RuntimeIntrinsicServiceCollectionExtensions
{
    public static IServiceCollection AddWasmRuntimeIntrinsicEmission(
        this IServiceCollection services)
    {
        services.AddSingleton<StringLengthIntrinsicEmitter>();
        services.AddSingleton<IArrayReceiverValidator, ArrayReceiverValidator>();
        services.AddSingleton<IArrayLengthEmitter, ArrayLengthEmitter>();
        services.AddSingleton<ArrayLengthIntrinsicEmitter>();
        services.AddSingleton<ArrayRankIntrinsicEmitter>();
        services.AddSingleton<ArrayGetLengthIntrinsicEmitter>();
        services.AddSingleton<ArrayCopyIntrinsicEmitter>();
        services.AddSingleton<ArrayClearIntrinsicEmitter>();
        services.AddSingleton<ArrayCloneIntrinsicEmitter>();
        services.AddSingleton<StringCharacterAtIntrinsicEmitter>();
        services.AddSingleton<StringSetCharacterUncheckedIntrinsicEmitter>();
        services.AddSingleton<JSObjectDisposeIntrinsicEmitter>();
        services.AddSingleton<JSSubscriptionDisposeIntrinsicEmitter>();
        services.AddSingleton<GcCollectIntrinsicEmitter>();
        services.AddSingleton<WeakHandleCreateIntrinsicEmitter>();
        services.AddSingleton<WeakHandleGetIntrinsicEmitter>();
        services.AddSingleton<WeakHandleSetIntrinsicEmitter>();
        services.AddSingleton<WeakHandleReleaseIntrinsicEmitter>();
        services.AddSingleton<GcHandleCreateIntrinsicEmitter>();
        services.AddSingleton<GcHandleGetIntrinsicEmitter>();
        services.AddSingleton<GcHandleSetIntrinsicEmitter>();
        services.AddSingleton<GcHandleReleaseIntrinsicEmitter>();
        services.AddSingleton<GcHandleAddressIntrinsicEmitter>();
        services.AddSingleton<GcGetMetricIntrinsicEmitter>();
        services.AddSingleton<GcMetricIsSupportedIntrinsicEmitter>();
        services.AddSingleton<GcWaitForPendingFinalizersIntrinsicEmitter>();
        services.AddSingleton<ObjectIdentityHashIntrinsicEmitter>();
        services.AddSingleton<IValueTypeEqualityFieldPlanner,
            ValueTypeEqualityFieldPlanner>();
        services.AddSingleton<IValueTypeEqualsEmitter, ValueTypeEqualsEmitter>();
        services.AddSingleton<IValueTypeHashCodeEmitter, ValueTypeHashCodeEmitter>();
        services.AddSingleton<ValueTypeEqualsIntrinsicEmitter>();
        services.AddSingleton<ValueTypeGetHashCodeIntrinsicEmitter>();
        services.AddSingleton<SuppressFinalizeIntrinsicEmitter>();
        services.AddSingleton<ReRegisterForFinalizeIntrinsicEmitter>();
        services.AddSingleton<ReportUnobservedTaskExceptionIntrinsicEmitter>();
        services.AddSingleton<IsReferenceOrContainsReferencesIntrinsicEmitter>();
        services.AddSingleton<GetArrayDataReferenceIntrinsicEmitter>();
        services.AddSingleton<NativeIntegerSizeIntrinsicEmitter>();
        services.AddSingleton<ComponentReallocateIntrinsicEmitter>();
        services.AddSingleton<ComponentFreeIntrinsicEmitter>();
        services.AddSingleton<ComponentResourceHandleCreateIntrinsicEmitter>();
        services.AddSingleton<ComponentResourceHandleGetIntrinsicEmitter>();
        services.AddSingleton<ComponentResourceHandleReleaseIntrinsicEmitter>();
        services.AddSingleton<UnsafeAddIntrinsicEmitter>();
        services.AddSingleton<UnsafeAsIntrinsicEmitter>();
        services.AddSingleton<UnsafeAsRefIntrinsicEmitter>();
        services.AddSingleton<UnsafeAsRefManagedIntrinsicEmitter>();
        services.AddSingleton<UnsafeNullRefIntrinsicEmitter>();
        services.AddSingleton<UnsafeIsAddressGreaterThanIntrinsicEmitter>();
        services.AddSingleton<UnsafeSizeOfIntrinsicEmitter>();
        services.AddSingleton<UnsafeByteOffsetIntrinsicEmitter>();
        services.AddSingleton<UnsafeAddByteOffsetIntrinsicEmitter>();
        services.AddSingleton<UnsafeObjectAsIntrinsicEmitter>();
        services.AddSingleton<UnsafeUnboxIntrinsicEmitter>();
        services.AddSingleton<INativeMemoryAllocationEmitter,
            NativeMemoryAllocationEmitter>();
        services.AddSingleton<INativeMemoryReleaseEmitter,
            NativeMemoryReleaseEmitter>();
        services.AddSingleton<NativeMemoryAllocIntrinsicEmitter>();
        services.AddSingleton<NativeMemoryReallocIntrinsicEmitter>();
        services.AddSingleton<NativeMemoryFreeIntrinsicEmitter>();
        services.AddSingleton<NativeMemoryAlignedAllocIntrinsicEmitter>();
        services.AddSingleton<NativeMemoryAlignedReallocIntrinsicEmitter>();
        services.AddSingleton<NativeMemoryAlignedFreeIntrinsicEmitter>();
        services.AddSingleton<SingleToInt32BitsIntrinsicEmitter>();
        services.AddSingleton<Int32BitsToSingleIntrinsicEmitter>();
        services.AddSingleton<DoubleToInt64BitsIntrinsicEmitter>();
        services.AddSingleton<Int64BitsToDoubleIntrinsicEmitter>();
        services.AddSingleton<FloatingAbsoluteIntrinsicEmitter>();
        services.AddSingleton<FloatingCeilingIntrinsicEmitter>();
        services.AddSingleton<FloatingFloorIntrinsicEmitter>();
        services.AddSingleton<FloatingTruncateIntrinsicEmitter>();
        services.AddSingleton<FloatingRoundIntrinsicEmitter>();
        services.AddSingleton<FloatingSquareRootIntrinsicEmitter>();

        services.AddSingleton<IEnumStorageResolver, EnumStorageResolver>();
        services.AddSingleton<IEnumNullCheckEmitter, EnumNullCheckEmitter>();
        services.AddSingleton<ITypeObjectIdReader, TypeObjectIdReader>();
        services.AddSingleton<IEnumTypeArgumentValidator, EnumTypeArgumentValidator>();
        services.AddSingleton<NullableGetUnderlyingTypeIntrinsicEmitter>();
        services.AddSingleton<IEnumValuePairEmitter, EnumValuePairEmitter>();
        services.AddSingleton<IEnumEqualsEmitter, EnumEqualsEmitter>();
        services.AddSingleton<IEnumHashCodeEmitter, EnumHashCodeEmitter>();
        services.AddSingleton<IEnumCompareToEmitter, EnumCompareToEmitter>();
        services.AddSingleton<EnumEqualsIntrinsicEmitter>();
        services.AddSingleton<EnumGetHashCodeIntrinsicEmitter>();
        services.AddSingleton<EnumCompareToIntrinsicEmitter>();
        services.AddSingleton<IEnumTypeCodeEmitter, EnumTypeCodeEmitter>();
        services.AddSingleton<IEnumValueReturnEmitter, EnumValueReturnEmitter>();
        services.AddSingleton<EnumTypeCodeIntrinsicEmitter>();
        services.AddSingleton<IEnumHasFlagEmitter, EnumHasFlagEmitter>();
        services.AddSingleton<EnumHasFlagIntrinsicEmitter>();
        services.AddSingleton<IEnumGetNamesEmitter, EnumGetNamesEmitter>();
        services.AddSingleton<EnumGetNamesIntrinsicEmitter>();
        services.AddSingleton<IEnumGetNameEmitter, EnumGetNameEmitter>();
        services.AddSingleton<EnumGetNameIntrinsicEmitter>();
        services.AddSingleton<IEnumGetValuesEmitter, EnumGetValuesEmitter>();
        services.AddSingleton<EnumGetValuesIntrinsicEmitter>();
        services.AddSingleton<IEnumIsDefinedEmitter, EnumIsDefinedEmitter>();
        services.AddSingleton<EnumIsDefinedIntrinsicEmitter>();
        services.AddSingleton<IEnumNumericParseEmitter, EnumNumericParseEmitter>();
        services.AddSingleton<IEnumValueBoxEmitter, EnumValueBoxEmitter>();
        services.AddSingleton<IEnumParseEmitter, EnumParseEmitter>();
        services.AddSingleton<EnumParseIntrinsicEmitter>();
        services.AddSingleton<IEnumGetUnderlyingTypeEmitter, EnumGetUnderlyingTypeEmitter>();
        services.AddSingleton<EnumGetUnderlyingTypeIntrinsicEmitter>();
        services.AddSingleton<IEnumNumericFormatter, EnumNumericFormatter>();
        services.AddSingleton<IEnumValueFormatter, EnumValueFormatter>();
        services.AddSingleton<IEnumToStringEmitter, EnumToStringEmitter>();
        services.AddSingleton<EnumToStringIntrinsicEmitter>();
        services.AddSingleton<IEnumToObjectEmitter, EnumToObjectEmitter>();
        services.AddSingleton<EnumToObjectIntrinsicEmitter>();
        services.AddSingleton<IEnumConvertEmitter, EnumConvertEmitter>();
        services.AddSingleton<EnumConvertIntrinsicEmitter>();

        AddRegistration<StringLengthIntrinsicEmitter>(
            services, RuntimeIntrinsic.StringLength);
        AddRegistration<ArrayLengthIntrinsicEmitter>(
            services, RuntimeIntrinsic.ArrayLength);
        AddRegistration<ArrayRankIntrinsicEmitter>(
            services, RuntimeIntrinsic.ArrayRank);
        AddRegistration<ArrayGetLengthIntrinsicEmitter>(
            services, RuntimeIntrinsic.ArrayGetLength);
        AddRegistration<ArrayCopyIntrinsicEmitter>(
            services, RuntimeIntrinsic.ArrayCopy);
        AddRegistration<ArrayClearIntrinsicEmitter>(
            services, RuntimeIntrinsic.ArrayClear);
        AddRegistration<ArrayCloneIntrinsicEmitter>(
            services, RuntimeIntrinsic.ArrayClone);
        AddRegistration<StringCharacterAtIntrinsicEmitter>(
            services, RuntimeIntrinsic.StringCharacterAt);
        AddRegistration<StringSetCharacterUncheckedIntrinsicEmitter>(
            services, RuntimeIntrinsic.StringSetCharacterUnchecked);
        AddRegistration<JSObjectDisposeIntrinsicEmitter>(
            services, RuntimeIntrinsic.JSObjectDispose);
        AddRegistration<JSSubscriptionDisposeIntrinsicEmitter>(
            services, RuntimeIntrinsic.JSSubscriptionDispose);
        AddRegistration<GcCollectIntrinsicEmitter>(services, RuntimeIntrinsic.GcCollect);
        AddRegistration<WeakHandleCreateIntrinsicEmitter>(services, RuntimeIntrinsic.WeakHandleCreate);
        AddRegistration<WeakHandleGetIntrinsicEmitter>(services, RuntimeIntrinsic.WeakHandleGet);
        AddRegistration<WeakHandleSetIntrinsicEmitter>(services, RuntimeIntrinsic.WeakHandleSet);
        AddRegistration<WeakHandleReleaseIntrinsicEmitter>(services, RuntimeIntrinsic.WeakHandleRelease);
        AddRegistration<GcHandleCreateIntrinsicEmitter>(services, RuntimeIntrinsic.GcHandleCreate);
        AddRegistration<GcHandleGetIntrinsicEmitter>(services, RuntimeIntrinsic.GcHandleGet);
        AddRegistration<GcHandleSetIntrinsicEmitter>(services, RuntimeIntrinsic.GcHandleSet);
        AddRegistration<GcHandleReleaseIntrinsicEmitter>(services, RuntimeIntrinsic.GcHandleRelease);
        AddRegistration<GcHandleAddressIntrinsicEmitter>(services, RuntimeIntrinsic.GcHandleAddress);
        AddRegistration<GcGetMetricIntrinsicEmitter>(services, RuntimeIntrinsic.GcGetMetric);
        AddRegistration<GcMetricIsSupportedIntrinsicEmitter>(
            services,
            RuntimeIntrinsic.GcMetricIsSupported);
        AddRegistration<GcWaitForPendingFinalizersIntrinsicEmitter>(
            services,
            RuntimeIntrinsic.GcWaitForPendingFinalizers);
        AddRegistration<ObjectIdentityHashIntrinsicEmitter>(services, RuntimeIntrinsic.ObjectIdentityHash);
        AddRegistration<ValueTypeEqualsIntrinsicEmitter>(
            services,
            RuntimeIntrinsic.ValueTypeEquals);
        AddRegistration<ValueTypeGetHashCodeIntrinsicEmitter>(
            services,
            RuntimeIntrinsic.ValueTypeGetHashCode);
        AddRegistration<SuppressFinalizeIntrinsicEmitter>(
            services, RuntimeIntrinsic.SuppressFinalize);
        AddRegistration<ReRegisterForFinalizeIntrinsicEmitter>(
            services, RuntimeIntrinsic.ReRegisterForFinalize);
        AddRegistration<ReportUnobservedTaskExceptionIntrinsicEmitter>(
            services, RuntimeIntrinsic.ReportUnobservedTaskException);
        AddRegistration<IsReferenceOrContainsReferencesIntrinsicEmitter>(
            services, RuntimeIntrinsic.IsReferenceOrContainsReferences);
        AddRegistration<GetArrayDataReferenceIntrinsicEmitter>(
            services, RuntimeIntrinsic.GetArrayDataReference);
        AddRegistration<NullableGetUnderlyingTypeIntrinsicEmitter>(
            services, RuntimeIntrinsic.NullableGetUnderlyingType);
        AddRegistration<NativeIntegerSizeIntrinsicEmitter>(
            services, RuntimeIntrinsic.NativeIntegerSize);
        AddRegistration<ComponentReallocateIntrinsicEmitter>(
            services, RuntimeIntrinsic.ComponentReallocate);
        AddRegistration<ComponentFreeIntrinsicEmitter>(
            services, RuntimeIntrinsic.ComponentFree);
        AddRegistration<ComponentResourceHandleCreateIntrinsicEmitter>(
            services, RuntimeIntrinsic.ComponentResourceHandleCreate);
        AddRegistration<ComponentResourceHandleGetIntrinsicEmitter>(
            services, RuntimeIntrinsic.ComponentResourceHandleGet);
        AddRegistration<ComponentResourceHandleReleaseIntrinsicEmitter>(
            services, RuntimeIntrinsic.ComponentResourceHandleRelease);
        AddRegistration<UnsafeAddIntrinsicEmitter>(services, RuntimeIntrinsic.UnsafeAdd);
        AddRegistration<UnsafeAsIntrinsicEmitter>(services, RuntimeIntrinsic.UnsafeAs);
        AddRegistration<UnsafeAsRefIntrinsicEmitter>(services, RuntimeIntrinsic.UnsafeAsRef);
        AddRegistration<UnsafeAsRefManagedIntrinsicEmitter>(
            services, RuntimeIntrinsic.UnsafeAsRefManaged);
        AddRegistration<UnsafeNullRefIntrinsicEmitter>(
            services, RuntimeIntrinsic.UnsafeNullRef);
        AddRegistration<UnsafeIsAddressGreaterThanIntrinsicEmitter>(
            services, RuntimeIntrinsic.UnsafeIsAddressGreaterThan);
        AddRegistration<UnsafeSizeOfIntrinsicEmitter>(
            services, RuntimeIntrinsic.UnsafeSizeOf);
        AddRegistration<UnsafeByteOffsetIntrinsicEmitter>(
            services, RuntimeIntrinsic.UnsafeByteOffset);
        AddRegistration<UnsafeAddByteOffsetIntrinsicEmitter>(
            services, RuntimeIntrinsic.UnsafeAddByteOffset);
        AddRegistration<UnsafeObjectAsIntrinsicEmitter>(
            services, RuntimeIntrinsic.UnsafeObjectAs);
        AddRegistration<UnsafeUnboxIntrinsicEmitter>(
            services, RuntimeIntrinsic.UnsafeUnbox);
        AddRegistration<NativeMemoryAllocIntrinsicEmitter>(
            services, RuntimeIntrinsic.NativeMemoryAlloc);
        AddRegistration<NativeMemoryReallocIntrinsicEmitter>(
            services, RuntimeIntrinsic.NativeMemoryRealloc);
        AddRegistration<NativeMemoryFreeIntrinsicEmitter>(
            services, RuntimeIntrinsic.NativeMemoryFree);
        AddRegistration<NativeMemoryAlignedAllocIntrinsicEmitter>(
            services, RuntimeIntrinsic.NativeMemoryAlignedAlloc);
        AddRegistration<NativeMemoryAlignedReallocIntrinsicEmitter>(
            services, RuntimeIntrinsic.NativeMemoryAlignedRealloc);
        AddRegistration<NativeMemoryAlignedFreeIntrinsicEmitter>(
            services, RuntimeIntrinsic.NativeMemoryAlignedFree);
        AddRegistration<SingleToInt32BitsIntrinsicEmitter>(
            services, RuntimeIntrinsic.SingleToInt32Bits);
        AddRegistration<Int32BitsToSingleIntrinsicEmitter>(
            services, RuntimeIntrinsic.Int32BitsToSingle);
        AddRegistration<DoubleToInt64BitsIntrinsicEmitter>(
            services, RuntimeIntrinsic.DoubleToInt64Bits);
        AddRegistration<Int64BitsToDoubleIntrinsicEmitter>(
            services, RuntimeIntrinsic.Int64BitsToDouble);
        AddRegistration<FloatingAbsoluteIntrinsicEmitter>(
            services, RuntimeIntrinsic.FloatingAbsolute);
        AddRegistration<FloatingCeilingIntrinsicEmitter>(
            services, RuntimeIntrinsic.FloatingCeiling);
        AddRegistration<FloatingFloorIntrinsicEmitter>(
            services, RuntimeIntrinsic.FloatingFloor);
        AddRegistration<FloatingTruncateIntrinsicEmitter>(
            services, RuntimeIntrinsic.FloatingTruncate);
        AddRegistration<FloatingRoundIntrinsicEmitter>(
            services, RuntimeIntrinsic.FloatingRound);
        AddRegistration<FloatingSquareRootIntrinsicEmitter>(
            services, RuntimeIntrinsic.FloatingSquareRoot);
        AddRegistration<EnumEqualsIntrinsicEmitter>(services, RuntimeIntrinsic.EnumEquals);
        AddRegistration<EnumGetHashCodeIntrinsicEmitter>(
            services, RuntimeIntrinsic.EnumGetHashCode);
        AddRegistration<EnumCompareToIntrinsicEmitter>(
            services, RuntimeIntrinsic.EnumCompareTo);
        AddRegistration<EnumTypeCodeIntrinsicEmitter>(
            services, RuntimeIntrinsic.EnumGetTypeCode);
        AddRegistration<EnumHasFlagIntrinsicEmitter>(
            services, RuntimeIntrinsic.EnumHasFlag);
        AddRegistration<EnumGetNamesIntrinsicEmitter>(
            services, RuntimeIntrinsic.EnumGetNames);
        AddRegistration<EnumGetNameIntrinsicEmitter>(
            services, RuntimeIntrinsic.EnumGetName);
        AddRegistration<EnumGetValuesIntrinsicEmitter>(
            services, RuntimeIntrinsic.EnumGetValues);
        AddRegistration<EnumIsDefinedIntrinsicEmitter>(
            services, RuntimeIntrinsic.EnumIsDefined);
        AddRegistration<EnumParseIntrinsicEmitter>(
            services, RuntimeIntrinsic.EnumParse);
        AddRegistration<EnumGetUnderlyingTypeIntrinsicEmitter>(
            services, RuntimeIntrinsic.EnumGetUnderlyingType);
        AddRegistration<EnumToStringIntrinsicEmitter>(
            services, RuntimeIntrinsic.EnumToString);
        AddRegistration<EnumToStringIntrinsicEmitter>(
            services, RuntimeIntrinsic.EnumFormat);
        AddRegistration<EnumToObjectIntrinsicEmitter>(
            services, RuntimeIntrinsic.EnumToObject);
        AddRegistration<EnumConvertIntrinsicEmitter>(
            services, RuntimeIntrinsic.EnumConvert);

        services.AddSingleton<IRuntimeIntrinsicEmitterRegistry,
            RuntimeIntrinsicEmitterRegistry>();
        services.AddSingleton<RuntimeIntrinsicCallEmitter>();
        services.AddSingleton<RootPublicationEmitter>();
        services.AddSingleton<IRootPublicationEmitter>(provider =>
            provider.GetRequiredService<RootPublicationEmitter>());
        return services;
    }

    private static void AddRegistration<TEmitter>(
        IServiceCollection services,
        RuntimeIntrinsic intrinsic)
        where TEmitter : class, IRuntimeIntrinsicEmitter =>
        services.AddSingleton<RuntimeIntrinsicEmitterRegistration>(provider =>
            new RuntimeIntrinsicEmitterRegistration(
                intrinsic,
                provider.GetRequiredService<TEmitter>()));
}
