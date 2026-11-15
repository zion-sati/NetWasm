// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
