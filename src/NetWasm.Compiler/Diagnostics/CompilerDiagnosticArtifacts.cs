using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;

namespace NetWasm.Compiler.Diagnostics;

// The shapes of the JSON diagnostic artifacts. They are named records, not
// anonymous objects, because the compiler serializes JSON only through
// source-generated contracts (CompilerDiagnosticArtifactJsonContext).

internal sealed record CompilerOptionsArtifact(
    string EntryAssemblyPath,
    ImmutableArray<string> ReferencePaths,
    string EntryTypeName,
    string EntryMethodName,
    ImmutableArray<RequestedExport> Exports,
    WasmTarget Target,
    string? WitPath,
    string? WitWorld,
    ImmutableArray<string> SourcePaths,
    string Reproduce);

internal sealed record CompilerSourceArtifact(string Path, bool Exists, string? Copy);

internal sealed record CompilerProvenanceArtifact(
    string Compiler,
    string DotNet,
    string Runtime,
    string OperatingSystem,
    string Architecture,
    string? WasmTools,
    CompilerAssemblyProvenanceArtifact[] Assemblies);

internal sealed record CompilerAssemblyProvenanceArtifact(string Path, string? Sha256, string? PdbSha256);

internal sealed record CompilerEntryArtifact(string? EntryPoint, ImmutableArray<ProgramExport> Exports);

internal sealed record CompilerMetadataArtifact(
    string EntryAssembly,
    CompilerMetadataTypeArtifact[] Types,
    CompilerMetadataMethodArtifact[] Methods);

internal sealed record CompilerMetadataTypeArtifact(string Identity, string FullName, bool IsValueType, int GenericArity);

internal sealed record CompilerMetadataMethodArtifact(
    string Identity,
    int MetadataToken,
    int GenericArity,
    bool HasBody,
    string? DecodeError,
    CompilerCilInstructionArtifact[] Instructions,
    CilExceptionRegion[] ExceptionRegions);

internal sealed record CompilerCilInstructionArtifact(int Offset, int NextOffset, string Operation, string? Operand);

internal sealed record CompilerProgramArtifact(
    string? EntryPoint,
    string[] ReachableMethods,
    string[] ConstructedMethods,
    string[] ReachableTypes,
    string[] ConstructedTypes,
    CompilerTypeTestSiteArtifact[] TypeTestSites,
    string[] ReachableFields,
    ProgramExport[] Exports,
    string[] AllocatingMethods,
    ImmutableHashSet<string> ConstructedAllocatingMethods,
    CompilerDispatchSiteArtifact[] DispatchSites,
    CompilerDelegateBindingArtifact[] DelegateBindings,
    CompilerMethodArtifact[] Methods);

internal sealed record CompilerTypeTestSiteArtifact(string Key, string TargetType, string[] MatchingTypes);

internal sealed record CompilerDispatchSiteArtifact(string Key, string Declaration, CompilerDispatchTargetArtifact[] Targets);

internal sealed record CompilerDispatchTargetArtifact(string Receiver, string Method);

internal sealed record CompilerDelegateBindingArtifact(
    string Invoke,
    string Target,
    CompilerDelegateValueArtifact[] Parameters,
    CompilerDelegateValueArtifact Return);

internal sealed record CompilerDelegateValueArtifact(string Source, string Target, ManagedDelegateAdaptation Adaptation);

internal sealed record CompilerMethodArtifact(
    string Identity,
    CompilerCilInstructionArtifact[] Instructions,
    CompilerExceptionRegionArtifact[] ExceptionRegions,
    CompilerBlockArtifact[] Blocks,
    CompilerInstructionStackArtifact[] InstructionStacks);

internal sealed record CompilerExceptionRegionArtifact(
    CilExceptionRegionKind Kind,
    int TryOffset,
    int TryLength,
    int HandlerOffset,
    int HandlerLength,
    EntityKey? CatchType,
    string? CatchTypeIdentity,
    int? FilterOffset);

internal sealed record CompilerBlockArtifact(
    int Index,
    int StartOffset,
    ImmutableArray<int> Successors,
    ImmutableArray<int> ExceptionalSuccessors,
    string[] EntryStack);

internal sealed record CompilerInstructionStackArtifact(int Offset, string[] Stack);

internal sealed record CompilerLayoutsArtifact(
    WasmTarget Target,
    int AddressSize,
    int ObjectReferenceSize,
    int StaticDataEnd,
    int[] StaticRoots,
    TypeDescriptorLayout[] TypeDescriptors,
    ConstructedTypeDescriptorLayout[] ConstructedTypeDescriptors,
    ValueTypeDescriptorLayout[] ValueTypeDescriptors,
    CompilerDataSegmentArtifact[] DataSegments);

internal sealed record CompilerDataSegmentArtifact(int Address, int Length);

internal sealed record CompilerRootMapsArtifact(CompilerRootMapArtifact[] Direct, CompilerRootMapArtifact[] Constructed);

internal sealed record CompilerRootMapArtifact(
    string Identity,
    string Method,
    CompilerRootSlotArtifact[] Slots,
    CompilerSafepointArtifact[] Safepoints);

internal sealed record CompilerRootSlotArtifact(int Slot, string Source);

internal sealed record CompilerSafepointArtifact(int Offset, string[] Roots, string[] ConstructorRoots);

internal sealed record CompilerEmissionArtifact(int Length, string Sha256, bool WatWritten);

internal sealed record CompilerStatusArtifact(string Status, string Stage);

internal sealed record CompilerFailureArtifact(
    string Status,
    string Stage,
    string? Exception,
    string Message,
    string? StackTrace,
    string? Diagnostic,
    string Reproduce,
    IReadOnlyList<string> AvailableArtifacts);

internal sealed record CompilerObservationsArtifact(string Desktop, string NetWasm);
