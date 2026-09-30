using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed record MemberDescriptorPlanningRequest(
    ImmutableDictionary<string, MethodInstanceModel> MethodDescriptors,
    ImmutableDictionary<string, FieldInstanceModel> FieldDescriptors,
    bool IncludePropertyAssociations,
    bool IncludeNames);

internal sealed record MemberDescriptorPlan(
    ImmutableDictionary<string, MethodInstanceModel> Methods,
    ImmutableDictionary<string, FieldInstanceModel> Fields,
    ImmutableDictionary<string, PropertyInstanceModel> Properties,
    ImmutableHashSet<CliTypeIdentity> RequiredTypes,
    ImmutableHashSet<string> NamedDescriptors,
    ImmutableHashSet<string> NameStrings);
