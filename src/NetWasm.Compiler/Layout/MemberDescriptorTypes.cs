// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

// The CoreLib types a member descriptor is written as (MemberDescriptorDataBuilder): static data objects whose fields
// (a property's accessors, an accessor's property, a member's name) no IL stores.
internal static class MemberDescriptorTypes
{
    internal const string Method = "System.Reflection.RuntimeMethodInfo";
    internal const string Constructor = "System.Reflection.RuntimeConstructorInfo";
    internal const string Field = "System.Reflection.RuntimeFieldInfo";
    internal const string Property = "System.Reflection.RuntimePropertyInfo";

    internal static ImmutableArray<string> All { get; } = [Method, Constructor, Field, Property];

    // A constructor's descriptor is a RuntimeConstructorInfo, any other method's a RuntimeMethodInfo.
    internal static string Of(MethodDefinitionModel method) => method.Name is ".ctor" or ".cctor" ? Constructor : Method;
}
