// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.ReceiverFlow;

internal interface IRuntimeWrittenFields
{
    // Whether generated code or the runtime writes the field, so its IL stores are not all its values.
    bool IsWritten(FieldDefinitionModel field);
}

// The fields NetWasm's lowerings and runtime write without IL (RECEIVER_TYPE_FLOW_DESIGN.md, 4.3): a delegate's
// target and invocation list (delegate construction and combination are lowered), an exception's message and stack
// trace (the runtime's exceptions), the task and value-task fields JavaScript interop's glue completes, the runtime
// handles a token load makes, whose type, method and field objects are static data, and the member descriptors the
// compiler writes as static data (MemberDescriptorTypes: a property's accessors, an accessor's property, a name).
internal sealed class RuntimeWrittenFields(ITypeRepository types) : IRuntimeWrittenFields
{
    private static readonly HashSet<(string Type, string Field)> Named =
    [
        ("System.Exception", "_message"),
        ("System.Exception", "_stackTrace"),
        ("System.Threading.Tasks.Task`1", "_result"),
        ("System.Threading.Tasks.ValueTask", "_task"),
        ("System.Threading.Tasks.ValueTask`1", "_task"),
    ];

    private static readonly HashSet<string> WholeTypes =
    [
        "System.Delegate", "System.MulticastDelegate", "System.RuntimeTypeHandle", "System.RuntimeMethodHandle",
        "System.RuntimeFieldHandle", "System.ModuleHandle", .. Layout.MemberDescriptorTypes.All,
    ];

    private readonly ITypeRepository _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly Dictionary<EntityKey, string> _names = [];

    public bool IsWritten(FieldDefinitionModel field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!_names.TryGetValue(field.DeclaringType, out var type))
        {
            var definition = _types.GetTypeDefinition(field.DeclaringType);
            type = string.IsNullOrEmpty(definition.Namespace) ? definition.Name : $"{definition.Namespace}.{definition.Name}";
            _names.Add(field.DeclaringType, type);
        }
        return WholeTypes.Contains(type) || Named.Contains((type, field.Name));
    }
}
