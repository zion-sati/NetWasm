using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IEnumMetadataRequirementClassifier
{
    EnumMetadataPayload Classify(RuntimeIntrinsic intrinsic, bool hasClosedEnum, string methodName);
}
